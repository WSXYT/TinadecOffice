using System.Text.Json;

namespace TinadecTools.Tools.Mcp;

internal sealed class McpServerRepository
{
    public const string ConfigPathEnvironmentVariable = "TINADEC_TOOLS_MCP_CONFIG";

    public string ConfigPath { get; }

    public McpServerRepository(string? configPath = null)
    {
        ConfigPath = ResolveConfigPath(configPath);
    }

    public async Task<IReadOnlyList<McpServerConfig>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigPath))
            return [];

        await using var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var file = await JsonSerializer.DeserializeAsync(stream, McpJsonContext.Default.McpServersFile, cancellationToken)
            .ConfigureAwait(false);

        return file?.Servers.Where(IsValid).ToArray() ?? [];
    }

    public async Task<McpServerConfig> GetRequiredAsync(string serverId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        var servers = await ListAsync(cancellationToken).ConfigureAwait(false);
        return servers.FirstOrDefault(server => string.Equals(server.Id, serverId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"MCP server '{serverId}' was not found in {ConfigPath}.");
    }

    private static bool IsValid(McpServerConfig server)
    {
        return !string.IsNullOrWhiteSpace(server.Id) && !string.IsNullOrWhiteSpace(server.Command);
    }

    private static string ResolveConfigPath(string? configPath)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConfigPathEnvironmentVariable);
        var rawPath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : !string.IsNullOrWhiteSpace(fromEnvironment)
                ? fromEnvironment
                : Path.Combine(Environment.CurrentDirectory, "mcp_servers.json");

        return Path.GetFullPath(rawPath);
    }
}
