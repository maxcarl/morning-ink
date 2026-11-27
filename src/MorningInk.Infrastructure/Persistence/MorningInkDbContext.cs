using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MorningInk.Domain;
using MorningInk.Infrastructure.Ai;
using MorningInk.Infrastructure.Identity;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>EF Core context for all Morning Ink data. Only parameterized queries, never concatenated SQL.</summary>
public sealed class MorningInkDbContext(
    DbContextOptions<MorningInkDbContext> options,
    IDataProtectionProvider dataProtection) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    // Column limits; they also cap what untrusted input can store.
    private const int NameLength = 200;
    private const int UrlLength = 2048;
    private const int EmailLength = 320; // RFC 5321 maximum
    private const int LanguageLength = 16;
    private const int TimeZoneLength = 64;
    private const int RegionLength = 100;
    private const int EnumLength = 32;
    private const int ETagLength = 512;
    private const int Sha256HexLength = 64;
    private const int CountryCodeLength = 2;

    /// <summary>Encrypts secrets; part of the model cache key, see <see cref="DataProtectionModelCacheKeyFactory"/>.</summary>
    internal IDataProtectionProvider DataProtection { get; } = dataProtection;

    /// <summary>Morning Ink's users; Identity's own <c>Users</c> set holds their logins.</summary>
    public DbSet<User> DomainUsers => Set<User>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<SeenArticle> SeenArticles => Set<SeenArticle>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<AllowedHost> AllowedHosts => Set<AllowedHost>();

    public DbSet<MailSettings> MailSettings => Set<MailSettings>();

    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    public DbSet<AiCall> AiCalls => Set<AiCall>();

    public DbSet<Domain.Feedback> Feedback => Set<Domain.Feedback>();

    public DbSet<NewsSearchSettings> NewsSearchSettings => Set<NewsSearchSettings>();

    // SQLite has no date type: as text, DateTimeOffset cannot be compared or sorted in queries. As a number
    // (UTC ticks plus offset) it can, which slot lookups and the cleanup by age need.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's tables first; everything below is Morning Ink's own model.
        base.OnModelCreating(builder);

        // A login belongs to exactly one domain user and shares its id; deleting the user deletes the login.
        builder.Entity<AppUser>().HasOne<User>().WithOne().HasForeignKey<AppUser>(a => a.Id).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.Property(u => u.Name).HasMaxLength(NameLength);
            user.HasIndex(u => u.Name).IsUnique();
            user.Property(u => u.Language).HasMaxLength(LanguageLength);
            // Enums as text, so the database stays readable and reordering members is safe.
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(EnumLength);
        });

        builder.Entity<Profile>(profile =>
        {
            profile.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            profile.Property(p => p.Title).HasMaxLength(NameLength);
            profile.Property(p => p.Recipient).HasMaxLength(EmailLength);
            profile.Property(p => p.Region).HasMaxLength(RegionLength);
            profile.Property(p => p.TimeZoneId).HasMaxLength(TimeZoneLength);
            profile.Property(p => p.Country).HasMaxLength(CountryCodeLength);
            profile.OwnsOne(p => p.Location, location =>
            {
                location.Property(l => l.Query).HasMaxLength(RegionLength);
                location.Property(l => l.Name).HasMaxLength(NameLength);
                location.Property(l => l.District).HasMaxLength(NameLength);
            });
            // Interests belong to one edition and are always read with it: one JSON column, no table of their own.
            profile.Property(p => p.Interests).HasConversion(JsonColumn.Converter<Interest>(), JsonColumn.Comparer<Interest>());
            profile.Property(p => p.NotifySecret).HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)new ProtectedStringConverter(DataProtection));
            profile.HasMany(p => p.Sections).WithOne().HasForeignKey(s => s.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Section>(section =>
        {
            section.Property(s => s.Name).HasMaxLength(NameLength);
            section.Property(s => s.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            section.HasMany(s => s.Sources).WithMany();
        });

        builder.Entity<Source>(source =>
        {
            source.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            source.Property(s => s.Name).HasMaxLength(NameLength);
            source.Property(s => s.Url).HasMaxLength(UrlLength);
            source.Property(s => s.Stream).HasMaxLength(UrlLength);
            source.Property(s => s.Sender).HasMaxLength(NameLength);
            source.Property(s => s.LastProblem).HasMaxLength(UrlLength);
            source.Property(s => s.LastProblemReason).HasMaxLength(NameLength);
            source.Property(s => s.LastProblemArguments).HasConversion(JsonColumn.Converter<string>(), JsonColumn.Comparer<string>());
            source.Property(s => s.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            source.Property(s => s.ETag).HasMaxLength(ETagLength);
            source.Property(s => s.Place).HasMaxLength(RegionLength);
            source.Property(s => s.Country).HasMaxLength(CountryCodeLength);
            source.OwnsOne(s => s.Location, location =>
            {
                location.Property(l => l.Query).HasMaxLength(RegionLength);
                location.Property(l => l.Name).HasMaxLength(NameLength);
                location.Property(l => l.District).HasMaxLength(NameLength);
            });
            source.HasOne<Credential>().WithMany().HasForeignKey(s => s.CredentialId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SeenArticle>(seen =>
        {
            seen.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            seen.Property(s => s.UrlHash).HasMaxLength(Sha256HexLength).IsFixedLength();

            // Lookups always go by user and hash; uniqueness keeps an article from being stored twice.
            seen.HasIndex(s => new { s.UserId, s.UrlHash }).IsUnique();

            // For the cleanup job, which deletes by age.
            seen.HasIndex(s => s.SeenAt);
        });

        builder.Entity<Issue>(issue =>
        {
            issue.HasOne<Profile>().WithMany().HasForeignKey(i => i.ProfileId).OnDelete(DeleteBehavior.Cascade);
            issue.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            issue.Property(i => i.FileName).HasMaxLength(NameLength);

            // One issue per profile and slot: the guard against sending twice, even across processes.
            issue.HasIndex(i => new { i.ProfileId, i.ScheduledFor }).IsUnique();
            issue.Property(i => i.ErrorArguments).HasConversion(JsonColumn.Converter<string>(), JsonColumn.Comparer<string>());
            issue.Property(i => i.Log).HasConversion(JsonColumn.Converter<IssueLogEntry>(), JsonColumn.Comparer<IssueLogEntry>());
            issue.HasMany(i => i.Entries).WithOne().HasForeignKey(e => e.IssueId).OnDelete(DeleteBehavior.Cascade);
            issue.HasMany(i => i.Attempts).WithOne().HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<NewsSearchSettings>().Property(n => n.SearxngUrl).HasMaxLength(500);
        builder.Entity<MaintenanceSettings>().Property(m => m.CleanupTimeZoneId).HasMaxLength(TimeZoneLength);

        builder.Entity<AiSettings>(ai =>
        {
            ai.Property(a => a.Endpoint).HasMaxLength(500);
            ai.Property(a => a.Model).HasMaxLength(200);
            ai.Property(a => a.SummaryPrompt).HasMaxLength(AiSettingsStore.MaxPromptLength);
            ai.Property(a => a.OverviewPrompt).HasMaxLength(AiSettingsStore.MaxPromptLength);
            ai.Property(a => a.HeadlinesPrompt).HasMaxLength(AiSettingsStore.MaxPromptLength);
            ai.Property(a => a.InterestsPrompt).HasMaxLength(AiSettingsStore.MaxPromptLength);
            ai.Property(a => a.ApiKey).HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)new ProtectedStringConverter(DataProtection));
        });

        builder.Entity<Domain.Feedback>(feedback =>
        {
            feedback.HasOne<Profile>().WithMany().HasForeignKey(f => f.ProfileId).OnDelete(DeleteBehavior.Cascade);
            feedback.Property(f => f.SourceName).HasMaxLength(NameLength);
            feedback.Property(f => f.ArticleTitle).HasMaxLength(500);
            feedback.Property(f => f.ArticleUrl).HasMaxLength(UrlLength);
            feedback.Property(f => f.Kind).HasConversion<string>().HasMaxLength(EnumLength);

            // One answer per edition and article: saying the other later replaces it.
            feedback.HasIndex(f => new { f.ProfileId, f.ArticleUrl }).IsUnique();
        });

        builder.Entity<AiCall>(call =>
        {
            call.Property(c => c.Provider).HasMaxLength(NameLength);
            call.Property(c => c.Model).HasMaxLength(200);
            call.Property(c => c.SourceName).HasMaxLength(NameLength);
            call.Property(c => c.ArticleTitle).HasMaxLength(500);
            call.Property(c => c.ArticleUrl).HasMaxLength(2000);

            // The limit counts the last 24 hours before every call, and the cleanup deletes by age.
            call.HasIndex(c => c.At);
        });

        builder.Entity<MailSettings>(mail =>
        {
            mail.Property(m => m.Host).HasMaxLength(NameLength);
            mail.Property(m => m.Username).HasMaxLength(NameLength);
            mail.Property(m => m.FromAddress).HasMaxLength(EmailLength);
            mail.Property(m => m.FromName).HasMaxLength(NameLength);
            mail.Property(m => m.Security).HasConversion<string>().HasMaxLength(EnumLength);

            // Encrypted at rest like other credentials; the keys live outside the database.
            // Non-generic overload: the property may be null, and EF never passes null to a converter.
            mail.Property(m => m.Password).HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)new ProtectedStringConverter(DataProtection));
        });

        builder.Entity<AllowedHost>(allowed =>
        {
            allowed.Property(a => a.Entry).HasMaxLength(NameLength);
            allowed.HasIndex(a => a.Entry).IsUnique();
        });

        builder.Entity<IssueEntry>(entry =>
        {
            entry.Property(e => e.Url).HasMaxLength(UrlLength);
            entry.Property(e => e.Title).HasMaxLength(UrlLength);
            entry.Property(e => e.SourceName).HasMaxLength(NameLength);

            // No foreign key to the source: a deleted source must not take the archive's entries with it.
            entry.Property(e => e.ExternalId).HasMaxLength(UrlLength);
        });

        builder.Entity<Credential>(credential =>
        {
            credential.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            credential.Property(c => c.Name).HasMaxLength(NameLength);
            credential.Property(c => c.Username).HasMaxLength(NameLength);
            credential.Property(c => c.Method).HasConversion<string>().HasMaxLength(EnumLength);
            // Encrypted at rest; the keys live in a separate directory, not in the database.
            credential.Property(c => c.Secret).HasConversion(new ProtectedStringConverter(DataProtection));
        });

        // Ids come from the domain (Guid v7), never from the database. Without this, EF takes a new child with an
        // Id already set, added to a loaded parent (an entry added to an issue), for an existing row and updates it.
        foreach (var key in builder.Model.GetEntityTypes().SelectMany(e => e.FindPrimaryKey()?.Properties ?? []))
        {
            if (key.Name == "Id" && key.ClrType == typeof(Guid))
            {
                key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    /// <summary>The one row of an instance-wide settings table, added when there is none yet.</summary>
    /// <remarks>Single, not First: a second row would be a bug worth seeing.</remarks>
    public async Task<T> SettingsRowAsync<T>(CancellationToken cancellationToken)
        where T : class, new() =>
        await Set<T>().SingleOrDefaultAsync(cancellationToken) ?? Add(new T()).Entity;
}
