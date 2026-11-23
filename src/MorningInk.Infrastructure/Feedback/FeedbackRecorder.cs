using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Rendering;

namespace MorningInk.Infrastructure.Feedback;

/// <summary>What a feedback link did: the kind recorded and for which article, or null kind for a link not valid.</summary>
/// <param name="Message">The answer for the reader, in their language, as plain text.</param>
public sealed record FeedbackOutcome(FeedbackKind? Kind, string? ArticleTitle, string Message);

/// <summary>Records what a reader said with a link from the issue; one feedback per edition and article.</summary>
public sealed class FeedbackRecorder(MorningInkDbContext db, SignedFeedbackLinks links, TimeProvider time)
{
    private const string DefaultLanguage = "en";

    public async Task<FeedbackOutcome> RecordAsync(string token, CancellationToken cancellationToken)
    {
        if (links.Read(token) is not var (entryId, kind))
        {
            return Invalid(DefaultLanguage);
        }

        // The entry, its issue's edition and its owner: all from the stored issue, nothing from the link but the id.
        var found = await (
            from entry in db.Set<IssueEntry>()
            join issue in db.Issues on entry.IssueId equals issue.Id
            join profile in db.Profiles on issue.ProfileId equals profile.Id
            join user in db.DomainUsers on profile.UserId equals user.Id
            where entry.Id == entryId
            select new { entry.Title, entry.Url, entry.SourceId, entry.SourceName, ProfileId = profile.Id, UserId = user.Id, user.Language })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            // The issue was cleaned up meanwhile.
            return Invalid(DefaultLanguage);
        }

        var feedback = await db.Feedback.SingleOrDefaultAsync(f => f.ProfileId == found.ProfileId && f.ArticleUrl == found.Url, cancellationToken);
        if (feedback is null)
        {
            feedback = new Domain.Feedback
            {
                UserId = found.UserId,
                ProfileId = found.ProfileId,
                SourceId = found.SourceId,
                SourceName = found.SourceName,
                ArticleTitle = found.Title,
                ArticleUrl = found.Url,
            };
            db.Feedback.Add(feedback);
        }

        feedback.Kind = kind;
        feedback.At = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var texts = EpubTexts.For(found.Language);
        return new FeedbackOutcome(kind, found.Title, kind == FeedbackKind.More ? texts.FeedbackThanksMore(found.Title) : texts.FeedbackThanksLess(found.Title));
    }

    private static FeedbackOutcome Invalid(string language) => new(null, null, EpubTexts.For(language).FeedbackInvalid);
}
