namespace ShopHub.Modules.Identity.Contracts;

/// <summary>
/// Identity's public surface to other modules (spec §9.1).
/// <para>
/// This is the seam that makes the migration test in spec §3 real: extracting Identity into
/// its own service means swapping this implementation for an HTTP client and changing
/// nothing at any call site.
/// </para>
/// <para>
/// Returns Contracts DTOs, never entities. Callers get exactly the fields listed here and
/// have no way to reach a <c>User</c> or open Identity's DbContext.
/// </para>
/// </summary>
public interface IIdentityModuleApi
{
    /// <summary>
    /// Resolves display details for a set of user ids.
    /// <para>
    /// Batched by design. The order read model (spec §8.4) needs a customer name per row,
    /// and <c>Orders.UserId</c> lives in another module's schema - so this cannot be a SQL
    /// join. Calling it per row instead would be the N+1 that boundary buys you.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<UserSummaryDto>> GetUserSummariesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Active and inactive user counts, for the admin dashboard (spec §10.1).</summary>
    Task<UserCountsDto> GetUserCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of active users holding a role, capped at <paramref name="take"/>.
    /// <para>
    /// Bounded on purpose, like every other list in this solution (spec §6.5). Roles are
    /// data rather than an enum (spec A3), so the caller names the role as a string.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(
        string roleName,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Users registered per day over a window, for the dashboard chart. Returns only days
    /// that have data - the caller zero-fills the gaps, because a chart that silently omits
    /// quiet days lies about them (spec §10.1).
    /// </summary>
    Task<IReadOnlyList<DailyCountDto>> GetRegistrationsPerDayAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user id by email, for the guest-checkout prompt in spec §8.4.
    /// <para>
    /// Deliberately returns the id only. Ordering needs to know <em>whether</em> an account
    /// exists so it can show "an account exists for this email - log in to have this order
    /// in your history"; it must not learn anything else and must never auto-link on the
    /// strength of an unverified email.
    /// </para>
    /// </summary>
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>Display details for one user.</summary>
public sealed record UserSummaryDto(Guid Id, string FullName, string Email, bool IsActive);

public sealed record UserCountsDto(int Active, int Inactive)
{
    public int Total => Active + Inactive;
}

/// <summary>One day's count in a daily series.</summary>
public sealed record DailyCountDto(DateOnly Day, int Count);
