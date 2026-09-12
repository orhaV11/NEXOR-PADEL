using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Items on a look (Round 10): the owner tags the pieces (brand, model, store link, a dot on the photo), anyone can find
/// looks by brand or category, and a store link leaves the app through one door. Skeleton: every route answers 501 until
/// the items-server builder fills it. The contract:
/// <list type="bullet">
/// <item><c>PATCH /api/posts/{id}/items</c> (owner): the whole list (<see cref="UpdateItemsRequest"/>), at most
/// <see cref="PostItems.MaxTagged"/> items; name ≤ 40, brand ≤ 40, model ≤ 60, url http(s) ≤ 500
/// (<see cref="PostItems.IsStoreUrl"/>), x and y in 0..1. Answers the post's <see cref="PostItemDto"/> list. Errors:
/// error.items_too_many, error.item_invalid, error.item_url_invalid, error.item_position_invalid, error.post_not_found, error.forbidden.</item>
/// <item><c>GET /api/items?brand=&amp;category=&amp;q=&amp;offset=&amp;limit=</c> (public): <see cref="ItemsDto"/>, visible looks only.</item>
/// <item><c>GET /api/items/brands?q=</c> (public): <see cref="BrandsDto"/> for the autocomplete: brand accounts and brands already used on looks.</item>
/// <item><c>GET /api/items/{id}/out</c> (public): 302 to the item's url with the affiliate parameters for its host
/// (<see cref="Domain.AffiliateOptions.ParametersFor"/>), <c>Referrer-Policy: no-referrer</c>, counted in
/// <see cref="Domain.CounterName.ItemOuts"/>; 404 for an item without a link or on a hidden look.</item>
/// </list>
/// </summary>
public static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/posts/{id:guid}/items", UpdateItemsAsync).RequireAuthorization();
        app.MapGet("/api/items", SearchAsync);
        app.MapGet("/api/items/brands", BrandsAsync);
        app.MapGet("/api/items/{id:guid}/out", OutAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static IResult UpdateItemsAsync(HttpContext context, Guid id, UpdateItemsRequest? request) => Stubs.NotBuilt(context);

    private static IResult SearchAsync(HttpContext context, string? brand, string? category, string? q, int? offset, int? limit) => Stubs.NotBuilt(context);

    private static IResult BrandsAsync(HttpContext context, string? q) => Stubs.NotBuilt(context);

    private static IResult OutAsync(HttpContext context, Guid id) => Stubs.NotBuilt(context);
}
