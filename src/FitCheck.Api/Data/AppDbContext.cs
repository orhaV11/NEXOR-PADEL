using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<OutfitCheck> Checks => Set<OutfitCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Handle).HasMaxLength(40).IsRequired();
            user.Property(u => u.PreferredLanguage).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<OutfitCheck>(check =>
        {
            check.HasKey(c => c.Id);
            // Stored as text so the rows stay readable in any SQLite browser.
            check.Property(c => c.Intent).HasConversion<string>().HasMaxLength(32);
            check.Property(c => c.Occasion).HasMaxLength(120);
            check.Property(c => c.Language).HasMaxLength(16).IsRequired();
            check.Property(c => c.ImagePath).HasMaxLength(260).IsRequired();
            check.Property(c => c.Status).HasMaxLength(16).IsRequired();
            check.Property(c => c.PromptVersion).HasMaxLength(16).IsRequired();
            // Every user-facing query is "this user's checks, newest first"; the metrics endpoint groups on the same pair.
            check.HasIndex(c => new { c.UserId, c.CreatedAt });
            check.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
