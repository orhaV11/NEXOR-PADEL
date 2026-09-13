using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

public sealed record ErrorDto(string Error);

// ---- auth and users ----

/// <summary>
/// BirthDate: "yyyy-MM-dd". Required from Round 9 (16 and over); Confirmed16Plus stays for older clients. Today: the
/// client's own calendar date ("yyyy-MM-dd", optional), the day the sixteen rule is measured on when it is within a day
/// of the server's UTC date; missing, unreadable or further off, the UTC date is used.
/// </summary>
public sealed record SignupRequest(string? Handle, string? Password, bool Confirmed16Plus, string? Language, string? AccountType, string? DisplayName, string? BirthDate = null, string? Today = null);

public sealed record LoginRequest(string? Handle, string? Password);

/// <summary>Email: null leaves it alone, "" clears it, a new address replaces it and starts a verification.</summary>
public sealed record UpdateMeRequest(string? Language, string? DisplayName, string? Bio, string? Website, string? AccountType = null, List<string>? Interests = null, string? Email = null);

public sealed record ForgotPasswordRequest(string? HandleOrEmail);

public sealed record ResetPasswordRequest(string? Token, string? Password);

public sealed record VerifyEmailRequest(string? Token);

/// <summary>The signed-in user, as the client keeps it in memory. Badge: last week's place on the board, for this week only (Round 10; null until the board builder fills it).</summary>
public sealed record MeDto(
    Guid Id, string Handle, string Name, string AccountType, string Language, string? Bio, string? Website, int Streak, int UnreadNotifications,
    string? AvatarUrl = null, List<string>? Interests = null, bool IsAdmin = false, string? Email = null, bool EmailVerified = false,
    string Plan = "free", DateTime? ProUntil = null, bool Verified = false, int ChecksToday = 0, int ChecksPerDay = 0, BadgeDto? Badge = null);

/// <summary>AvatarUrl is versioned (?v=) so it can be cached hard; null when the account has no photo.</summary>
public sealed record UserRefDto(string Handle, string Name, string AccountType, string? AvatarUrl = null, bool Verified = false);

/// <summary>A person or brand in a list: search results, brands to follow.</summary>
public sealed record UserCardDto(UserRefDto User, int Followers, int Posts, bool Following);

public sealed record TagDto(string Tag, int Posts);

public sealed record AvatarDto(string? AvatarUrl);

public sealed record ViewerProfileDto(bool IsMe, bool Following);

/// <summary>
/// Featured: for a brand, looks it featured; for a person, their looks that were featured. Community: looks mentioning this
/// account. Verified: the owner confirmed this brand by hand (--verify); the check next to the brand mark.
/// </summary>
public sealed record ProfileDto(
    string Handle, string Name, string AccountType, string? Bio, string? Website,
    int Posts, int Followers, int Following, int FireReceived, int? BestScore, int Streak, DateTime CreatedAt,
    ViewerProfileDto Viewer, string? AvatarUrl = null, int Featured = 0, int Community = 0, bool Verified = false, BadgeDto? Badge = null);

/// <summary>Last week's place on one board, worn on the profile and on Me for the week that follows. Board is a <see cref="BoardName"/>.</summary>
public sealed record BadgeDto(string Board, int Rank, DateTime WeekStart);

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
    /// <summary>
    /// Rejected rows store nothing but the status; the neutral message is added here, in the check's language. The feedback
    /// is the stored document as it was written, so items carry brandSeen from rubric v3 on and nothing from before.
    /// </summary>
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

/// <summary>
/// BeforePostId: "after the tip" — one of your own earlier looks this one improves on. Items (Round 10): the pieces as the
/// post sheet leaves them, the whole list (PostItems.Apply); absent, the stylist's names go on the look as before.
/// </summary>
public sealed record CreatePostRequest(Guid CheckId, string? Caption, Guid? ChallengeId, List<ProductLinkDto>? Products, Guid? BeforePostId = null, List<PostItemInput>? Items = null);

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
    BeforeDto? Before = null,
    List<PostItemDto>? Items = null,
    int ItemCount = 0);

/// <summary>The earlier look an "after the tip" post improves on: its score and photo, for the before/after strip.</summary>
public sealed record BeforeDto(Guid PostId, int Score, string ImageUrl);

// ---- items on a look (Round 10) ----

/// <summary>
/// One piece on a look. Url is the raw store link (the client sends people through /api/items/{id}/out, never to it);
/// Host is its host for "Shop at {host}". Source: Stylist | User. X and Y are the dot on the photo, 0..1, null when
/// the item is listed and not placed. Confirmed: a stylist brand suggestion the person accepted.
/// </summary>
public sealed record PostItemDto(
    Guid Id, string Name, string Category, string? Brand, string? Model, string? Url, string? Host, ItemSource Source,
    double? X, double? Y, bool Confirmed);

