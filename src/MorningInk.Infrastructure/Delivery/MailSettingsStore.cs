using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Delivery;

/// <summary>Mail settings as typed in the UI. A null or empty password keeps the stored one.</summary>
public sealed record MailSettingsInput(string? Host, string? Port, MailSecurity Security, string? Username, string? Password, string? FromAddress, string? FromName);

/// <summary>The fields an environment variable sets; the UI shows them read-only. Computed once from the configuration.</summary>
/// <param name="Fields">Names like "Host" or "Password", as in the "Smtp" section.</param>
public sealed record MailSettingsLocks(IReadOnlySet<string> Fields);

/// <summary>
/// Mail settings from the database, overridden field by field by the configuration ("Smtp" section, e.g. environment
/// variables): whoever versions their setup in a compose file keeps control, and the UI fills in the rest.
/// </summary>
public sealed class MailSettingsStore(MorningInkDbContext db, IOptions<SmtpOptions> configured, MailSettingsLocks locks)
{
    public const string HostField = nameof(MailSettings.Host);
    public const string PortField = nameof(MailSettings.Port);
    public const string SecurityField = nameof(MailSettings.Security);
    public const string UsernameField = nameof(MailSettings.Username);
    public const string PasswordField = nameof(MailSettings.Password);
    public const string FromAddressField = nameof(MailSettings.FromAddress);
    public const string FromNameField = nameof(MailSettings.FromName);

    private const int MaxTextLength = 200;

    public IReadOnlySet<string> Locked => locks.Fields;

    /// <summary>The stored settings, for the form; the password itself is never handed to a page, only whether there is one.</summary>
    public async Task<MailSettings> LoadAsync(CancellationToken cancellationToken) =>
        await db.MailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new MailSettings();

    /// <summary>What mail delivery uses: stored values, each one replaced by the configuration where it sets it.</summary>
    public async Task<SmtpOptions> EffectiveAsync(CancellationToken cancellationToken)
    {
        var stored = await LoadAsync(cancellationToken);
        var config = configured.Value;
        return new SmtpOptions
        {
            Host = Pick(HostField, config.Host, stored.Host),
            Port = Locked.Contains(PortField) ? config.Port : stored.Port,
            Security = Locked.Contains(SecurityField) ? config.Security : stored.Security,
            Username = Pick(UsernameField, config.Username, stored.Username),
            Password = Pick(PasswordField, config.Password, stored.Password),
            FromAddress = Pick(FromAddressField, config.FromAddress, stored.FromAddress),
            FromName = Pick(FromNameField, config.FromName, stored.FromName) ?? config.FromName,
            MaxMessageBytes = config.MaxMessageBytes,
            Timeout = config.Timeout,
        };
    }

    /// <summary>Saves what is not locked; returns the invalid fields (same names as the field constants).</summary>
    public async Task<IReadOnlyList<string>> SaveAsync(MailSettingsInput input, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var host = input.Host?.Trim();
        var fromAddress = input.FromAddress?.Trim();
        if (host?.Length > MaxTextLength || host?.Any(char.IsWhiteSpace) == true)
        {
            errors.Add(HostField);
        }

        if (!int.TryParse(input.Port, out var port) || port is < 1 or > 65535)
        {
            errors.Add(PortField);
        }

        if (!string.IsNullOrEmpty(fromAddress) && !(MailAddress.TryCreate(fromAddress, out var parsed) && parsed.Address == fromAddress))
        {
            errors.Add(FromAddressField);
        }

        if (input.Username?.Length > MaxTextLength || input.FromName?.Length > MaxTextLength || input.Password?.Length > MaxTextLength)
        {
            errors.Add(UsernameField);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var settings = await db.SettingsRowAsync<MailSettings>(cancellationToken);

        // Locked fields belong to the configuration; the form shows them read-only and their values are ignored here.
        Set(HostField, () => settings.Host = Empty(host));
        Set(PortField, () => settings.Port = port);
        Set(SecurityField, () => settings.Security = input.Security);
        Set(UsernameField, () => settings.Username = Empty(input.Username?.Trim()));
        Set(FromAddressField, () => settings.FromAddress = Empty(fromAddress));
        Set(FromNameField, () => settings.FromName = Empty(input.FromName?.Trim()) ?? new MailSettings().FromName);
        if (!string.IsNullOrEmpty(input.Password))
        {
            Set(PasswordField, () => settings.Password = input.Password);
        }

        await db.SaveChangesAsync(cancellationToken);
        return [];
    }

    private string? Pick(string field, string? fromConfiguration, string? stored) => Locked.Contains(field) ? fromConfiguration : stored;

    private void Set(string field, Action apply)
    {
        if (!Locked.Contains(field))
        {
            apply();
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
