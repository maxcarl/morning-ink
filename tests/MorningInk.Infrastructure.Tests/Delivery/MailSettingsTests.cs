using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Delivery;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Delivery;

/// <summary>Mail settings from the UI, overridden by the configuration, with the password encrypted at rest.</summary>
public sealed class MailSettingsTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private readonly TestHost _host = new();
    private readonly FakeSmtpServer _smtp = new();

    [Fact]
    public async Task Settings_from_the_ui_are_used_for_delivery()
    {
        await using var services = await _host.StartAsync();
        await SaveAsync(services, Input(password: Password));

        var result = await SendTestAsync(services);

        Assert.IsType<DeliveryResult.Delivered>(result);
        Assert.Equal("max\0" + Password, Assert.Single(_smtp.Logins));
        var mail = Assert.Single(_smtp.Messages);
        Assert.StartsWith("Morning Ink Test", mail.Subject, StringComparison.Ordinal);
        Assert.EndsWith(".epub", Assert.Single(mail.Attachments).ContentDisposition?.FileName ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_is_encrypted_and_kept_when_the_field_stays_empty()
    {
        await using var services = await _host.StartAsync();
        await SaveAsync(services, Input(password: Password));

        await SaveAsync(services, Input(password: null));
        await SendTestAsync(services);

        Assert.Equal("max\0" + Password, Assert.Single(_smtp.Logins));
        Assert.DoesNotContain(Password, await ReadStoredPasswordAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_wins_and_locks_its_fields()
    {
        await using var services = await _host.StartAsync(new Dictionary<string, string?> { ["Smtp:FromAddress"] = "from-env@example.com" });
        await SaveAsync(services, Input(password: Password) with { FromAddress = "from-ui@example.com" });

        await SendTestAsync(services);

        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<MailSettingsStore>();
        Assert.Contains(MailSettingsStore.FromAddressField, store.Locked);
        Assert.Null((await store.LoadAsync(CancellationToken.None)).FromAddress); // the UI value was not stored
        Assert.Equal("from-env@example.com", Assert.Single(_smtp.Messages).From.Mailboxes.Single().Address);
    }

    [Theory]
    [InlineData("smtp example.com", "587", "max@example.com", MailSettingsStore.HostField)]
    [InlineData("smtp.example.com", "70000", "max@example.com", MailSettingsStore.PortField)]
    [InlineData("smtp.example.com", "587", "a@b.de, c@d.de", MailSettingsStore.FromAddressField)]
    public async Task Invalid_settings_are_refused(string host, string port, string from, string field)
    {
        await using var services = await _host.StartAsync();

        var errors = await SaveAsync(services, new MailSettingsInput(host, port, MailSecurity.StartTls, null, null, from, null));

        Assert.Contains(field, errors);
    }

    public void Dispose()
    {
        _smtp.Dispose();
        _host.Dispose();
    }

    private MailSettingsInput Input(string? password) =>
        new("127.0.0.1", _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), MailSecurity.None, "max", password, "morning-ink@example.com", null);

    private static async Task<IReadOnlyList<string>> SaveAsync(IServiceProvider services, MailSettingsInput input)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MailSettingsStore>().SaveAsync(input, CancellationToken.None);
    }

    private static async Task<DeliveryResult> SendTestAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestMail>().SendAsync("max_abc@kindle.com", "en", CancellationToken.None);
    }

    private async Task<string> ReadStoredPasswordAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_host.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Password FROM MailSettings";
        return (string)(await command.ExecuteScalarAsync())!; // ! is safe: the test saved a password
    }
}
