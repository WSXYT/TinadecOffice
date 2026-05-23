using System.Net.Http.Json;
using Tinadec.Contracts.Models;
using TinadecCore.Abstractions;

namespace TinadecCore.Services;

public sealed class CodeToolClient(HttpClient httpClient) : ICodeToolClient
{
    public async Task<CodeToolExecuteResultDto> ExecuteAsync(
        ToolDescriptorDto tool,
        CodeToolExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(tool.Source, "codex-rust", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(tool.Source, "code", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Tool '{tool.Id}' is not provided by the Codex glue adapter.");
        }

        using var response = await httpClient.PostAsJsonAsync(tool.ExecuteEndpoint, request, TinadecJson.Options, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<CodeToolExecuteResultDto>(TinadecJson.Options, cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException($"Code tool '{tool.Id}' returned an empty response.");
        }

        return result;
    }
}
