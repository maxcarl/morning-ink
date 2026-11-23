using Microsoft.EntityFrameworkCore;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Feedback;

/// <summary>The reader's feedback of an edition, to look through and take back; always only the user's own.</summary>
public sealed class FeedbackStore(MorningInkDbContext db)
{
    private const int Shown = 30;

    /// <summary>The latest feedback of the edition, newest first.</summary>
    public async Task<IReadOnlyList<Domain.Feedback>> ListAsync(Guid userId, Guid profileId, CancellationToken cancellationToken) =>
        await db.Feedback.AsNoTracking()
            .Where(f => f.UserId == userId && f.ProfileId == profileId)
            .OrderByDescending(f => f.At)
            .Take(Shown)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid userId, Guid feedbackId, CancellationToken cancellationToken) =>
        await db.Feedback.Where(f => f.Id == feedbackId && f.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;
}
