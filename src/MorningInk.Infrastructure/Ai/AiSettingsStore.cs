using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Ai;

/// <summary>What the settings page saves; an empty endpoint switches the AI off.</summary>
/// <param name="ApiKey">Empty keeps the saved key, as with the mail password.</param>
/// <param name="Enabled">Off pauses the AI without forgetting address, model and key.</param>
/// <param name="DailyLimit">Most requests in 24 hours; null or empty for no limit.</param>
/// <param name="SummaryPrompt">Own instructions for summaries; empty or the unchanged default keeps the default.</param>
/// <param name="OverviewPrompt">Own instructions for the issue's overview, the same way.</param>
public sealed record AiSettingsInput(
    string? Endpoint, string? Model, string? ApiKey, bool IsLocal, bool Summaries, bool Enabled = true, int? DailyLimit = null, string? SummaryPrompt = null, string? OverviewPrompt = null);

/// <summary>The instance's AI provider settings, in the database.</summary>
public sealed class AiSettingsStore(MorningInkDbContext db, HostAllowlist allowlist)
{
    public const int MaxPromptLength = 4000;

    /// <summary>The default summary instructions, which the settings page shows to start editing from.</summary>
    public static string DefaultSummaryPrompt => AiSummaryProcessor.DefaultPrompt;

    /// <summary>The default overview instructions, likewise.</summary>
    public static string DefaultOverviewPrompt => AiOverviewWriter.DefaultPrompt;

    private const string ApiPath = "/v1";

    public async Task<AiSettings> LoadAsync(CancellationToken cancellationToken) =>
        await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new AiSettings();

    /// <summary>Saves the settings, or returns the invalid fields: Endpoint, Model, DailyLimit, SummaryPrompt, OverviewPrompt.</summary>
    /// <param name="adminId">The admin saving; a local provider's host is allowed in the home network in their name.</param>
    public async Task<IReadOnlyList<string>> SaveAsync(AiSettingsInput input, Guid adminId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(input.Endpoint) && (endpoint = ApiBase(input.Endpoint)) is null)
        {
            errors.Add("Endpoint");
        }

        var model = input.Model?.Trim();
        if (endpoint is not null && string.IsNullOrEmpty(model))
        {
            errors.Add("Model");
        }

        if (input.DailyLimit is < 1)
        {
            errors.Add("DailyLimit");
        }

        var summaryPrompt = input.SummaryPrompt?.Trim();
        if (summaryPrompt?.Length > MaxPromptLength)
        {
            errors.Add("SummaryPrompt");
        }

        var overviewPrompt = input.OverviewPrompt?.Trim();
        if (overviewPrompt?.Length > MaxPromptLength)
        {
            errors.Add("OverviewPrompt");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var settings = await db.SettingsRowAsync<AiSettings>(cancellationToken);

        settings.Endpoint = endpoint?.AbsoluteUri.TrimEnd('/');
        settings.Model = endpoint is null ? null : model;
        settings.IsLocal = input.IsLocal;
        settings.Summaries = input.Summaries;
        settings.Enabled = input.Enabled;
        settings.DailyLimit = input.DailyLimit;

        settings.SummaryPrompt = OwnPrompt(summaryPrompt, AiSummaryProcessor.DefaultPrompt);
        settings.OverviewPrompt = OwnPrompt(overviewPrompt, AiOverviewWriter.DefaultPrompt);
        if (!string.IsNullOrEmpty(input.ApiKey))
        {
            settings.ApiKey = input.ApiKey;
        }

        await db.SaveChangesAsync(cancellationToken);

        // A provider on the user's own hardware usually sits in the home network, which is blocked otherwise.
        if (endpoint is not null && input.IsLocal)
        {
            await allowlist.AllowServiceAsync(endpoint, adminId, cancellationToken);
        }

        return [];
    }

    // "http://host:11434" and "http://host:11434/v1" both mean Ollama's OpenAI-compatible API.
    private static Uri? ApiBase(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        return uri.AbsolutePath is "/" or "" ? new Uri(uri, ApiPath) : uri;
    }

    // Stored only when it differs from the default, so improvements to the default reach everyone else.
    private static string? OwnPrompt(string? typed, string defaultPrompt) =>
        string.IsNullOrEmpty(typed) || typed == defaultPrompt ? null : typed;
}
