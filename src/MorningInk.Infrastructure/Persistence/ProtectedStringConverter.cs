using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Encrypts a string column with ASP.NET Data Protection; the keys live outside the database.</summary>
internal sealed class ProtectedStringConverter(IDataProtector protector)
    : ValueConverter<string, string>(value => protector.Protect(value), value => protector.Unprotect(value))
{
    // Changing the purpose makes existing secrets unreadable; bump the version only with a data migration.
    private const string Purpose = "MorningInk.Persistence.Secrets.v1";

    public ProtectedStringConverter(IDataProtectionProvider provider)
        : this(provider.CreateProtector(Purpose))
    {
    }
}
