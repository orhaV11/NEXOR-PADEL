using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The answer of a route a round maps before its builders fill it: 501 with the app's { error } shape in the caller's
/// language, so a client that finds the route early gets a sentence, not a stack. Delete this file with the last stub.
/// </summary>
public static class Stubs
{
    public static IResult NotBuilt(HttpContext context)
    {
        var localizer = context.RequestServices.GetRequiredService<Localizer>();
        return AuthEndpoints.Error(StatusCodes.Status501NotImplemented, localizer.Get(Localizer.Resolve(null, context.Request), "error.not_built"));
    }
}
