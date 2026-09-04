using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

public sealed record ErrorDto(string Error);

// ---- auth and users ----

public sealed record SignupRequest(string? Handle, string? Password, bool Confirmed16Plus, string? Language, string? AccountType, string? DisplayName);

public sealed record LoginRequest(string? Handle, string? Password);

public sealed record UpdateMeRequest(string? Language, string? DisplayName, string? Bio, string? Website, string? AccountType = null, List<string>? Interests = null);

/// <summary>The signed-in user, as the client keeps it in memory.</summary>
public sealed record MeDto(
    Guid Id, string Handle, string Name, string AccountType, string Language, string? Bio, string? Website, int Streak, int UnreadNotifications,
    string? AvatarUrl = null, List<string>? Interests = null);

/// <summary>AvatarUrl is versioned (?v=) so it can be cached hard; null when the account has no photo.</summary>
public sealed record UserRefDto(string Handle, string Name, string AccountType, string? AvatarUrl = null);

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

public sealed record CreatePostRequest(Guid CheckId, string? Caption, Guid? ChallengeId, List<ProductLinkDto>? Products);

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
    UserRefDto? FeaturedBy);

public sealed record FeatureStateDto(UserRefDto? FeaturedBy);

// ---- explore ----

public sealed record ExploreDto(List<TagDto> TrendingTags, List<UserCardDto> Brands, List<PostDto> TopLooks, List<ChallengeDto> Challenges);

public sealed record SearchDto(List<UserCardDto> Users, List<TagDto> Tags);

public sealed record FireStateDto(int FireCount, bool Fired);

public sealed record SaveStateDto(bool Saved);

public sealed record CreateCommentRequest(string? Text);

public sealed record CommentDto(Guid Id, UserRefDto User, string Text, bool IsMine, bool CanDelete, DateTime CreatedAt);

public sealed record FeedDto(List<PostDto> Items, int? NextOffset);

// ---- challenges ----

public sealed record CreateChallengeRequest(string? Title, string? Brief, string? Intent, string? Prize, string? PrizeUrl, DateTime? EndsAt);

public sealed record VoteRequest(Guid PostId);

public sealed record ChallengeViewerDto(bool IsBrand, bool HasEntered, Guid? VotedPostId, Guid? MyEntryId);

public sealed record ChallengeDto(
    Guid Id,
    UserRefDto Brand,
    string Title,
    string Brief,
    StyleIntent Intent,
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
    int Featured = 0);

public sealed record PilotMetricsDto(
    int TotalChecks,
    int UsersWithAtLeastOneCheck,
    int UsersWithSecondCheckWithin7Days,
    double ReturnRate,
    int AvgLatencyMs,
    Dictionary<string, int> ScoreDistribution,
    Dictionary<string, int> ByLanguage,
    Dictionary<string, int> ByPromptVersion,
    SocialMetricsDto? Social = null);
