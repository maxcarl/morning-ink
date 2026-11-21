using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class CredentialTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private readonly TestHost _host = new();

    [Fact]
    public async Task Secret_is_not_stored_in_plain_text()
    {
        await using (var services = await _host.StartAsync())
        {
            await SaveCredentialAsync(services);
        }

        var stored = await ReadStoredSecretAsync();
        Assert.NotEqual(Password, stored);
        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(Password, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(_host.DatabasePath)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secret_is_readable_after_a_restart()
    {
        await using (var services = await _host.StartAsync())
        {
            await SaveCredentialAsync(services);
        }

        await using (var services = await _host.StartAsync())
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var credential = await db.Credentials.SingleAsync();

            Assert.Equal(Password, credential.Secret);
        }
    }

    [Fact]
    public async Task Keys_are_kept_in_the_data_directory()
    {
        await using (var services = await _host.StartAsync())
        {
            await SaveCredentialAsync(services);
        }

        Assert.NotEmpty(Directory.GetFiles(_host.KeysDirectory, "*.xml"));
    }

    public void Dispose() => _host.Dispose();

    private static async Task SaveCredentialAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "max" };
        db.AddRange(user, new Credential { UserId = user.Id, Name = "FreshRSS", Username = "max", Secret = Password });
        await db.SaveChangesAsync();
    }

    private async Task<string> ReadStoredSecretAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_host.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Secret FROM Credentials";
        return (string)(await command.ExecuteScalarAsync())!; // ! is safe: the test saved exactly one credential
    }
}
