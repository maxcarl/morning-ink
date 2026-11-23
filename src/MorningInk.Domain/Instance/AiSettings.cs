namespace MorningInk.Domain;

/// <summary>The AI provider for the whole instance: an OpenAI-compatible endpoint such as Ollama's. One row at most.</summary>
public class AiSettings
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Whether the AI is used at all; off pauses every AI step but keeps the provider's settings.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base address, e.g. "http://192.168.1.20:11434/v1"; null means no AI.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name as the provider knows it, e.g. "qwen2.5:7b".</summary>
    public string? Model { get; set; }

    /// <summary>Stored encrypted; never shown again once saved. Not needed for Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whether the provider runs on the user's own hardware; sources marked "local AI only" use it only then.</summary>
    public bool IsLocal { get; set; } = true;

    /// <summary>Whether articles get a short summary above their text.</summary>
    public bool Summaries { get; set; } = true;

    /// <summary>Most AI requests in any 24 hours; null means no limit. Above it, issues come without AI extras.</summary>
    public int? DailyLimit { get; set; }

    /// <summary>
    /// The admin's own instructions for summaries; null uses the default from the repository, so updates to it arrive.
    /// The rules that keep the article text as data are added by the code and cannot be edited away.
    /// </summary>
    public string? SummaryPrompt { get; set; }

    /// <summary>
    /// The admin's own instructions for the issue's overview, like <see cref="SummaryPrompt"/>; null uses the default.
    /// The answer's format and the rule that articles are data are added by the code.
    /// </summary>
    public string? OverviewPrompt { get; set; }
}
