using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// The "for you" score from PHASE3.md, kept pure and static so every term can be tested without a database:
/// ln(1 + fires) + 0.5·ln(1 + comments) + 2 if the viewer follows the author + 1 if the intent is one of the viewer's
/// interests + 0.5 if the viewer had an ok check with that intent lately + 1 if a brand featured it − 0.35 per day of age.
/// Deterministic for a given second, good enough for a pilot; a real recommender is a later phase.
/// </summary>
public static class FeedRanker
{
    public const double CommentWeight = 0.5;
    public const double FollowedWeight = 2;
    public const double InterestWeight = 1;
    public const double CheckedIntentWeight = 0.5;
    public const double FeaturedWeight = 1;

    /// <summary>Score lost for every 24 hours since posting.</summary>
    public const double DecayPerDay = 0.35;

    public static double Score(
        int fireCount, int commentCount, DateTime createdAt, DateTime now,
        bool authorFollowed, bool intentInInterests, bool viewerCheckedIntent, bool featured)
    {
        // A clock that runs slightly ahead of the database must not turn a brand-new post into a bonus.
        var hoursSincePost = Math.Max(0, (now - createdAt).TotalHours);
        return Math.Log(1 + Math.Max(0, fireCount))
               + CommentWeight * Math.Log(1 + Math.Max(0, commentCount))
               + (authorFollowed ? FollowedWeight : 0)
               + (intentInInterests ? InterestWeight : 0)
               + (viewerCheckedIntent ? CheckedIntentWeight : 0)
               + (featured ? FeaturedWeight : 0)
               - DecayPerDay * hoursSincePost / 24;
    }

    /// <summary>Highest score first, ties by newest. The sort is stable, so rows that tie on both keep the order they came in.</summary>
    public static List<T> Rank<T>(IEnumerable<T> candidates, Func<T, double> score, Func<T, DateTime> createdAt) =>
        candidates.OrderByDescending(score).ThenByDescending(createdAt).ToList();

    /// <summary>"Casual, office,Party" → {Casual, Office, Party}. Names that are not intents are skipped rather than failing the feed.</summary>
    public static HashSet<StyleIntent> ParseInterests(string? interests)
    {
        var result = new HashSet<StyleIntent>();
        if (string.IsNullOrWhiteSpace(interests))
        {
            return result;
        }

        foreach (var name in interests.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<StyleIntent>(name, ignoreCase: true, out var intent) && Enum.IsDefined(intent))
            {
                result.Add(intent);
            }
        }

        return result;
    }
}
