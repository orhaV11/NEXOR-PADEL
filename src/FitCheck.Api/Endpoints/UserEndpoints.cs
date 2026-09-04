using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

public static class UserEndpoints
{
    public const int HandleMinLength = 2;
    public const int HandleMaxLength = 40;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPost("/", CreateAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapGet("/{id:guid}/checks", ListChecksAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest body, HttpRequest request, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var language = Localizer.Resolve(body.Language, request);

        // No account without the self-declaration. Real age assurance comes before public launch.
        if (!body.Confirmed16Plus)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.age_required"));
        }

        var handle = body.Handle?.Trim() ?? "";
        if (!IsValidHandle(handle))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.handle_invalid"));
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Handle = handle,
            Confirmed16Plus = true,
            PreferredLanguage = language,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/users/{user.Id}", new UserDto(user.Id, user.Handle, user.PreferredLanguage));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateUserRequest body, HttpRequest request, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, request), "error.user_not_found"));
        }

        var language = body.Language?.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (!Localizer.IsSupported(language))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.language_invalid"));
        }

        user.PreferredLanguage = language!;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new UserDto(user.Id, user.Handle, user.PreferredLanguage));
    }

    /// <summary>Removes the user, every check and every photo file. There is no soft delete and no recovery.</summary>
    private static async Task<IResult> DeleteAsync(
        Guid id, HttpRequest request, AppDbContext db, IImageStore images, Localizer localizer, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, request), "error.user_not_found"));
        }

        // Files first: if the row delete fails the user can retry, but an orphaned photo would have no owner to delete it.
        images.DeleteUser(id);
        await db.Checks.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ListChecksAsync(
        Guid id, HttpRequest request, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, request), "error.user_not_found"));
        }

        var checks = await db.Checks
            .Where(c => c.UserId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Results.Ok(checks.Select(c => CheckDto.FromEntity(c, localizer)).ToList());
    }

    public static bool IsValidHandle(string handle) =>
        handle.Length is >= HandleMinLength and <= HandleMaxLength && !handle.Any(char.IsControl);

    public static IResult Error(int status, string message) =>
        Results.Json(new ErrorDto(message), AppJson.Options, statusCode: status);
}
