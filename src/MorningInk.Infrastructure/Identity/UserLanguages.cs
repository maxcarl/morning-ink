using Microsoft.EntityFrameworkCore;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Identity;

/// <summary>Keeps each user's language in step with their browser, for what is made without a page, e.g. issues.</summary>
public sealed class UserLanguages(MorningInkDbContext db)
{
    /// <param name="language">Two letters, e.g. "de", as the request's culture gives it.</param>
    public Task SetAsync(Guid userId, string language, CancellationToken cancellationToken) =>
        db.DomainUsers.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Language, language), cancellationToken);
}
