using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Persistence;

public sealed class MorningInkDbContext(DbContextOptions<MorningInkDbContext> options) : DbContext(options)
{
    private const int NameLength = 200;
    private const int UrlLength = 2048;
    private const int EmailLength = 320;
    private const int LanguageLength = 16;
    private const int TimeZoneLength = 64;
    private const int RegionLength = 100;
    private const int EnumLength = 32;

    public DbSet<User> Users => Set<User>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Source> Sources => Set<Source>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Name).HasMaxLength(NameLength);
            user.HasIndex(u => u.Name).IsUnique();
            user.Property(u => u.Language).HasMaxLength(LanguageLength);
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
        });
    }
}
