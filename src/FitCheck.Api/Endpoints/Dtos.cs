using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

public sealed record ErrorDto(string Error);

// ---- auth and users ----

/// <summary>BirthDate: "yyyy-MM-dd". Required from Round 9 (16 and over); Confirmed16Plus stays for older clients.</summary>
public sealed record SignupRequest(string? Handle, string? Password, bool Confirmed16Plus, string? Language, string? AccountType, string? DisplayName, string? BirthDate = null);

public sealed record LoginRequest(string? Handle, string? Password);

/// <summary>Email: null leaves it alone, "" clears it, a new address replaces it and starts a verification.</summary>
public sealed record UpdateMeRequest(string? Language, string? DisplayName, string? Bio, string? Website, string? AccountType = null, List<string>? Interests = null, string? Email = null);

public sealed record ForgotPasswordRequest(string? HandleOrEmail);

public sealed record ResetPasswordRequest(string? Token, string? Password);

public sealed record VerifyEmailRequest(string? Token);

/// <summary>The signed-in user, as the client keeps it in memory.</summary>
public sealed record MeDto(
    Guid Id, string Handle, string Name, string AccountType, string Language, string? Bio, string? Website, int Streak, int UnreadNotifications,
    string? AvatarUrl = null, List<string>? Interests = null, bool IsAdmin = false, string? Email = null, bool EmailVerified = false,
    string Plan = "free", DateTime? ProUntil = null, bool Verified = false, int ChecksToday = 0, int ChecksPerDay = 0);

/// <summary>AvatarUrl is versioned (?v=) so it can be cached hard; null when the account has no photo.</summary>
public sealed record UserRefDto(string Handle, string Name, string AccountType, string? AvatarUrl = null, bool Verified = false);

/// <summary>A person or brand in a list: search results, brands to follow.</summary>
public sealed record UserCardDto(UserRefDto User, int Followers, int Posts, bool Following);

public sealed record TagDto(string Tag, int Posts);

public sealed record AvatarDto(string? AvatarUrl);

public sealed record ViewerProfileDto(bool IsMe, bool Following);

/// <summary>Featured: for a brand, looks it featured; for a person, their looks that were featured. Community: looks mentioning this account.</summary>
public sealed record ProfileDto(
    string Handle, string Name, string AccountType, string? Bio, string? Website,
    int Posts, int Followers, int Following, int FireReceived, int? BestScore, int Streak, DateTime CreatedAt,
    ViewerProfileDto Viewer, string? AvatarUrl = null, int Featured = 0, int Community = 0);

public sealed record FollowStateDto(int Followers, bool Following);

// ---- checks ----

public sealed record CheckDto(
    Guid Id,
    StyleIntent Intent,
    string? Occasion,
    string Language,
    DateTime CreatedAt,
    int LatencyMs,
    string Status,
    int? Score,
    OutfitFeedback? Feedback,
    Guid? PostId)
{
    /// <summary>Rejected rows store nothing but the status; the neutral message is added here, in the check's language.</summary>
    public static CheckDto FromEntity(OutfitCheck check, Localizer localizer, Guid? postId)
    {
        OutfitFeedback? feedback = null;
        if (check.Status == CheckStatus.Rejected)
        {
            feedback = new OutfitFeedback
            {
                Status = CheckStatus.Rejected,
                Score = 1,
                IntentMatch = 0,
                Message = localizer.Get(check.Language, "feedback.rejected")
            };
        }
        else if (check.FeedbackJson is not null)
        {
            feedback = JsonSerializer.Deserialize<OutfitFeedback>(check.FeedbackJson, AppJson.Options);
        }

        return new CheckDto(
            check.Id,
            check.Intent,
            check.Occasion,
            check.Language,
            DateTime.SpecifyKind(check.CreatedAt, DateTimeKind.Utc),
            check.LatencyMs,
            check.Status,
            check.Score,
            feedback,
            postId);
    }
}

// ---- posts ----

public sealed record ProductLinkDto(string Label, string Url, string? Price);

/// <summary>BeforePostId: "after the tip" — one of your own earlier looks this one improves on.</summary>
public sealed record CreatePostRequest(Guid CheckId, string? Caption, Guid? ChallengeId, List<ProductLinkDto>? Products, Guid? BeforePostId = null);

public sealed record ReportRequest(string? Reason);

public sealed record PostDto(
    Guid Id,
    UserRefDto User,
    StyleIntent Intent,
    int Score,
    int IntentMatch,
    string Headline,
    string? Caption,
    Guid? ChallengeId,
    string? ChallengeTitle,
    int FireCount,
    int CommentCount,
    bool Fired,
    bool Saved,
    bool IsMine,
    bool Hidden,
    int Votes,
    List<ProductLinkDto> Products,
    string ImageUrl,
    DateTime CreatedAt,
    List<string> Tags,
    List<UserRefDto> Mentions,
    UserRefDto? FeaturedBy,
    string? VideoUrl = null,
    BreakdownDto? Breakdown = null,
    BeforeDto? Before = null);

/// <summary>The earlier look an "after the tip" post improves on: its score and photo, for the before/after strip.</summary>
public sealed record BeforeDto(Guid PostId, int Score, string ImageUrl);

/// <summary>The rubric v2 sub-scores (1–10). On a post they were copied at posting time.</summary>
public sealed record BreakdownDto(int Fit, int Color, int Accessories);

public sealed record FeatureStateDto(UserRefDto? FeaturedBy);

// ---- explore ----

public sealed record ExploreDto(List<TagDto> TrendingTags, List<UserCardDto> Brands, List<PostDto> TopLooks, List<ChallengeDto> Challenges);

/// <summary>Posts: looks whose stylist-named items match the query ("black boots"), newest first, up to 12.</summary>
public sealed record SearchDto(List<UserCardDto> Users, List<TagDto> Tags, List<PostDto>? Posts = null);

