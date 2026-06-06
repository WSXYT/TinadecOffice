using Tinadec.Contracts.Models;
using TinadecCore.Abstractions;
using TinadecCore.Services;
using TinadecCore.Storage;

namespace TinadecCore.Tests;

public sealed class CoreCapabilityAdapterTests
{
    [Fact]
    public void CodexCapabilityProviderRegistersKernelBackedCapabilities()
    {
        var provider = new CodexCapabilityProvider();

        var capabilities = provider.ListCapabilities();

        Assert.Contains(capabilities, tool => tool.Id == "search_files" && tool.Source == "codex-rust");
        Assert.Contains(capabilities, tool => tool.Id == "read_file" && tool.Capabilities.Contains("codex-rust.active"));
        Assert.Contains(capabilities, tool => tool.Id == "grep_content" && tool.Risk == "read-only");
        Assert.Contains(capabilities, tool => tool.Id == "apply_patch" && tool.RequiresApproval);
        Assert.Contains(capabilities, tool => tool.Id == "sandbox_exec" && tool.RequiresApproval);
    }

    [Fact]
    public void CapabilityPolicyKeepsReadOnlyAutomaticAndMutatingApprovalGated()
    {
        var policy = new CapabilityPolicyService();
        var provider = new CodexCapabilityProvider();
        var readFile = provider.ListCapabilities().Single(tool => tool.Id == "read_file");
        var applyPatch = provider.ListCapabilities().Single(tool => tool.Id == "apply_patch");

        Assert.False(policy.Evaluate("approval", readFile).Required);
        Assert.True(policy.IsReadOnly(readFile));
        Assert.True(policy.Evaluate("approval", applyPatch).Required);
        Assert.False(policy.IsReadOnly(applyPatch));
    }

    [Fact]
    public void PromptContextCapabilityProviderRegistersReadOnlyCoreTool()
    {
        var registry = new ToolRegistryService();

        var tool = Assert.Single(registry.ListTools("agent-context"), item => item.Id == "prompt_context_resolve");

        Assert.Equal("core", tool.Source);
        Assert.Equal("read-only", tool.Risk);
        Assert.False(tool.RequiresApproval);
        Assert.Contains("prompt.context.resolve", tool.Capabilities);
    }

    [Fact]
    public async Task CodexInvocationAdapterTranslatesCoreInvocationToCodeClient()
    {
        var client = new RecordingCodeToolClient();
        var adapter = new CodexToolInvocationAdapter(client);
        var tool = new CodexCapabilityProvider().ListCapabilities().Single(item => item.Id == "read_file");
        var request = new CodeToolExecuteRequest("sess_1", "run_1", "node_1", null, "D:\\repo", new Dictionary<string, object?>());

        var result = await adapter.InvokeAsync(tool, request);

        Assert.True(adapter.CanInvoke(tool));
        Assert.Equal("native", result.Status);
        Assert.Equal(tool.Id, client.ToolId);
        Assert.Equal(request.RunId, client.Request?.RunId);
    }

    [Fact]
    public async Task CodeInvocationAdapterAcceptsCodeSuiteTools()
    {
        var client = new RecordingCodeToolClient();
        var adapter = new CodexToolInvocationAdapter(client);
        var tool = new CodeCapabilityProvider().ListCapabilities().Single(item => item.Id == "project_template_scaffold");
        var request = new CodeToolExecuteRequest("sess_1", "run_1", "node_1", null, "D:\\repo", new Dictionary<string, object?>());

        var result = await adapter.InvokeAsync(tool, request);

        Assert.True(adapter.CanInvoke(tool));
        Assert.Equal("native", result.Status);
        Assert.Equal("project_template_scaffold", client.ToolId);
    }

    [Fact]
    public async Task CoreInvocationAdapterResolvesPromptContextWithoutReturningFullPrompt()
    {
        var store = new CoreStore(Path.Combine(Path.GetTempPath(), $"tinadec-core-adapter-{Guid.NewGuid():N}.db"));
        store.Initialize();
        var service = new PromptContextService(
            store,
            new ToolRegistryService(),
            new NullPromptContextPlannerRuntime());
        var adapter = new CoreToolInvocationAdapter(service);
        var tool = new PromptContextCapabilityProvider().ListCapabilities().Single(item => item.Id == "prompt_context_resolve");

        var result = await adapter.InvokeAsync(tool, new CodeToolExecuteRequest(
            "sess_1",
            "run_1",
            "node_1",
            null,
            Directory.GetCurrentDirectory(),
            new Dictionary<string, object?>
            {
                ["agent_id"] = "agent_meeting",
                ["mode"] = "plan-first",
                ["user_content"] = "simple preview"
            }));

        Assert.True(adapter.CanInvoke(tool));
        Assert.Equal("completed", result.Status);
        Assert.True(result.Data.ContainsKey("fragment_ids"));
        Assert.True(result.Data.ContainsKey("estimated_tokens"));
        Assert.False(result.Data.ContainsKey("system_prompt"));
        Assert.DoesNotContain("TinadecCode prompt context", result.Data.Values.Select(value => value?.ToString()));
    }

    private sealed class RecordingCodeToolClient : ICodeToolClient
    {
        public string? ToolId { get; private set; }
        public CodeToolExecuteRequest? Request { get; private set; }

        public Task<CodeToolExecuteResultDto> ExecuteAsync(
            ToolDescriptorDto tool,
            CodeToolExecuteRequest request,
            CancellationToken cancellationToken = default)
        {
            ToolId = tool.Id;
            Request = request;
            return Task.FromResult(new CodeToolExecuteResultDto(
                tool.Id,
                "native",
                "Recorded Codex invocation.",
                ["adapter:codex-rust"],
                new Dictionary<string, object?>(),
                false,
                null));
        }
    }

    private sealed class NullPromptContextPlannerRuntime : IPromptContextPlannerRuntime
    {
        public Task<PromptContextPlanDto?> TryCreatePlanAsync(
            PromptContextPlanningInput input,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PromptContextPlanDto?>(null);
        }
    }
}
