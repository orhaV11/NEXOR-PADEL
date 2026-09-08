namespace FitCheck.Api.Domain;

public enum AccountType
{
    Person,
    Brand
}

/// <summary>A check the owner chose to publish. The photo stays private until a post exists for it.</summary>
public sealed class Post
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CheckId { get; set; }
    public StyleIntent Intent { get; set; }
    public int Score { get; set; }
    public int IntentMatch { get; set; }

    /// <summary>The stylist's headline, copied at posting time so feeds never parse feedback JSON.</summary>
    public string Headline { get; set; } = "";

    /// <summary>At most 140 characters, optional.</summary>
    public string? Caption { get; set; }

    public Guid? ChallengeId { get; set; }

    /// <summary>A brand that featured this look (it mentions the brand or entered one of its challenges). One brand per post.</summary>
    public Guid? FeaturedByBrandId { get; set; }

    public DateTime? FeaturedAt { get; set; }

    /// <summary>The rubric v2 sub-scores, copied at posting time like the headline. Null for looks checked before v2.</summary>
    public int? FitScore { get; set; }

    public int? ColorScore { get; set; }
    public int? AccessoriesScore { get; set; }

    /// <summary>Denormalised so the feed never counts rows.</summary>
    public int FireCount { get; set; }

    public int CommentCount { get; set; }
    public int ReportCount { get; set; }

    /// <summary>Hidden pending review once enough reports arrive. Never shown in feeds or profiles.</summary>
    public bool Hidden { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>A #tag from the caption, lower case, without the #. At most 5 per post.</summary>
public sealed class PostTag
{
    public Guid PostId { get; set; }
    public string Tag { get; set; } = "";
}

/// <summary>An @mention from the caption that resolved to an account. At most 5 per post.</summary>
public sealed class PostMention
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>Short and public. The post owner can remove any comment on their post; three reports hide one.</summary>
public sealed class Comment
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = "";
    public int ReportCount { get; set; }
    public bool Hidden { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A private bookmark: inspiration to come back to. Never shown to anyone else.</summary>
public sealed class SavedPost
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Brand posts may carry up to three of these. Plain links to the brand's own site, no payments.</summary>
public sealed class ProductLink
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }

    /// <summary>Position in the brand's list, so links come back in the order they were entered.</summary>
    public int Position { get; set; }

    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Price { get; set; }
}

/// <summary>The one reaction. Positive only.</summary>
public sealed class Fire
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FollowedId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Opened by a brand. Entries are posts of the same intent; the crowd votes; the deadline fixes the winner.</summary>
public sealed class Challenge
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Title { get; set; } = "";
    public string Brief { get; set; } = "";
    public StyleIntent Intent { get; set; }

    /// <summary>The hashtag that enters a look: post with #tag while the challenge is open and you are in. Lower case, no #.</summary>
    public string Tag { get; set; } = "";

    public string Prize { get; set; } = "";
    public string? PrizeUrl { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Set once, lazily, by the first read after EndsAt. Null while open or when nobody entered.</summary>
    public Guid? WinnerPostId { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One vote per user per challenge; the row is replaced when the vote changes.</summary>
public sealed class ChallengeVote
{
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class NotificationType
{
    public const string Fire = "fire";
    public const string Follow = "follow";
    public const string Vote = "vote";
    public const string Entry = "entry";
    public const string Ended = "ended";
    public const string Won = "won";
    public const string Comment = "comment";
    public const string Mention = "mention";
    public const string Featured = "featured";
}

/// <summary>In-app activity only. No push, no email.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string ActorHandle { get; set; } = "";
    public Guid? PostId { get; set; }
    public Guid? ChallengeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>Targets either a post or a comment. One report per reporter per target.</summary>
public sealed class Report
{
    public Guid Id { get; set; }
    public Guid? PostId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid ReporterId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A Web Push subscription for one browser of one account. The endpoint is the browser's push service URL; p256dh and
/// auth are the client keys the payload is encrypted to. Gone browsers answer 404/410 and the row is dropped.
/// </summary>
public sealed class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// A one-time link: email verification or a password reset. Only the SHA-256 of the random token is stored, so a copy
/// of the database cannot mint links. Used once, expires, and every open token of the same purpose is voided when a new
/// one is issued.
/// </summary>
public sealed class AuthToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>verify | reset</summary>
    public string Purpose { get; set; } = "";

    /// <summary>Base64url SHA-256 of the token the person received.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>The address the link went to, so a verification proves this address and not a later one.</summary>
    public string? Email { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class AuthTokenPurpose
{
    public const string Verify = "verify";
    public const string Reset = "reset";
}
