using System.Text.Json.Nodes;
using TinadecCore.Storage;
using Tinadec.Contracts.Models;
using TinadecCore.Abstractions;

namespace TinadecCore.Services;

public sealed class OrchestratorService
{
    private readonly CoreStore _store;
    private readonly EventHub _events;
    private readonly IAgentWorkflowRuntime _workflowRuntime;
    private readonly IToolRegistry _tools;
    private readonly ICapabilityPolicy _capabilityPolicy;
    private readonly IReadOnlyList<IToolInvocationAdapter> _invocationAdapters;

    public OrchestratorService(
        CoreStore store,
        EventHub events,
        IAgentWorkflowRuntime workflowRuntime,
        IToolRegistry tools,
        ICapabilityPolicy capabilityPolicy,
        IEnumerable<IToolInvocationAdapter> invocationAdapters)
    {
        _store = store;
        _events = events;
        _workflowRuntime = workflowRuntime;
        _tools = tools;
        _capabilityPolicy = capabilityPolicy;
        _invocationAdapters = invocationAdapters.ToArray();
    }

    public OrchestrationSnapshotDto CreateRunForMessage(string sessionId, string userMessageId, string userContent)
    {
        var snapshot = _store.CreateOrchestrationRun(sessionId, userMessageId, userContent);
        if (snapshot.Run is null)
        {
            return snapshot;
        }

        Publish("run.started", sessionId, new JsonObject
        {
            ["run_id"] = snapshot.Run.Id,
            ["summary"] = snapshot.Run.Summary,
            ["status"] = snapshot.Run.Status
        }, ["agent.run"]);

        if (snapshot.Graph is not null)
        {
            Publish("task_graph.created", sessionId, new JsonObject
            {
                ["run_id"] = snapshot.Run.Id,
                ["graph_id"] = snapshot.Graph.Id,
                ["node_count"] = snapshot.Nodes.Count
            }, ["task_graph.create"]);
        }

        foreach (var assignment in snapshot.Assignments)
        {
            Publish("task.assigned", sessionId, new JsonObject
            {
                ["run_id"] = assignment.RunId,
                ["task_node_id"] = assignment.TaskNodeId,
                ["agent_id"] = assignment.AgentId,
                ["agent_type"] = assignment.AgentType,
                ["permission_mode"] = assignment.PermissionMode
            }, ["task.assign", "agent.execution"]);
        }

        var workflow = _workflowRuntime.Compile(snapshot);
        Publish("agent.workflow.compiled", sessionId, new JsonObject
        {
            ["run_id"] = workflow.RunId,
            ["runtime"] = workflow.Runtime,
            ["step_count"] = workflow.Steps.Count
        }, ["agent.workflow", "runtime.core-workflow"]);

        foreach (var result in snapshot.StepResults)
        {
            Publish("step.result.created", sessionId, new JsonObject
            {
                ["run_id"] = result.RunId,
                ["task_node_id"] = result.TaskNodeId,
                ["agent_id"] = result.AgentId,
                ["status"] = result.Status
            }, ["step.result"]);
        }

        foreach (var finding in snapshot.SupervisionFindings)
        {
            Publish("supervision.checked", sessionId, new JsonObject
            {
                ["run_id"] = finding.RunId,
                ["severity"] = finding.Severity,
                ["category"] = finding.Category,
                ["status"] = finding.Status
            }, ["supervisor.check"]);
        }

        foreach (var pack in snapshot.ContextPacks)
        {
            Publish("context.pack.created", sessionId, new JsonObject
            {
                ["run_id"] = pack.RunId,
                ["context_pack_id"] = pack.Id,
                ["token_budget"] = pack.TokenBudget,
                ["compression_ratio"] = pack.CompressionRatio
            }, ["context.compact"]);
        }

        return snapshot;
    }

    public async Task DispatchReadOnlyToolsAsync(
        OrchestrationSnapshotDto snapshot,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.Run is null)
        {
            return;
        }

        var workflow = _workflowRuntime.Compile(snapshot);
        var session = _store.ListSessions(null).FirstOrDefault(item => item.Id == snapshot.Run.SessionId);
        var project = session is null
            ? null
            : _store.ListProjects().FirstOrDefault(item => item.Id == session.ProjectId);
        var cwd = project?.Path ?? Directory.GetCurrentDirectory();

        foreach (var step in workflow.Steps)
        {
            foreach (var toolId in step.ToolIds)
            {
                var tool = _tools.Resolve(toolId);
                if (tool is null || !_capabilityPolicy.IsReadOnly(tool))
                {
                    continue;
                }

                Publish("tool.execution.requested", snapshot.Run.SessionId, new JsonObject
                {
                    ["run_id"] = snapshot.Run.Id,
                    ["tool_id"] = tool.Id,
                    ["task_node_id"] = step.TaskNodeId,
                    ["auto_dispatch"] = true
                }, ["tool.execution", "agent.workflow"]);

                try
                {
                    var adapter = _invocationAdapters.FirstOrDefault(item => item.CanInvoke(tool));
                    if (adapter is null)
                    {
                        throw new InvalidOperationException($"No Core invocation adapter is registered for tool source '{tool.Source}'.");
                    }

                    var result = await adapter.InvokeAsync(
                        tool,
                        new CodeToolExecuteRequest(
                            snapshot.Run.SessionId,
                            snapshot.Run.Id,
                            step.TaskNodeId,
                            null,
                            cwd,
                            BuildReadOnlyArguments(tool.Id, userContent)),
                        cancellationToken);

                    var stepResult = _store.AddStepResult(
                        snapshot.Run.Id,
                        step.TaskNodeId,
                        step.AgentId,
                        result.Status,
                        result.Summary,
                        result.Evidence);

                    Publish(result.Status is "failed" or "blocked" ? "tool.execution.failed" : "tool.execution.completed",
                        snapshot.Run.SessionId,
                        new JsonObject
                        {
                            ["run_id"] = snapshot.Run.Id,
                            ["tool_id"] = tool.Id,
                            ["task_node_id"] = step.TaskNodeId,
                            ["status"] = result.Status,
                            ["step_result_id"] = stepResult.Id
                        },
                        ["tool.execution", "step.result"]);
                }
                catch (Exception ex)
                {
                    var stepResult = _store.AddStepResult(
                        snapshot.Run.Id,
                        step.TaskNodeId,
                        step.AgentId,
                        "failed",
                        $"Read-only tool dispatch failed: {ex.Message}",
                        ["tool dispatch failed", tool.Id]);

                    Publish("tool.execution.failed", snapshot.Run.SessionId, new JsonObject
                    {
                        ["run_id"] = snapshot.Run.Id,
                        ["tool_id"] = tool.Id,
                        ["task_node_id"] = step.TaskNodeId,
                        ["status"] = "failed",
                        ["step_result_id"] = stepResult.Id
                    }, ["tool.execution", "step.result"]);
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildReadOnlyArguments(string toolId, string userContent)
    {
        return toolId switch
        {
            "search_files" => new Dictionary<string, object?>
            {
                ["query"] = string.IsNullOrWhiteSpace(userContent) ? "Tinadec" : userContent,
                ["limit"] = 10
            },
            _ => new Dictionary<string, object?>()
        };
    }

    private void Publish(string type, string sessionId, JsonObject payload, IReadOnlyList<string> capabilities)
    {
        var envelope = _store.AppendNewEvent(type, sessionId, payload, capabilities);
        _events.Publish(envelope);
    }
}
