namespace TinadecTools.Tools.Mcp;

internal static class McpRuntime
{
    public static McpServerRepository Repository { get; private set; } = new();
    public static McpClientPool ClientPool { get; private set; } = new();

    public static void ConfigureForTests(string? configPath = null, McpClientPool? clientPool = null)
    {
        Repository = new McpServerRepository(configPath);
        ClientPool = clientPool ?? new McpClientPool();
    }

    public static async ValueTask DisposeAsync()
    {
        await ClientPool.DisposeAsync().ConfigureAwait(false);
    }
}
