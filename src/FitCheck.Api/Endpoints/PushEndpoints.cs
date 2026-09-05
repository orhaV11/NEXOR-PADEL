namespace FitCheck.Api.Endpoints;

/// <summary>Web Push: the VAPID public key, subscribe and unsubscribe. Filled in by the push builder.</summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
