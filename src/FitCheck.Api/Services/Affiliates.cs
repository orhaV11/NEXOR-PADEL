using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 16 — the affiliate line, read and written in one place so the click route, the import and the numbers page
/// cannot disagree about what a host is called or what counts as income.
/// </summary>
public static class Affiliates
{
    /// <summary>
    /// The form a host is stored and reconciled in: lower-case, no "www.", no trailing dot. A partner reports
    /// "Zara.com" and a link says "www.zara.com"; a table keyed on the raw string would hold both and match neither.
    /// </summary>
    public static string NormaliseHost(string? host)
    {
        var name = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        return name.StartsWith("www.", StringComparison.Ordinal) ? name[4..] : name;
    }

    /// <summary>
    /// Record what a partner reported, or update what we already hold for it. The (host, external id) pair is the
    /// identity: a report re-imported, or the same sale coming back confirmed weeks after it came back expected,
    /// must move the row it already made. Without that every import adds the money again.
    /// <para>
    /// Where the report carries an item we recorded a click for, the look and its owner are filled in from that click,
    /// so a commission can be traced to the look that earned it.
    /// </para>
    /// </summary>
    public static async Task<Commission> RecordAsync(
        AppDbContext db, string host, string externalId, decimal amount, string currency, string state,
        DateTime occurredAt, Guid? itemId, DateTime now, CancellationToken ct)
    {
        var key = NormaliseHost(host);
        var id = externalId.Trim();
        var row = await db.Commissions.FirstOrDefaultAsync(c => c.Host == key && c.ExternalId == id, ct);
        if (row is null)
        {
            row = new Commission { Id = Guid.NewGuid(), Host = key, ExternalId = id };
            db.Commissions.Add(row);
        }

        row.AmountMinor = Commission.ToMinor(amount);
        row.Currency = currency.Trim().ToUpperInvariant();
        row.State = state;
        row.OccurredAt = occurredAt;
        row.UpdatedAt = now;

        if (itemId is { } item)
        {
            row.ItemId = item;
            var click = await db.ItemClicks.Where(c => c.ItemId == item)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.PostId, c.OwnerId })
                .FirstOrDefaultAsync(ct);
            if (click is not null)
            {
                row.PostId = click.PostId;
                row.OwnerId = click.OwnerId;
            }
        }

        return row;
    }
}
