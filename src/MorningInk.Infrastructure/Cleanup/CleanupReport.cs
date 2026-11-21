namespace MorningInk.Infrastructure.Cleanup;

/// <summary>What one cleanup run deleted.</summary>
public sealed record CleanupReport(int Issues, int SeenArticles, int Images, int Backups);
