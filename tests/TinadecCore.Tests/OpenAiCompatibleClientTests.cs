using System.Net;
using TinadecCore.Services;
using TinadecCore.Storage;
using Tinadec.Contracts.Models;

namespace TinadecCore.Tests;

public sealed class OpenAiCompatibleClientTests
{
    [Fact]
    public void BuildsChatCompletionsEndpointFromOpenAiCompatibleBaseUrl()
    {
        var uri = OpenAiCompatibleClient.BuildChatCompletionsEndpoint("https://api.example.test/v1/");

        Assert.Equal("https://api.example.test/v1/chat/completions", uri.ToString());
    }

    [Fact]
    public async Task BuildsBearerAuthorizedChatCompletionRequest()
    {
        var settings = new StoredModelSettings("https://api.example.test/v1", "test-model", null, DateTimeOffset.UtcNow);
        var messages = new[]
        {
            new MessageDto("msg_1", "sess_1", "user", "Hello", DateTimeOffset.UtcNow)
        };

        using var request = OpenAiCompatibleClient.BuildChatCompletionRequest(settings, "sk-test", messages);
        var body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test", request.Headers.Authorization?.Parameter);
        Assert.Contains("\"model\":\"test-model\"", body);
        Assert.Contains("\"role\":\"user\"", body);
    }
}
