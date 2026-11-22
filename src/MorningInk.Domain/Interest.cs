namespace MorningInk.Domain;

/// <summary>How much an edition wants a topic.</summary>
public enum InterestWeight
{
    /// <summary>Left out like a filter.</summary>
    Hide,

    Less,

    Prefer,

    StrongPrefer,
}

/// <summary>A topic of an edition, recognized by its keywords in headline and teaser, e.g. "Homelab": Proxmox, NAS.</summary>
public sealed class Interest
{
    public string Topic { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public InterestWeight Weight { get; set; } = InterestWeight.Prefer;
}
