using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using MorningInk.Domain;
using OpenAI;

namespace MorningInk.Infrastructure.Ai;

/// <summary>Creates chat clients for the configured OpenAI-compatible provider (Ollama, LM Studio, OpenRouter, OpenAI).</summary>
/// <remarks>
/// Requests go through the "ai" HTTP client, whose connections pass the same home-network check as everything else;
/// a local provider's host is allowed when the admin saves it (see <see cref="AiSettingsStore"/>).
/// </remarks>
public sealed class AiChat(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "ai";

    // A small model on a CPU can take minutes for one answer.
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);

    // Ollama ignores the key, but the client library insists on one.
    private const string NoKey = "none";

    public IChatClient Create(AiSettings settings)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(settings.Endpoint ?? throw new InvalidOperationException("No AI endpoint is configured.")),
            Transport = new HttpClientPipelineTransport(httpClients.CreateClient(HttpClientName)),
            NetworkTimeout = NetworkTimeout,

            // One retry at most: when the AI is down, the issue must not wait for many.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
        };
        var key = new ApiKeyCredential(string.IsNullOrEmpty(settings.ApiKey) ? NoKey : settings.ApiKey);
        return new OpenAIClient(key, options).GetChatClient(settings.Model).AsIChatClient();
    }

    /// <summary>Asks the provider for a one-word answer; null when it works, otherwise why not.</summary>
    public async Task<string?> TestAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Create(settings).GetResponseAsync("Reply with the single word OK.", new ChatOptions { MaxOutputTokens = 10 }, cancellationToken);
            return string.IsNullOrWhiteSpace(response.Text) ? "The AI gave an empty answer." : null;
        }
        catch (Exception e) when (e is ClientResultException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return e.Message;
        }
    }
}
