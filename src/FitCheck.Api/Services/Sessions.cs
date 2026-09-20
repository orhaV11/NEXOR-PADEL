using System.Security.Claims;
using System.Security.Cryptography;
using FitCheck.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

namespace FitCheck.Api.Services;

/// <summary>Cookie session helpers. The cookie carries the user id and handle; everything else is read from the database.</summary>
public static class Sessions
{
    public const string CookieName = "orevosh.session";

    /// <summary>Every state-changing API call must carry this header; cross-site forms cannot set it, so it doubles as the CSRF token.</summary>
    public const string RequestHeader = "X-Requested-With";
    public const string RequestHeaderValue = "Orevosh";

    /// <summary>
    /// The ticket's own random id (Round 13, <see cref="Security.SessionRevocation"/>): 32 random bytes as base64url, kept
    /// in the encrypted ticket, never in a claim the client could read back. Logout revokes this one id; a password reset
    /// cuts off every ticket issued before it.
    /// </summary>
    public const string SessionIdKey = "sid";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid RequiredUserId(ClaimsPrincipal principal) =>
        UserId(principal) ?? throw new InvalidOperationException("Endpoint requires authorization but no user id claim is present.");

    /// <summary>The session id inside a ticket's properties, or null for a ticket issued before Round 13.</summary>
    public static string? SessionId(AuthenticationProperties? properties) =>
        properties is not null && properties.Items.TryGetValue(SessionIdKey, out var id) && !string.IsNullOrEmpty(id) ? id : null;

    /// <summary>The session id of the request's own ticket (what authentication read), and when that ticket expires.</summary>
    public static (string? Id, DateTimeOffset? Expires) CurrentSession(HttpContext context)
    {
        var properties = context.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties;
        return (SessionId(properties), properties?.ExpiresUtc);
    }

    public static Task SignInAsync(HttpContext context, AppUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Handle),
            new Claim("account_type", user.AccountType.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties { IsPersistent = true, AllowRefresh = true };
        properties.Items[SessionIdKey] = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
    }

    public static Task SignOutAsync(HttpContext context) =>
        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
