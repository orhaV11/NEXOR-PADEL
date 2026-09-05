using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<OutfitCheck> Checks => Set<OutfitCheck>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<ProductLink> ProductLinks => Set<ProductLink>();
    public DbSet<Fire> Fires => Set<Fire>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeVote> ChallengeVotes => Set<ChallengeVote>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<SavedPost> SavedPosts => Set<SavedPost>();
    public DbSet<PostTag> PostTags => Set<PostTag>();
    public DbSet<PostMention> PostMentions => Set<PostMention>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Handle).HasMaxLength(40).IsRequired();
            user.Property(u => u.HandleLower).HasMaxLength(40).IsRequired();
            user.HasIndex(u => u.HandleLower).IsUnique();
            user.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            user.Property(u => u.AccountType).HasConversion<string>().HasMaxLength(16);
            user.Property(u => u.DisplayName).HasMaxLength(40);
            user.Property(u => u.Bio).HasMaxLength(160);
            user.Property(u => u.Website).HasMaxLength(200);
            user.Property(u => u.PreferredLanguage).HasMaxLength(16).IsRequired();
            user.Property(u => u.AvatarPath).HasMaxLength(260);
            user.Property(u => u.Interests).HasMaxLength(200);
            user.Ignore(u => u.Name);
        });

        modelBuilder.Entity<OutfitCheck>(check =>
        {
            check.HasKey(c => c.Id);
            // Stored as text so the rows stay readable in any SQLite browser.
            check.Property(c => c.Intent).HasConversion<string>().HasMaxLength(32);
            check.Property(c => c.Occasion).HasMaxLength(120);
            check.Property(c => c.Language).HasMaxLength(16).IsRequired();
            check.Property(c => c.ImagePath).HasMaxLength(260).IsRequired();
            check.Property(c => c.VideoPath).HasMaxLength(260);
            check.Property(c => c.Status).HasMaxLength(16).IsRequired();
            check.Property(c => c.PromptVersion).HasMaxLength(16).IsRequired();
            // Every user-facing query is "this user's checks, newest first"; the metrics endpoint groups on the same pair.
            check.HasIndex(c => new { c.UserId, c.CreatedAt });
            check.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Post>(post =>
        {
            post.HasKey(p => p.Id);
            post.Property(p => p.Intent).HasConversion<string>().HasMaxLength(32);
            post.Property(p => p.Headline).HasMaxLength(160).IsRequired();
            post.Property(p => p.Caption).HasMaxLength(140);
            post.HasIndex(p => p.CheckId).IsUnique();
            post.HasIndex(p => new { p.UserId, p.CreatedAt });
            post.HasIndex(p => p.CreatedAt);
            post.HasIndex(p => p.ChallengeId);
            // One entry per person per challenge, enforced where two taps cannot argue with it.
            post.HasIndex(p => new { p.ChallengeId, p.UserId }).IsUnique().HasFilter("\"ChallengeId\" IS NOT NULL");
            post.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            post.HasOne<OutfitCheck>().WithMany().HasForeignKey(p => p.CheckId).OnDelete(DeleteBehavior.Cascade);
            post.HasOne<Challenge>().WithMany().HasForeignKey(p => p.ChallengeId).OnDelete(DeleteBehavior.SetNull);
            post.HasIndex(p => p.FeaturedByBrandId);
            post.HasOne<AppUser>().WithMany().HasForeignKey(p => p.FeaturedByBrandId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PostTag>(tag =>
        {
            tag.HasKey(t => new { t.PostId, t.Tag });
            tag.Property(t => t.Tag).HasMaxLength(30).IsRequired();
            tag.HasIndex(t => new { t.Tag, t.PostId });
            tag.HasOne<Post>().WithMany().HasForeignKey(t => t.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PostMention>(mention =>
        {
            mention.HasKey(m => new { m.PostId, m.UserId });
            mention.HasIndex(m => m.UserId);
            mention.HasOne<Post>().WithMany().HasForeignKey(m => m.PostId).OnDelete(DeleteBehavior.Cascade);
            mention.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductLink>(link =>
        {
            link.HasKey(l => l.Id);
            link.Property(l => l.Label).HasMaxLength(60).IsRequired();
            link.Property(l => l.Url).HasMaxLength(500).IsRequired();
            link.Property(l => l.Price).HasMaxLength(20);
            link.HasIndex(l => l.PostId);
            link.HasOne<Post>().WithMany().HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Fire>(fire =>
        {
            fire.HasKey(f => new { f.PostId, f.UserId });
            fire.HasOne<Post>().WithMany().HasForeignKey(f => f.PostId).OnDelete(DeleteBehavior.Cascade);
            fire.HasOne<AppUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Follow>(follow =>
        {
            follow.HasKey(f => new { f.FollowerId, f.FollowedId });
            follow.HasIndex(f => f.FollowedId);
            follow.HasOne<AppUser>().WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.Cascade);
            follow.HasOne<AppUser>().WithMany().HasForeignKey(f => f.FollowedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Challenge>(challenge =>
        {
            challenge.HasKey(c => c.Id);
            challenge.Property(c => c.Title).HasMaxLength(80).IsRequired();
            challenge.Property(c => c.Brief).HasMaxLength(500).IsRequired();
            challenge.Property(c => c.Intent).HasConversion<string>().HasMaxLength(32);
            challenge.Property(c => c.Tag).HasMaxLength(30).IsRequired();
            challenge.HasIndex(c => c.Tag);
            challenge.Property(c => c.Prize).HasMaxLength(200).IsRequired();
            challenge.Property(c => c.PrizeUrl).HasMaxLength(500);
            challenge.HasIndex(c => c.EndsAt);
            challenge.HasOne<AppUser>().WithMany().HasForeignKey(c => c.BrandId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChallengeVote>(vote =>
        {
            vote.HasKey(v => new { v.ChallengeId, v.UserId });
            vote.HasIndex(v => new { v.ChallengeId, v.PostId });
            vote.HasOne<Challenge>().WithMany().HasForeignKey(v => v.ChallengeId).OnDelete(DeleteBehavior.Cascade);
            vote.HasOne<AppUser>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
            vote.HasOne<Post>().WithMany().HasForeignKey(v => v.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Type).HasMaxLength(16).IsRequired();
            notification.Property(n => n.ActorHandle).HasMaxLength(40).IsRequired();
            notification.HasIndex(n => new { n.UserId, n.CreatedAt });
            notification.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Report>(report =>
        {
            report.HasKey(r => r.Id);
            report.Property(r => r.Reason).HasMaxLength(200).IsRequired();
            report.HasIndex(r => new { r.PostId, r.ReporterId }).IsUnique();
            report.HasIndex(r => new { r.CommentId, r.ReporterId }).IsUnique();
            report.HasOne<Post>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
            report.HasOne<Comment>().WithMany().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Cascade);
            report.HasOne<AppUser>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Comment>(comment =>
        {
            comment.HasKey(c => c.Id);
            comment.Property(c => c.Text).HasMaxLength(200).IsRequired();
            comment.HasIndex(c => new { c.PostId, c.CreatedAt });
            comment.HasOne<Post>().WithMany().HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade);
            comment.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(sub =>
        {
            sub.HasKey(s => s.Id);
            sub.Property(s => s.Endpoint).HasMaxLength(1000).IsRequired();
            sub.Property(s => s.P256dh).HasMaxLength(200).IsRequired();
            sub.Property(s => s.Auth).HasMaxLength(100).IsRequired();
            sub.HasIndex(s => s.Endpoint).IsUnique();
            sub.HasIndex(s => s.UserId);
            sub.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedPost>(saved =>
        {
            saved.HasKey(s => new { s.UserId, s.PostId });
            saved.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            saved.HasOne<Post>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
