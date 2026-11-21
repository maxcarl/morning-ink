using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>
/// Caches the EF model per Data Protection provider instead of once per context type.
/// </summary>
/// <remarks>
/// The model holds <see cref="ProtectedStringConverter"/>, which captures a protector. With EF's default
/// cache key, a second service provider (another data directory, or a test) would reuse the first one's
/// protector, encrypting with the wrong keys or with an already disposed provider.
/// </remarks>
internal sealed class DataProtectionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is MorningInkDbContext db
        ? (context.GetType(), db.DataProtection, designTime)
        : (context.GetType(), designTime);
}
