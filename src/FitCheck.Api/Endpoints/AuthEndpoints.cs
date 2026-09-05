using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

public static partial class AuthEndpoints
{
    public const string SignupPolicy = "signup";
    public const string LoginPolicy = "login";
    public const int PasswordMinLength = 8;

    // Handles appear in URLs and in notifications; letters in any script, digits, dot and underscore.
    [GeneratedRegex(@"^[\p{L}\p{N}_.]{2,40}$")]
    private static partial Regex HandleRegex();

    private static readonly HashSet<string> ReservedHandles =
        new(StringComparer.OrdinalIgnoreCase) { "me", "admin", "fitcheck", "orevosh", "support", "brand", "challenges", "feed", "api", "explore", "search", "tag", "tags" };

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/signup", SignupAsync).RequireRateLimiting(SignupPolicy);
        group.MapPost("/login", LoginAsync).RequireRateLimiting(LoginPolicy);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/me", MeAsync).RequireAuthorization();
        return app;
    }

    public static IResult Error(int status, string message) =>
        Results.Json(new ErrorDto(message), AppJson.Options, statusCode: status);

    /// <summary>
    /// The signed-in user as the client keeps it. IsAdmin comes from Admin:Handles, read from the request's services rather
    /// than injected, so every route that answers with "me" (signup, login, /me, the profile edits) says the same thing.
    /// </summary>
    public static async Task<MeDto> ToMeAsync(HttpContext context, AppDbContext db, AppUser user, CancellationToken ct)
    {
        var unread = await db.Notifications.CountAsync(n => n.UserId == user.Id && n.ReadAt == null, ct);
        var interests = (user.Interests ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var admin = context.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        return new MeDto(user.Id, user.Handle, user.Name, user.AccountType.ToString(), user.PreferredLanguage, user.Bio, user.Website, user.StreakCount, unread,
            PostReader.AvatarUrl(user.Handle, user.AvatarPath, user.AvatarVersion), interests, admin.IsAdmin(user.Handle));
    }

    private static async Task<IResult> SignupAsync(
        SignupRequest body, HttpContext context, AppDbContext db, Localizer localizer, IPasswordHasher<AppUser> hasher, CancellationToken ct)
    {
        var language = Localizer.Resolve(body.Language, context.Request);

        // No account without the self-declaration. Real age assurance comes before public launch.
        if (!body.Confirmed16Plus)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.age_required"));
        }

        var handle = body.Handle?.Trim() ?? "";
        if (!HandleRegex().IsMatch(handle) || ReservedHandles.Contains(handle))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.handle_format"));
        }

        var password = body.Password ?? "";
        if (password.Length < PasswordMinLength || password.Length > 200)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.password_short"));
        }

        var displayName = OutfitAnalyzer.SanitizeText(body.DisplayName, multiline: false) is { Length: > 0 } cleanName ? cleanName : null;
        if (displayName is { Length: > 40 })
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.profile_invalid"));
        }

        var accountType = Enum.TryParse<AccountType>(body.AccountType, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AccountType.Person;

        var handleLower = handle.ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.HandleLower == handleLower, ct))
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(language, "error.handle_taken"));
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Handle = handle,
            HandleLower = handleLower,
            AccountType = accountType,
            DisplayName = displayName,
            Confirmed16Plus = true,
            PreferredLanguage = language,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two signups raced for the same handle; the unique index decided.
            return Error(StatusCodes.Status409Conflict, localizer.Get(language, "error.handle_taken"));
        }

        await Sessions.SignInAsync(context, user);
        return Results.Json(await ToMeAsync(context, db, user, ct), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest body, HttpContext context, AppDbContext db, Localizer localizer, IPasswordHasher<AppUser> hasher, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        var handleLower = (body.Handle ?? "").Trim().ToLowerInvariant();
        var user = handleLower.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.HandleLower == handleLower, ct);

        // Same message for unknown handle and wrong password, so handles cannot be probed.
        if (user is null)
        {
            return Error(StatusCodes.Status401Unauthorized, localizer.Get(language, "error.login_failed"));
        }

        var verdict = hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password ?? "");
        if (verdict == PasswordVerificationResult.Failed)
        {
            return Error(StatusCodes.Status401Unauthorized, localizer.Get(user.PreferredLanguage, "error.login_failed"));
        }

        // Only after the password checked out: a stranger probing handles learns nothing from this door being shut.
        if (user.Suspended)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(user.PreferredLanguage, "error.suspended"));
        }

        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, body.Password ?? "");
            await db.SaveChangesAsync(ct);
        }

        await Sessions.SignInAsync(context, user);
        return Results.Json(await ToMeAsync(context, db, user, ct), AppJson.Options);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken ct)
    {
        await Sessions.SignOutAsync(context);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        // The account may be gone (401) or suspended (403) while the cookie lived on; either way the cookie goes with the answer.
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        return Results.Json(await ToMeAsync(context, db, user, ct), AppJson.Options);
    }
}
