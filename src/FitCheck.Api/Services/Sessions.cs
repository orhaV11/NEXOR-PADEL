using System.Security.Claims;
using FitCheck.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FitCheck.Api.Services;

/// <summary>Cookie session helpers. The cookie carries the user id and handle; everything else is read from the database.</summary>
public static class Sessions
{
    public const string CookieName = "orevosh.session";

    /// <summary>Every state-changing API call must carry this header; cross-site forms cannot set it, so it doubles as the CSRF token.</summary>
    public const string RequestHeader = "X-Requested-With";
    public const string RequestHeaderValue = "Orevosh";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid RequiredUserId(ClaimsPrincipal principal) =>
        UserId(principal) ?? throw new InvalidOperationException("Endpoint requires authorization but no user id claim is present.");

    public static Task SignInAsync(HttpContext context, AppUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Handle),
            new Claim("account_type", user.AccountType.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
    }

    public static Task SignOutAsync(HttpContext context) =>
        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
