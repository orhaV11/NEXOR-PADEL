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
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<PostItem> PostItems => Set<PostItem>();
    public DbSet<OutfitComparison> Comparisons => Set<OutfitComparison>();
    public DbSet<BoardExclusion> BoardExclusions => Set<BoardExclusion>();
    public DbSet<WeeklyWinner> WeeklyWinners => Set<WeeklyWinner>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<Block> Blocks => Set<Block>();

    // Round 14 — the loop: the "I tried it" pairs and the taste profile's two switches (Services/Taste.cs).
    public DbSet<CheckLink> CheckLinks => Set<CheckLink>();
    public DbSet<TasteSetting> TasteSettings => Set<TasteSetting>();
    // Round 14 — the wardrobe that builds itself (Services/Wardrobe.cs): the pieces an account kept from its checks,
    // the looks each one appeared in, and the account's one say over whether they reach the stylist.
    public DbSet<Services.WardrobeItem> WardrobeItems => Set<Services.WardrobeItem>();
    public DbSet<Services.WardrobeAppearance> WardrobeAppearances => Set<Services.WardrobeAppearance>();
    public DbSet<Services.WardrobeSetting> WardrobeSettings => Set<Services.WardrobeSetting>();

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
            user.Property(u => u.Email).HasMaxLength(200);
            user.Property(u => u.Plan).HasMaxLength(16).IsRequired();
            user.Property(u => u.BillingCustomerId).HasMaxLength(100);
            user.HasIndex(u => u.BillingCustomerId);
            // Round 11: the subscription Checkout opened, matched by the webhook; found through the customer, so no index.
            user.Property(u => u.BillingSubscriptionId).HasMaxLength(64);
            user.HasIndex(u => u.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
            user.Ignore(u => u.Name);
        });

        modelBuilder.Entity<OutfitCheck>(check =>
        {
            check.HasKey(c => c.Id);
            // Stored as text so the rows stay readable in any SQLite browser.
            check.Property(c => c.Intent).HasConversion<string>().HasMaxLength(32);
            check.Property(c => c.Language).HasMaxLength(16).IsRequired();
            check.Property(c => c.ImagePath).HasMaxLength(260).IsRequired();
            check.Property(c => c.VideoPath).HasMaxLength(260);
            check.Property(c => c.GuestToken).HasMaxLength(64);
            check.HasIndex(c => c.GuestToken);
            check.Property(c => c.Status).HasMaxLength(16).IsRequired();
            check.Property(c => c.PromptVersion).HasMaxLength(16).IsRequired();
            // Every user-facing query is "this user's checks, newest first"; the metrics endpoint groups on the same pair.
            check.HasIndex(c => new { c.UserId, c.CreatedAt });
            check.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            // Round 14 — the loop: the typed reason beside the yes/no, one of Domain.TipReason.
            check.Property(c => c.UsefulReason).HasMaxLength(16);
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
            post.HasIndex(p => p.BeforePostId);
            post.HasOne<Post>().WithMany().HasForeignKey(p => p.BeforePostId).OnDelete(DeleteBehavior.SetNull);
            // Round 14 — post the look, keep the grade: a plain flag, false for every look posted before it existed.
            post.Property(p => p.ScorePrivate).HasDefaultValue(false);
        });

        modelBuilder.Entity<PostItem>(item =>
        {
            // Round 9 keyed the row on (PostId, Name); Round 10 gives every item its own id (a link, a dot, an edit need one)
            // and keeps Name as an index, so the item search is as it was. The Round10 migration mints ids for the old rows.
            item.HasKey(i => i.Id);
            item.Property(i => i.Name).HasMaxLength(60).IsRequired();
            item.Property(i => i.Category).HasMaxLength(16).IsRequired();
            item.Property(i => i.Brand).HasMaxLength(40);
            item.Property(i => i.Model).HasMaxLength(60);
            item.Property(i => i.Url).HasMaxLength(500);
            item.Property(i => i.Source).HasConversion<string>().HasMaxLength(16);
            item.HasIndex(i => i.Name);
            item.HasIndex(i => i.Brand);
            item.HasIndex(i => i.Category);
            item.HasIndex(i => new { i.PostId, i.Position });
            item.HasOne<Post>().WithMany().HasForeignKey(i => i.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BoardExclusion>(exclusion =>
        {
            exclusion.HasKey(e => e.PostId);
            exclusion.Property(e => e.Reason).HasMaxLength(200).IsRequired();
            exclusion.HasIndex(e => e.ByUserId);
            exclusion.HasOne<Post>().WithMany().HasForeignKey(e => e.PostId).OnDelete(DeleteBehavior.Cascade);
            // A pulled look stays pulled when the moderator's account goes: the row keeps its reason and loses its signature.
            exclusion.HasOne<AppUser>().WithMany().HasForeignKey(e => e.ByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WeeklyWinner>(winner =>
        {
            winner.HasKey(w => w.Id);
            winner.Property(w => w.Board).HasMaxLength(32).IsRequired();
            // One row per place per board per week: the closer can run twice (a restart, a catch-up) and write once.
            winner.HasIndex(w => new { w.WeekStart, w.Board, w.Rank }).IsUnique();
            winner.HasIndex(w => new { w.UserId, w.WeekStart });
            winner.HasIndex(w => w.PostId);
            winner.HasOne<AppUser>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
            winner.HasOne<Post>().WithMany().HasForeignKey(w => w.PostId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Counter>(counter =>
        {
            counter.HasKey(c => c.Name);
            counter.Property(c => c.Name).HasMaxLength(40);
        });

        modelBuilder.Entity<OutfitComparison>(comparison =>
        {
            comparison.HasKey(c => c.Id);
            comparison.Property(c => c.Intent).HasConversion<string>().HasMaxLength(32);
            comparison.Property(c => c.Occasion).HasMaxLength(120);
            comparison.Property(c => c.Language).HasMaxLength(16).IsRequired();
            comparison.Property(c => c.ImagePathA).HasMaxLength(260).IsRequired();
            comparison.Property(c => c.ImagePathB).HasMaxLength(260).IsRequired();
            comparison.Property(c => c.Winner).HasMaxLength(2).IsRequired();
            comparison.Property(c => c.Status).HasMaxLength(16).IsRequired();
            comparison.Property(c => c.PromptVersion).HasMaxLength(16).IsRequired();
            comparison.Property(c => c.GuestToken).HasMaxLength(64);
            comparison.HasIndex(c => new { c.UserId, c.CreatedAt });
            comparison.HasIndex(c => c.GuestToken);
            comparison.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
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
            // The board reads a week's fires by time (Round 10); the metrics' 7-day window walks the same index.
            fire.HasIndex(f => f.CreatedAt);
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

        modelBuilder.Entity<Block>(block =>
        {
            // Round 11. One row per pair and direction; the key refuses a second tap. Both ends go with their account.
            // "Who blocked me" is read by BlockedId (the feed and the profile filters), hence the index; the blocker's own
            // list walks the key.
            block.HasKey(b => new { b.BlockerId, b.BlockedId });
            block.HasIndex(b => b.BlockedId);
            block.HasOne<AppUser>().WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Cascade);
            block.HasOne<AppUser>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
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
            // Round 14 — constraint challenges: the rule in the brand's own words; null is the open hashtag challenge.
            challenge.Property(c => c.Constraint).HasMaxLength(140);
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

        modelBuilder.Entity<AuthToken>(token =>
        {
            token.HasKey(t => t.Id);
            token.Property(t => t.Purpose).HasMaxLength(16).IsRequired();
            token.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            token.Property(t => t.Email).HasMaxLength(200);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => new { t.UserId, t.Purpose });
            token.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedPost>(saved =>
        {
            saved.HasKey(s => new { s.UserId, s.PostId });
            saved.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            saved.HasOne<Post>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        // Round 14 — the occasion split (the stylist). A check carries the pair the person chose: where it is going, and
        // the style they want it to read as (null when they asked for none). Both are text, like Intent above, so the rows
        // stay readable in any SQLite browser.
        // The column names are deliberately not the property names. The wearer's free line has been in a column called
        // "Occasion" since the first migration, and DatabaseSetup upgrades a pilot database made before migrations by
        // matching the model's columns against the file's: renaming that column would leave such a file with an extra
        // column nothing maps to, or turn 120 characters of the wearer's words into a column that must parse as an enum.
        // So the line keeps its column and the new chip takes a new one, and no stored character moves.
        modelBuilder.Entity<OutfitCheck>(check =>
        {
            check.Property(c => c.Occasion).HasColumnName("OccasionKind").HasConversion<string>().HasMaxLength(32);
            check.Property(c => c.Style).HasConversion<string>().HasMaxLength(32);
            check.Property(c => c.Note).HasColumnName("Occasion").HasMaxLength(120);
        });

        // ---- Round 14 — the loop: "I tried it" and the taste profile ----

        modelBuilder.Entity<CheckLink>(link =>
        {
            link.HasKey(l => l.Id);
            link.Property(l => l.Preferred).HasMaxLength(8).IsRequired();
            // One pair per check on either side: a check is the "before" of one attempt and the "after" of one attempt.
            link.HasIndex(l => l.BeforeCheckId).IsUnique();
            link.HasIndex(l => l.AfterCheckId).IsUnique();
            link.HasIndex(l => new { l.UserId, l.CreatedAt });
            link.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
            // The pair goes when either check goes: two halves are what it is.
            link.HasOne<OutfitCheck>().WithMany().HasForeignKey(l => l.BeforeCheckId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne<OutfitCheck>().WithMany().HasForeignKey(l => l.AfterCheckId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TasteSetting>(taste =>
        {
            taste.HasKey(s => s.UserId);
            taste.HasOne<AppUser>().WithOne().HasForeignKey<TasteSetting>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Round 14 — the wardrobe ----------

        modelBuilder.Entity<Services.WardrobeItem>(item =>
        {
            item.HasKey(i => i.Id);
            item.Property(i => i.Name).HasMaxLength(Services.Wardrobe.NameMaxLength).IsRequired();
            item.Property(i => i.NameKey).HasMaxLength(Services.Wardrobe.NameMaxLength).IsRequired();
            item.Property(i => i.Category).HasMaxLength(16).IsRequired();
            // One row per piece per account: keeping the camel coat from a second check adds a look, never a second coat.
            item.HasIndex(i => new { i.UserId, i.NameKey }).IsUnique();
            // Every read is "this account's pieces, most recently worn first": the list, and the names that go to the stylist.
            item.HasIndex(i => new { i.UserId, i.LastSeenAt });
            item.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Services.WardrobeAppearance>(appearance =>
        {
            appearance.HasKey(a => new { a.ItemId, a.CheckId });
            appearance.HasIndex(a => a.CheckId);
            appearance.HasOne<Services.WardrobeItem>().WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Cascade);
            // A deleted check takes its appearances with it; the piece stays, with one fewer look to its name.
            appearance.HasOne<OutfitCheck>().WithMany().HasForeignKey(a => a.CheckId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Services.WardrobeSetting>(setting =>
        {
            setting.HasKey(s => s.UserId);
            setting.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
