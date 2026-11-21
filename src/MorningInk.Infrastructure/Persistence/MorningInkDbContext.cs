using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>EF Core context for all Morning Ink data. Only parameterized queries, never concatenated SQL.</summary>
public sealed class MorningInkDbContext(
    DbContextOptions<MorningInkDbContext> options,
    IDataProtectionProvider dataProtection) : DbContext(options)
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

    /// <summary>Encrypts secrets; part of the model cache key, see <see cref="DataProtectionModelCacheKeyFactory"/>.</summary>
    internal IDataProtectionProvider DataProtection { get; } = dataProtection;

    public DbSet<User> Users => Set<User>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<SeenArticle> SeenArticles => Set<SeenArticle>();

    public DbSet<Issue> Issues => Set<Issue>();

    // SQLite has no date type: as text, DateTimeOffset cannot be compared or sorted in queries. As a number
    // (UTC ticks plus offset) it can, which slot lookups and the cleanup by age need.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Name).HasMaxLength(NameLength);
            user.HasIndex(u => u.Name).IsUnique();
            user.Property(u => u.Language).HasMaxLength(LanguageLength);
            // Enums as text, so the database stays readable and reordering members is safe.
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<Profile>(profile =>
        {
            profile.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            profile.Property(p => p.Title).HasMaxLength(NameLength);
            profile.Property(p => p.Recipient).HasMaxLength(EmailLength);
            profile.Property(p => p.Region).HasMaxLength(RegionLength);
            profile.Property(p => p.TimeZoneId).HasMaxLength(TimeZoneLength);
            profile.HasMany(p => p.Sections).WithOne().HasForeignKey(s => s.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Section>(section =>
        {
            section.Property(s => s.Name).HasMaxLength(NameLength);
            section.HasMany(s => s.Sources).WithMany();
        });

        modelBuilder.Entity<Source>(source =>
        {
            source.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            source.Property(s => s.Name).HasMaxLength(NameLength);
            source.Property(s => s.Url).HasMaxLength(UrlLength);
            source.Property(s => s.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            source.Property(s => s.ETag).HasMaxLength(ETagLength);
            source.HasOne<Credential>().WithMany().HasForeignKey(s => s.CredentialId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SeenArticle>(seen =>
        {
            seen.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            seen.Property(s => s.UrlHash).HasMaxLength(Sha256HexLength).IsFixedLength();

            // Lookups always go by user and hash; uniqueness keeps an article from being stored twice.
            seen.HasIndex(s => new { s.UserId, s.UrlHash }).IsUnique();

            // For the cleanup job, which deletes by age.
            seen.HasIndex(s => s.SeenAt);
        });

        modelBuilder.Entity<Issue>(issue =>
        {
            issue.HasOne<Profile>().WithMany().HasForeignKey(i => i.ProfileId).OnDelete(DeleteBehavior.Cascade);
            issue.Property(i => i.Status).HasConversion<string>().HasMaxLength(EnumLength);
            issue.Property(i => i.FileName).HasMaxLength(NameLength);

            // One issue per profile and slot: the guard against sending twice, even across processes.
            issue.HasIndex(i => new { i.ProfileId, i.ScheduledFor }).IsUnique();
            issue.HasMany(i => i.Entries).WithOne().HasForeignKey(e => e.IssueId).OnDelete(DeleteBehavior.Cascade);
            issue.HasMany(i => i.Attempts).WithOne().HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IssueEntry>(entry =>
        {
            entry.Property(e => e.Url).HasMaxLength(UrlLength);
            entry.Property(e => e.Title).HasMaxLength(UrlLength);
            entry.Property(e => e.SourceName).HasMaxLength(NameLength);
        });

        modelBuilder.Entity<Credential>(credential =>
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
        foreach (var key in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.FindPrimaryKey()?.Properties ?? []))
        {
            if (key.Name == "Id" && key.ClrType == typeof(Guid))
            {
                key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
