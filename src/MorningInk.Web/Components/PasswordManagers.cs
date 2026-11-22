namespace MorningInk.Web.Components;

/// <summary>Attributes that keep password managers away from fields that are not the sign-in to Morning Ink.</summary>
/// <remarks>
/// Password managers ignore autocomplete="off" on purpose, so each has its own marker. Without them, a Kindle address
/// or the mail server's login looks like a sign-in: the manager offers to fill it, or saves it as the login for this site.
/// Use with @attributes on every e-mail, user name or password field outside sign-in and setup. Bitwarden still treats
/// type="email" as a user name despite these markers, so such fields are type="text" with inputmode="email".
/// </remarks>
public static class PasswordManagers
{
    public static IReadOnlyDictionary<string, object> Ignore { get; } = new Dictionary<string, object>
    {
        ["data-bwignore"] = "true",   // Bitwarden
        ["data-1p-ignore"] = "true",  // 1Password
        ["data-lpignore"] = "true",   // LastPass
        ["data-form-type"] = "other", // Dashlane
    };
}
