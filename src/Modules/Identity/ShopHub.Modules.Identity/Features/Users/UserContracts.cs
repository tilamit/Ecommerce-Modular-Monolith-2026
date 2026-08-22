using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Identity.Features.Users;

/// <summary>Row in the admin user grid. A projection, never an entity (spec §6.5).</summary>
internal sealed record UserListItem(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc,
    IReadOnlyList<string> Roles);

internal sealed record UserDetail(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    bool IsEmailConfirmed,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc,
    DateTime? LastLoginUtc,
    DateTime? LockoutEndUtc,
    IReadOnlyList<RoleRef> Roles);

internal sealed record RoleRef(Guid Id, string Name);

/// <summary>
/// Admin user filters (spec §9.1). Bound from the query string alongside
/// <see cref="PagedRequest"/>.
/// </summary>
internal sealed record UserFilter(
    string? Search,
    Guid? RoleId,
    bool? IsActive,
    DateTime? CreatedFrom,
    DateTime? CreatedTo);

internal sealed record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    IReadOnlyList<Guid> RoleIds);

internal sealed record UpdateUserRequest(
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber);

internal sealed record SetUserStatusRequest(bool IsActive);

internal sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

internal sealed record UpdateMyProfileRequest(string FirstName, string LastName, string? PhoneNumber);
