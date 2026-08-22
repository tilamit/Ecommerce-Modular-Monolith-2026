using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Identity.Domain;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Identity.Features.Users;

/// <summary>User administration (spec §9.1).</summary>
internal sealed class UserService(IdentityDbContext db, IPasswordService passwords, IPermissionService permissions)
{
    /// <summary>
    /// Sortable columns, whitelisted (spec §6.5). A client string never reaches
    /// <c>OrderBy</c> - that path is how ORDER BY injection and accidental table scans get in.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<User, object?>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = u => u.Email,
            ["firstName"] = u => u.FirstName,
            ["lastName"] = u => u.LastName,
            ["createdUtc"] = u => u.CreatedUtc,
            ["lastLoginUtc"] = u => u.LastLoginUtc,
            ["isActive"] = u => u.IsActive,
        };

    internal async Task<PagedResult<UserListItem>> GetUsersAsync(
        PagedRequest paging,
        UserFilter filter,
        CancellationToken cancellationToken)
    {
        var request = paging.Normalized();
        var query = Filtered(filter);

        var total = await query.LongCountAsync(cancellationToken);

        var items = await Sort(query, request)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(u => new UserListItem(
                u.Id,
                u.Email,
                u.FirstName + " " + u.LastName,
                u.PhoneNumber,
                u.IsActive,
                u.CreatedUtc,
                u.LastLoginUtc,
                u.Roles.Select(r => r.Role.Name).ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserListItem>(items, request.Page, request.PageSize, total);
    }

    internal async Task<UserDetail> GetUserByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserDetail(
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.IsActive,
                u.IsEmailConfirmed,
                u.CreatedUtc,
                u.ModifiedUtc,
                u.LastLoginUtc,
                u.LockoutEndUtc,
                u.Roles.Select(r => new RoleRef(r.RoleId, r.Role.Name)).ToList()))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw NotFoundException.For("User", id);

    internal async Task<UserDetail> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(request.Email);

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new ConflictException("email_taken", "An account with that email already exists.");
        }

        await EnsureRolesExistAsync(request.RoleIds, cancellationToken);

        var user = User.Create(
            request.Email,
            passwords.Hash(request.Password),
            request.FirstName,
            request.LastName,
            request.PhoneNumber);

        user.ReplaceRoles(request.RoleIds);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return await GetUserByIdAsync(user.Id, cancellationToken);
    }

    internal async Task<UserDetail> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await TrackedUserAsync(id, cancellationToken);
        var normalized = User.Normalize(request.Email);

        if (!string.Equals(user.NormalizedEmail, normalized, StringComparison.Ordinal))
        {
            var taken = await db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != id, cancellationToken);

            if (taken)
            {
                throw new ConflictException("email_taken", "An account with that email already exists.");
            }

            user.ChangeEmail(request.Email);
        }

        user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber);
        await db.SaveChangesAsync(cancellationToken);

        return await GetUserByIdAsync(id, cancellationToken);
    }

    internal async Task SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await TrackedUserAsync(id, cancellationToken);

        if (isActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();

            // A deactivated user must not keep browsing on an unexpired refresh token.
            await db.RefreshTokens
                .Where(t => t.UserId == id && t.RevokedUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(t => t.RevokedUtc, DateTime.UtcNow)
                        .SetProperty(t => t.RevokedReason, RevocationReasons.UserDeactivated),
                    cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateUserAsync(id, cancellationToken);
    }

    /// <summary>Soft delete (spec A7). The interceptor turns the remove into a flag update.</summary>
    internal async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await TrackedUserAsync(id, cancellationToken);

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateUserAsync(id, cancellationToken);
    }

    internal async Task<UserDetail> SetRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        await EnsureRolesExistAsync(roleIds, cancellationToken);

        var user = await db.Users
            .AsTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw NotFoundException.For("User", id);

        user.ReplaceRoles(roleIds);
        await db.SaveChangesAsync(cancellationToken);

        // The user's effective permissions just changed; the cached set is now wrong.
        await permissions.InvalidateUserAsync(id, cancellationToken);

        return await GetUserByIdAsync(id, cancellationToken);
    }

    internal async Task<UserDetail> UpdateMyProfileAsync(
        Guid userId,
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await TrackedUserAsync(userId, cancellationToken);

        // Deliberately does not accept email or roles: a customer editing their own
        // profile must not be able to change either.
        user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber);
        await db.SaveChangesAsync(cancellationToken);

        return await GetUserByIdAsync(userId, cancellationToken);
    }

    internal async Task<(int Active, int Inactive)> GetCountsAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Users
            .GroupBy(u => u.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return (
            counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0,
            counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0);
    }

    private IQueryable<User> Filtered(UserFilter filter)
    {
        var query = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();

            // Prefix match on the indexed columns. A leading wildcard cannot use an
            // index and is the first thing to fall over as the table grows (spec §8.2).
            query = query.Where(u =>
                u.Email.StartsWith(term)
                || u.FirstName.StartsWith(term)
                || u.LastName.StartsWith(term));
        }

        if (filter.RoleId is { } roleId)
        {
            query = query.Where(u => u.Roles.Any(r => r.RoleId == roleId));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        if (filter.CreatedFrom is { } from)
        {
            query = query.Where(u => u.CreatedUtc >= from);
        }

        if (filter.CreatedTo is { } to)
        {
            query = query.Where(u => u.CreatedUtc <= to);
        }

        return query;
    }

    /// <summary>
    /// Applies a whitelisted sort with a deterministic tiebreaker. Without the tiebreaker,
    /// two rows with equal sort keys can swap between pages, so paging silently skips one
    /// and repeats the other (spec §6.5).
    /// </summary>
    private static IQueryable<User> Sort(IQueryable<User> query, PagedRequest request)
    {
        var field = request.SortField;

        if (field is null || !Sortable.TryGetValue(field, out var selector))
        {
            return query.OrderByDescending(u => u.CreatedUtc).ThenBy(u => u.Id);
        }

        return request.SortDescending
            ? query.OrderByDescending(selector).ThenBy(u => u.Id)
            : query.OrderBy(selector).ThenBy(u => u.Id);
    }

    private async Task<User> TrackedUserAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
        ?? throw NotFoundException.For("User", id);

    private async Task EnsureRolesExistAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            throw new DomainRuleException("user_requires_role", "A user must have at least one role.");
        }

        var found = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), cancellationToken);

        if (found != roleIds.Distinct().Count())
        {
            throw new NotFoundException("role_not_found", "One or more of the supplied roles do not exist.");
        }
    }
}
