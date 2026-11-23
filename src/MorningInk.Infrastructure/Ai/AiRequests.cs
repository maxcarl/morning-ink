using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Ai;

/// <summary>What one AI step asks, and how the request shows up in the admin's record.</summary>
/// <param name="Instructions">The system prompt: the step's instructions with the fixed rules appended.</param>
/// <param name="Data">The fenced data; never mixed into the instructions.</param>
/// <param name="SourceName">Source or edition the request was for, as the record shows it.</param>
/// <param name="Subject">The article's title, or what a request over many articles did.</param>
/// <param name="Url">The article's address; empty for a request over many articles.</param>
internal sealed record AiQuestion(string Instructions, string Data, int MaxOutputTokens, Guid UserId, string SourceName, string Subject, string Url = "");

/// <summary>
/// The way every AI step asks: only when the AI is set up and switched on, at most up to the daily limit, every
/// request recorded in <see cref="AiUsage"/>. The step fences its data in and checks the answer itself.
/// </summary>
/// <remarks>
/// One instance per issue run (scoped): once the provider failed or the limit is reached, no step of the run asks
/// again, so an AI that is down costs one timeout, not one per article.
/// </remarks>
internal sealed partial class AiRequests(AiSettingsStore store, AiChat chat, AiUsage usage, ILogger<AiRequests> logger)
{
    private const float Temperature = 0.2f;

    private AiSettings? _settings;
    private bool _unavailable;

    /// <summary>The settings when the AI is set up and switched on; null otherwise.</summary>
    public async Task<AiSettings?> SettingsAsync(CancellationToken cancellationToken)
    {
        _settings ??= await store.LoadAsync(cancellationToken);
        return _settings is { Enabled: true, Endpoint: not null, Model: not null } ? _settings : null;
    }

    /// <summary>The checked answer; null at the limit, when the provider fails or the answer does not check out.</summary>
    /// <param name="check">Turns the untrusted answer into the result, or null when it is not what was asked for.</param>
    public async Task<T?> AskAsync<T>(AiSettings settings, AiQuestion question, Func<string?, T?> check, CancellationToken cancellationToken)
        where T : class
    {
        if (_unavailable)
        {
            return null;
        }

        if (await usage.LimitReachedAsync(settings.DailyLimit, cancellationToken))
        {
            _unavailable = true;
            LogLimit(logger);
            return null;
        }

        T? answer = null;
        try
        {
            ChatMessage[] messages = [new(ChatRole.System, question.Instructions), new(ChatRole.User, question.Data)];
            var response = await chat.Create(settings).GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = question.MaxOutputTokens, Temperature = Temperature }, cancellationToken);
            answer = check(response.Text);
            return answer;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // Whatever goes wrong, the AI never costs the issue: each step goes on without its extra.
            _unavailable = true;
            LogUnavailable(logger, e.Message);
            return null;
        }
        finally
        {
            await usage.RecordAsync(new AiCall
            {
                At = usage.Now,
                Provider = AiUsage.ProviderOf(settings.Endpoint ?? string.Empty),
                Model = settings.Model ?? string.Empty,
                IsLocal = settings.IsLocal,
                UserId = question.UserId,
                SourceName = AiText.Shorten(question.SourceName, 200),
                ArticleTitle = AiText.Shorten(question.Subject, 500),
                ArticleUrl = AiText.Shorten(question.Url, 2000),
                Characters = question.Data.Length,
                Succeeded = answer is not null,
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI daily limit reached, the rest of this issue comes without AI")]
    private static partial void LogLimit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI not available, the rest of this issue comes without AI: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
