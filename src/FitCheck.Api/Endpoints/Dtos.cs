using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

public sealed record CreateUserRequest(string? Handle, bool Confirmed16Plus, string? Language);

public sealed record UpdateUserRequest(string? Language);

public sealed record UserDto(Guid Id, string Handle, string Language);

public sealed record ErrorDto(string Error);

public sealed record CheckDto(
    Guid Id,
    StyleIntent Intent,
    string? Occasion,
    string Language,
    DateTime CreatedAt,
    int LatencyMs,
    string Status,
    int? Score,
    OutfitFeedback? Feedback)
{
    /// <summary>Rejected rows store nothing but the status; the neutral message is added here, in the check's language.</summary>
    public static CheckDto FromEntity(OutfitCheck check, Localizer localizer)
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
            feedback);
    }
}

public sealed record PilotMetricsDto(
    int TotalChecks,
    int UsersWithAtLeastOneCheck,
    int UsersWithSecondCheckWithin7Days,
    double ReturnRate,
    int AvgLatencyMs,
    Dictionary<string, int> ScoreDistribution,
    Dictionary<string, int> ByLanguage,
    Dictionary<string, int> ByPromptVersion);
