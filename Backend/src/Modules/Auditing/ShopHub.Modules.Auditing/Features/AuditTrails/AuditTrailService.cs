using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Auditing.Domain;
using ShopHub.Modules.Auditing.Persistence;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Auditing.Features.AuditTrails;

internal sealed record AuditTrailListItem(
    long Id,
    DateTime OccurredUtc,
    string Action,
    string Module,
    string EntityName,
    string? EntityId,
    Guid? UserId,
    string? UserName,
    string? ScreenName,
    string? HttpMethod,
    string? Path,
    string? IpAddress,
    string? CorrelationId);

/// <summary>Full entry including the value diff (spec §9.4).</summary>
internal sealed record AuditTrailDetail(
    long Id,
    DateTime OccurredUtc,
    string Action,
    string Module,
    string EntityName,
    string? EntityId,
    Guid? UserId,
    string? UserName,
    string? UserRoles,
    string? ScreenName,
    string? HttpMethod,
    string? Path,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string? OldValues,
    string? NewValues,
    string? ChangedColumns);

internal sealed record AuditTrailFilter(
    string? Search,
    IReadOnlyList<AuditAction>? Actions,
    string? Module,
    string? EntityName,
    Guid? UserId,
    string? ScreenName,
    DateTime? DateFrom,
    DateTime? DateTo);

/// <summary>
/// Reads the audit trail (spec §9.4).
/// </summary>
/// <remarks>
/// Keyset-paginated from the start, because this table grows fastest (spec §6.5). Offset
/// paging degrades as the offset grows - <c>OFFSET 500000 ROWS</c> makes the server walk
/// half a million rows to discard them. A keyset seek goes straight to the row.
/// <para>
/// There is no update and no delete here and no admin override. The trail is append-only
/// (spec §9.4) and <c>AuditTrailIsAppendOnly</c> in the architecture tests enforces it.
/// </para>
/// </remarks>
internal sealed class AuditTrailService(AuditingDbContext db)
{
    private const int MaxPageSize = 100;

    internal async Task<CursorResult<AuditTrailListItem>> GetAsync(
        string? cursor,
        int pageSize,
        AuditTrailFilter filter,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = Filtered(filter);

        if (AuditCursor.TryParse(cursor, out var afterId))
        {
            // Descending by id, so "after" means "older than".
            query = query.Where(a => a.Id < afterId);
        }

        // Fetch one extra to learn whether another page exists, without a second COUNT.
        var rows = await query
            .OrderByDescending(a => a.Id)
            .Take(take + 1)
            .Select(a => new AuditTrailListItem(
                a.Id,
                a.OccurredUtc,
                a.Action.ToString(),
                a.Module,
                a.EntityName,
                a.EntityId,
                a.UserId,
                a.UserName,
                a.ScreenName,
                a.HttpMethod,
                a.Path,
                a.IpAddress,
                a.CorrelationId))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        var items = hasMore ? rows[..take] : rows;

        var nextCursor = hasMore && items.Count > 0
            ? AuditCursor.Create(items[^1].Id)
            : null;

        return new CursorResult<AuditTrailListItem>(items, nextCursor);
    }

    internal async Task<AuditTrailDetail> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        await db.AuditTrails
            .Where(a => a.Id == id)
            .Select(a => new AuditTrailDetail(
                a.Id,
                a.OccurredUtc,
                a.Action.ToString(),
                a.Module,
                a.EntityName,
                a.EntityId,
                a.UserId,
                a.UserName,
                a.UserRoles,
                a.ScreenName,
                a.HttpMethod,
                a.Path,
                a.IpAddress,
                a.UserAgent,
                a.CorrelationId,
                a.OldValues,
                a.NewValues,
                a.ChangedColumns))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw NotFoundException.For("AuditTrail", id);

    /// <summary>The dashboard tile: page one only, paged independently thereafter (spec §10.1).</summary>
    internal async Task<IReadOnlyList<AuditTrailListItem>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        await db.AuditTrails
            .OrderByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, 50))
            .Select(a => new AuditTrailListItem(
                a.Id,
                a.OccurredUtc,
                a.Action.ToString(),
                a.Module,
                a.EntityName,
                a.EntityId,
                a.UserId,
                a.UserName,
                a.ScreenName,
                a.HttpMethod,
                a.Path,
                a.IpAddress,
                a.CorrelationId))
            .ToListAsync(cancellationToken);

    private IQueryable<AuditTrail> Filtered(AuditTrailFilter filter)
    {
        var query = db.AuditTrails.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();

            query = query.Where(a =>
                a.EntityName.StartsWith(term)
                || (a.UserName != null && a.UserName.StartsWith(term))
                || (a.EntityId != null && a.EntityId.StartsWith(term)));
        }

        if (filter.Actions is { Count: > 0 } actions)
        {
            query = query.Where(a => actions.Contains(a.Action));
        }

        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            query = query.Where(a => a.Module == filter.Module);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            query = query.Where(a => a.EntityName == filter.EntityName);
        }

        if (filter.UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ScreenName))
        {
            query = query.Where(a => a.ScreenName == filter.ScreenName);
        }

        if (filter.DateFrom is { } from)
        {
            query = query.Where(a => a.OccurredUtc >= from);
        }

        if (filter.DateTo is { } to)
        {
            query = query.Where(a => a.OccurredUtc <= to);
        }

        return query;
    }
}

/// <summary>
/// Opaque cursor for keyset pagination (spec §6.5).
/// <para>
/// Base64url over the last id. Opaque on purpose: a client that learns the cursor is "just
/// the id" will start constructing them and the shape can then never change.
/// </para>
/// </summary>
internal static class AuditCursor
{
    internal static string Create(long lastId) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(lastId.ToString(CultureInfo.InvariantCulture)));

    internal static bool TryParse(string? cursor, out long lastId)
    {
        lastId = 0;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));

            return long.TryParse(decoded, NumberStyles.Integer, CultureInfo.InvariantCulture, out lastId);
        }
        catch (FormatException)
        {
            // A malformed cursor means "start from the beginning" rather than an error -
            // a stale bookmark should not break the page.
            return false;
        }
    }
}