/// <summary>
/// One item as the post sheet sends it. Id names an existing row to keep (its Source stays); no id means a new item,
/// Source User. Name ≤ 40, Brand ≤ 40, Model ≤ 60, Url http(s) ≤ 500, X and Y in 0..1 or both null.
/// </summary>
public sealed record PostItemInput(Guid? Id, string? Name, string? Category, string? Brand, string? Model, string? Url, double? X, double? Y, bool Confirmed = false);

/// <summary>The whole list, in order; rows not in it are removed. At most PostItems.MaxTagged.</summary>
public sealed record UpdateItemsRequest(List<PostItemInput>? Items);

/// <summary>GET /api/items: looks carrying the item asked for (a brand, a category, a free term), newest first.</summary>
public sealed record ItemsDto(string? Brand, string? Category, string? Q, List<PostDto> Posts, int? NextOffset = null);

/// <summary>GET /api/items/brands?q=: a brand name for the autocomplete, how many looks carry it, and the brand's account when it has one.</summary>
public sealed record BrandDto(string Name, int Looks, UserRefDto? Account = null);

public sealed record BrandsDto(List<BrandDto> Items);

// ---- the weekly board (Round 10) ----

/// <summary>
/// One place on a board. Post for the looks, rising, intent and picks boards; User (with Looks, how many they posted
/// that week) for the people board; Score for the picks board. Fires are the fires that counted.
/// </summary>
public sealed record BoardRowDto(int Rank, int Fires, PostDto? Post = null, UserRefDto? User = null, int? Looks = null, int? Score = null);

public sealed record BoardSponsorDto(string Name, string? Handle, string? PrizeText, string? Url);

/// <summary>The caller's own place on each board this week; null where they are not on it.</summary>
public sealed record BoardMeDto(int? Looks = null, int? People = null, int? Rising = null, int? Intent = null, int? Picks = null);

/// <summary>
/// GET /api/board?week=yyyy-MM-dd: the week (WeekStart and WeekEnd as UTC instants of the board's zone), ClosesIn in
/// seconds (0 once Closed), the five boards, the sponsor when the week has one, and the caller's places. Intents is
/// keyed by the StyleIntent name.
/// </summary>
public sealed record BoardDto(
    DateTime WeekStart, DateTime WeekEnd, int ClosesIn, bool Closed,
    List<BoardRowDto> Looks, List<BoardRowDto> People, List<BoardRowDto> Rising, Dictionary<string, List<BoardRowDto>> Intents, List<BoardRowDto> Picks,
    BoardSponsorDto? Sponsor = null, BoardMeDto? Me = null);

/// <summary>One archived place. ImageUrl is null when the look is gone or under review; the place stands.</summary>
public sealed record WeeklyWinnerDto(string Board, int Rank, UserRefDto User, Guid? PostId, string? ImageUrl, int Fires, int? Score);

public sealed record HallWeekDto(DateTime WeekStart, DateTime WeekEnd, List<WeeklyWinnerDto> Winners);

/// <summary>GET /api/board/hall: closed weeks, newest first.</summary>
public sealed record HallDto(List<HallWeekDto> Weeks);

/// <summary>POST /api/admin/board/exclude: a moderator pulls a look off this week's boards, with a reason (≤ 200).</summary>
public sealed record ExcludeRequest(Guid PostId, string? Reason);

public sealed record BoardExclusionDto(Guid PostId, string Reason, UserRefDto? By, DateTime CreatedAt);

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

/// <summary>ActorName is the actor's current display name, or the handle when there is none or the account is gone. Rank only on board_rank.</summary>
public sealed record NotificationDto(Guid Id, string Type, string ActorHandle, string ActorName, string? ActorAvatarUrl, Guid? PostId, Guid? ChallengeId, DateTime CreatedAt, bool Read, int? Rank = null);

public sealed record NotificationsDto(List<NotificationDto> Items, int Unread);

// ---- config, push, admin ----

/// <summary>Public, unauthenticated: what the client needs before it can do anything. No secrets.</summary>
public sealed record ConfigDto(long MaxImageBytes, long MaxVideoBytes, int MaxVideoSeconds, string? PushPublicKey, bool Email = false, bool Transcoding = false, PlansDto? Plans = null, AffiliateConfigDto? Affiliate = null);

/// <summary>Affiliate:Disclosure: whether the item sheet shows the commission line under a store link. The hosts and their parameters stay on the server.</summary>
public sealed record AffiliateConfigDto(bool Disclosure);

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
    int PushSubscriptions = 0,
    int ItemsTagged = 0,
    int ItemOuts = 0,
    int BoardViews = 0);

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
