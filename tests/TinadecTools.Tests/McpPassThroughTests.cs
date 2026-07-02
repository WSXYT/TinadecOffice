using System.Text.Json;
using TinadecTools.Tools.Mcp;

namespace TinadecTools.Tests;

public sealed class McpPassThroughTests : IAsyncLifetime
{
    private string? _configPath;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await McpRuntime.DisposeAsync();
    }

    [Fact]
    public async Task McpList_ReturnsToolsFromConfiguredServer()
    {
        ConfigureRuntime();

        var response = await McpListTool.HandleAsync(new McpListParams(), CancellationToken.None);

        var server = Assert.Single(response.Servers);
        Assert.Equal("mock", server.Id);
        Assert.Equal("connected", server.Status);
        Assert.Contains(server.Tools, tool => tool.Name == "echo" && tool.Description?.Contains("Echoes") == true);
    }

    [Fact]
    public async Task McpSearch_FuzzyMatchesToolNameAndDescription()
    {
        ConfigureRuntime();

        var response = await McpSearchTool.HandleAsync(new McpSearchParams
        {
            Query = "read file",
            IncludeSchema = false
        }, CancellationToken.None);

        var result = Assert.Single(response.Results, result => result.Tool.Name == "read_file");
        Assert.Equal("mock", result.ServerId);
        Assert.True(result.Score > 0);
    }

    [Fact]
    public async Task McpInvoke_CallsToolAndReturnsRawMcpResult()
    {
        ConfigureRuntime();
        using var arguments = JsonDocument.Parse("{\"message\":\"hello\"}");

        var response = await McpInvokeTool.HandleAsync(new McpInvokeParams
        {
            ServerId = "mock",
            ToolName = "echo",
            Arguments = arguments.RootElement.Clone()
        }, CancellationToken.None);

        Assert.Equal("mock", response.ServerId);
        Assert.Equal("echo", response.ToolName);
        Assert.True(response.Success, response.Error);
        Assert.Contains("echo:hello", response.Result.GetRawText());
    }

    private void ConfigureRuntime()
    {
        _configPath = Path.Combine(Path.GetTempPath(), $"tinadec-tools-mcp-{Guid.NewGuid():N}.json");
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "mcp-mock-server.js");
        var config = new McpServersFile
        {
            Servers =
            [
                new McpServerConfig
                {
                    Id = "mock",
                    Name = "Mock MCP",
                    Command = "node",
                    Args = [fixturePath]
                }
            ]
        };

        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, McpJsonContext.Default.McpServersFile));
        McpRuntime.ConfigureForTests(_configPath);
    }
}
