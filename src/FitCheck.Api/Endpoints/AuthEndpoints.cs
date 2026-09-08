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
    /// <summary>Forgot-password and verification-resend requests: a fixed window per client address (Limits:RecoveryPerHourPerIp).</summary>
    public const string RecoveryPolicy = "recovery";
    public const int RecoveryPerHourPerIpDefault = 5;
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
        // Recovery: all three are public. The two that take a token are not rate-limited beyond the global brake: a token
        // is 256 random bits, and a person retyping a password must not be locked out of their own link.
        group.MapPost("/forgot", ForgotAsync).RequireRateLimiting(RecoveryPolicy);
        group.MapPost("/reset", ResetAsync);
        group.MapPost("/verify-email", VerifyEmailAsync);
        return app;
    }

    public static IResult Error(int status, string message) =>
        Results.Json(new ErrorDto(message), AppJson.Options, statusCode: status);

    /// <summary>
    /// The signed-in user as the client keeps it. IsAdmin is the row's flag, so every route that answers with "me" (signup,
    /// login, /me, the profile edits) says the same thing, and the same thing the admin gate says.
    /// </summary>
    public static async Task<MeDto> ToMeAsync(AppDbContext db, AppUser user, CancellationToken ct)
    {
        var unread = await db.Notifications.CountAsync(n => n.UserId == user.Id && n.ReadAt == null, ct);
        var interests = (user.Interests ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new MeDto(user.Id, user.Handle, user.Name, user.AccountType.ToString(), user.PreferredLanguage, user.Bio, user.Website, user.StreakCount, unread,
            PostReader.AvatarUrl(user.Handle, user.AvatarPath, user.AvatarVersion), interests, user.IsAdmin, user.Email, user.EmailVerifiedAt is not null);
    }

    /// <summary>
    /// Mints a verification token for the account's current address (voiding the open ones), saves, and mails the link.
    /// The token is saved before the mail goes out, so a send that fails leaves an address the person can ask a link for
    /// again; the caller answers 502 error.email_send_failed when this returns false.
    /// </summary>
    public static async Task<bool> SendVerificationAsync(
        HttpContext context, AppDbContext db, AppUser user, IEmailSender email, IOptions<EmailOptions> options, Localizer localizer, ILogger logger, CancellationToken ct)
    {
        if (user.Email is null)
        {
            throw new InvalidOperationException("No address to verify.");
        }

        var token = await RecoveryTokens.IssueAsync(db, user.Id, AuthTokenPurpose.Verify, user.Email, ct);
        await db.SaveChangesAsync(ct);

        var link = RecoveryTokens.VerifyLink(RecoveryTokens.Origin(context.Request, options.Value), token);
        var message = new EmailMessage(user.Email, localizer.Get(user.PreferredLanguage, "email.verify_subject"), localizer.Get(user.PreferredLanguage, "email.verify_body", user.Handle, link));
        try
        {
            await email.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Verification mail for {UserId} could not be sent", user.Id);
            return false;
        }
    }

    private static async Task<IResult> SignupAsync(
        SignupRequest body, HttpContext context, AppDbContext db, Localizer localizer, IPasswordHasher<AppUser> hasher, IOptions<AdminOptions> admins,
        CancellationToken ct)
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

        // A handle in Admin:Handles is the owner's, whether or not the owner has signed up yet (the sync at start promotes an
        // existing account, so the owner signs up before listing it). To anyone else it is simply taken: nobody gets to
        // register a handle that a later restart would promote.
        var handleLower = handle.ToLowerInvariant();
        if (admins.Value.Lists(handle) || await db.Users.AnyAsync(u => u.HandleLower == handleLower, ct))
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
        return Results.Json(await ToMeAsync(db, user, ct), AppJson.Options, statusCode: StatusCodes.Status201Created);
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
        return Results.Json(await ToMeAsync(db, user, ct), AppJson.Options);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken ct)
    {
        await Sessions.SignOutAsync(context);
        return Results.NoContent();
    }

    /// <summary>
    /// Always 202 with an empty body, whatever the handle or address stands for: an answer that differed would list who
    /// has an account. The mail goes out only for an account that is not suspended and whose address is verified, and it
    /// goes out in the background so the answer takes the same time either way and never waits on a mail server.
    /// </summary>
    private static async Task<IResult> ForgotAsync(
        ForgotPasswordRequest body, HttpContext context, AppDbContext db, IEmailSender email, IOptions<EmailOptions> options, Localizer localizer,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var key = body.HandleOrEmail?.Trim() ?? "";
        AppUser? user = null;
        if (key.Length is > 0 and <= RecoveryTokens.MaxEmailLength)
        {
            if (key.Contains('@'))
            {
                var address = key.ToLowerInvariant();
                user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);
            }
            else
            {
                user = await UserEndpoints.FindByHandleAsync(db, key, ct);
            }
        }

        if (email.Enabled && user is { Suspended: false, Email: not null, EmailVerifiedAt: not null })
        {
            var token = await RecoveryTokens.IssueAsync(db, user.Id, AuthTokenPurpose.Reset, user.Email, ct);
            await db.SaveChangesAsync(ct);
            var link = RecoveryTokens.ResetLink(RecoveryTokens.Origin(context.Request, options.Value), token);
            var message = new EmailMessage(user.Email, localizer.Get(user.PreferredLanguage, "email.reset_subject"), localizer.Get(user.PreferredLanguage, "email.reset_body", user.Handle, link));
            var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
            var userId = user.Id;
            // The sender is a singleton and the message is a value: nothing scoped is touched after the request ends.
            _ = Task.Run(async () =>
            {
                try
                {
                    await email.SendAsync(message, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reset mail for {UserId} could not be sent", userId);
                }
            }, CancellationToken.None);
        }

        return Results.Accepted();
    }

    /// <summary>
    /// A valid reset token plus a password: the password changes, every reset token of the account is spent, and the
    /// person is signed in. A suspended account gets the same refusal as a bad token, so the door says nothing new.
    /// </summary>
    private static async Task<IResult> ResetAsync(
        ResetPasswordRequest body, HttpContext context, AppDbContext db, Localizer localizer, IPasswordHasher<AppUser> hasher, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        var token = await RecoveryTokens.FindValidAsync(db, body.Token, AuthTokenPurpose.Reset, ct);
        var user = token is null ? null : await db.Users.FindAsync([token.UserId], ct);
        if (token is null || user is null || user.Suspended)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.token_invalid"));
        }

        // The link stays valid past a password the rule refuses: the person fixes the password, not the link.
        var password = body.Password ?? "";
        if (password.Length < PasswordMinLength || password.Length > 200)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.password_short"));
        }

        var now = DateTime.UtcNow;
        user.PasswordHash = hasher.HashPassword(user, password);
        await RecoveryTokens.VoidOpenAsync(db, user.Id, AuthTokenPurpose.Reset, now, ct);
        token.UsedAt = now;
        await db.SaveChangesAsync(ct);

        await Sessions.SignInAsync(context, user);
        return Results.Json(await ToMeAsync(db, user, ct), AppJson.Options);
    }

    /// <summary>
    /// Marks the address verified when the token is open and was issued for the address the account still has (a link
    /// mailed to an earlier address proves nothing about the current one). Works signed out: the token names the user,
    /// and nobody is signed in by it.
    /// </summary>
    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest body, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        var token = await RecoveryTokens.FindValidAsync(db, body.Token, AuthTokenPurpose.Verify, ct);
        var user = token is null ? null : await db.Users.FindAsync([token.UserId], ct);
        if (token is null || user is null || user.Email is null || token.Email != user.Email)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.token_invalid"));
        }

        var now = DateTime.UtcNow;
        user.EmailVerifiedAt = now;
        token.UsedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Json(await ToMeAsync(db, user, ct), AppJson.Options);
    }

    private static async Task<IResult> MeAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        // The account may be gone (401) or suspended (403) while the cookie lived on; either way the cookie goes with the answer.
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        return Results.Json(await ToMeAsync(db, user, ct), AppJson.Options);
    }
}