public sealed record FireStateDto(int FireCount, bool Fired);

public sealed record SaveStateDto(bool Saved);

public sealed record CreateCommentRequest(string? Text);

public sealed record CommentDto(Guid Id, UserRefDto User, string Text, bool IsMine, bool CanDelete, DateTime CreatedAt);

public sealed record FeedDto(List<PostDto> Items, int? NextOffset);

// ---- challenges ----

/// <summary>Tag is optional: when missing it is derived from the title. Letters, digits and underscores, 2–30 characters, no #.</summary>
public sealed record CreateChallengeRequest(string? Title, string? Brief, string? Intent, string? Prize, string? PrizeUrl, DateTime? EndsAt, string? Tag = null);

public sealed record VoteRequest(Guid PostId);

public sealed record ChallengeViewerDto(bool IsBrand, bool HasEntered, Guid? VotedPostId, Guid? MyEntryId);

public sealed record ChallengeDto(
    Guid Id,
    UserRefDto Brand,
    string Title,
    string Brief,
    StyleIntent Intent,
    string Tag,
    string Prize,
    string? PrizeUrl,
    DateTime EndsAt,
    bool IsOpen,
    int Entries,
    int Votes,
    Guid? WinnerPostId,
    List<PostDto> Top,
    ChallengeViewerDto Viewer,
    DateTime CreatedAt);

public sealed record ChallengeDetailDto(ChallengeDto Challenge, List<PostDto> EntriesByVotes, PostDto? Winner);

public sealed record VoteStateDto(Guid? VotedPostId, int Votes);

// ---- notifications ----

/// <summary>ActorName is the actor's current display name, or the handle when there is none or the account is gone.</summary>
public sealed record NotificationDto(Guid Id, string Type, string ActorHandle, string ActorName, string? ActorAvatarUrl, Guid? PostId, Guid? ChallengeId, DateTime CreatedAt, bool Read);

public sealed record NotificationsDto(List<NotificationDto> Items, int Unread);

// ---- config, push, admin ----

/// <summary>Public, unauthenticated: what the client needs before it can do anything. No secrets.</summary>
public sealed record ConfigDto(long MaxImageBytes, long MaxVideoBytes, int MaxVideoSeconds, string? PushPublicKey, bool Email = false, bool Transcoding = false, PlansDto? Plans = null);

/// <summary>Billing: true when Stripe Checkout is live; false means Pro is granted by hand (--pro) and the Pro screen says so.</summary>
public sealed record PlansDto(int FreeChecksPerDay, int ProChecksPerDay, int GuestChecksPerDay, string ProPriceText, bool CompareNeedsPro, bool Billing);

// ---- comparisons, insights, today ----

public sealed record ComparisonDto(Guid Id, StyleIntent Intent, string? Occasion, string Language, DateTime CreatedAt, int LatencyMs, string Status, ComparisonFeedback? Feedback, string ImageUrlA, string ImageUrlB);

/// <summary>What your checks say about you. Lines are ready sentences in your language; the numbers are for tiles.</summary>
public sealed record InsightsDto(int Checks, double? AvgScore, int? BestScore, string? BestIntent, string? WeakestCategory, double? WeakestShare, double? AccessoriesMissingShare, int Streak, List<string> Lines);

/// <summary>The daily prompt: a hashtag, a title and a hint in the caller's language, and the looks posted with it today.</summary>
public sealed record TodayDto(string Tag, string Title, string Hint, StyleIntent? Intent, DateTime Date, List<PostDto> Posts, bool Posted);

public sealed record CheckoutDto(string Url);

public sealed record BillingStateDto(string Plan, DateTime? ProUntil, bool Billing, string ProPriceText);

public sealed record PushSubscribeRequest(string? Endpoint, string? P256dh, string? Auth);

public sealed record PushStateDto(bool Enabled, bool Subscribed);

/// <summary>One item of the moderation queue: a reported post or a reported comment, with the reasons people gave.</summary>
public sealed record AdminReportDto(string Kind, Guid Id, int Reports, bool Hidden, List<string> Reasons, DateTime LastReportedAt, PostDto? Post, CommentDto? Comment, UserRefDto? Author, bool AuthorSuspended);

public sealed record AdminQueueDto(List<AdminReportDto> Items, int HiddenPosts, int HiddenComments, int SuspendedUsers);

public sealed record AdminUserDto(UserRefDto User, bool Suspended, int Posts, int Reports, DateTime CreatedAt);

// ---- metrics ----

public sealed record SocialMetricsDto(
    int Users,
    int Brands,
    int Posts,
    int Fires,
    int Follows,
    int Comments,
    int ChallengesOpen,
    int ChallengesEnded,
    int Votes,
    int ActiveUsers7d,
    int Mentions = 0,
    int Featured = 0,
    int Videos = 0,
    int PushSubscriptions = 0);

public sealed record PilotMetricsDto(
    int TotalChecks,
    int UsersWithAtLeastOneCheck,
    int UsersWithSecondCheckWithin7Days,
    double ReturnRate,
    int AvgLatencyMs,
    Dictionary<string, int> ScoreDistribution,
    Dictionary<string, int> ByLanguage,
    Dictionary<string, int> ByPromptVersion,
    SocialMetricsDto? Social = null,
    BreakdownAveragesDto? BreakdownAverages = null);

/// <summary>
/// Mean of each rubric v2 sub-score over the ok checks that carry a breakdown (Checks says how many), two decimals.
/// Null on the metrics when no check has one yet, so a v1-only pilot reads as before.
/// </summary>
public sealed record BreakdownAveragesDto(double AvgFit, double AvgColor, double AvgAccessories, int Checks);
