using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Shared.Infrastructure.Security;

/// <summary>
/// Reads the authenticated caller from the current request's JWT claims (spec §7.1).
/// <para>
/// Permissions are deliberately <em>not</em> read from the token - they change and a
/// 15-minute access token would serve stale ones. They are looked up server-side and
/// cached per spec §7.5.
/// </para>
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? Id
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub");

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public string? Name => Principal?.FindFirstValue(ClaimTypes.Name) ?? Principal?.FindFirstValue("name");

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid RequiredId => Id ?? throw new InvalidOperationException(
        "No authenticated user on this request. Endpoints reading RequiredId must call RequireAuthorization().");
}
