namespace MorningInk.Domain;

/// <summary>What a section of an edition holds; all kinds share one order, so each can be moved and switched off.</summary>
public enum SectionKind
{
    /// <summary>Articles from the section's sources.</summary>
    Articles,

    /// <summary>"The most important in 5 minutes": the AI's pick of the issue's main news, each linked to its article.</summary>
    Overview,

    /// <summary>The forecast for the edition's region; first in the order, it stands on the title page.</summary>
    Weather,
}
