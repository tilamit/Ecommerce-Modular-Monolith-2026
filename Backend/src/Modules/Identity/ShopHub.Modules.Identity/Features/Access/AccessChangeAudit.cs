using System.Globalization;
using System.Text.Json;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Identity.Domain;

namespace ShopHub.Modules.Identity.Features.Access;

/// <summary>
/// Writes the audit entry for a change to the permissions or menus granted to a role.
/// <para>
/// The audit interceptor cannot see these changes. Saving the access page only adds and
/// removes <see cref="RolePermission"/> and <see cref="RoleMenuItem"/> link rows, which are
/// not auditable entities, while the <see cref="Role"/> row itself is left untouched - so a
/// grant could be changed without leaving any trace. Auditing the link rows one by one would
/// not help much either: each entry would hold a pair of ids and nothing a reader could
/// recognise. One entry per save records the role's full list before and after instead.
/// </para>
/// </summary>
internal static class AccessChangeAudit
{
    internal const string PermissionsField = "Permissions";

    internal const string MenusField = "Menus";

    /// <param name="before">The granted items before the save, in a stable order.</param>
    /// <param name="after">The granted items after the save, in the same order.</param>
    internal static void Write(
        IAuditWriter audit,
        Role role,
        string field,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after)
    {
        // Saving the page without changing anything is not an access change.
        if (before.SequenceEqual(after, StringComparer.Ordinal))
        {
            return;
        }

        audit.Write(new AuditEntryDto
        {
            Action = AuditAction.PermissionChange,
            Module = "identity",
            EntityName = nameof(Role),
            EntityId = role.Id.ToString("D", CultureInfo.InvariantCulture),
            OldValues = Serialize(role.Name, field, before),
            NewValues = Serialize(role.Name, field, after),
            ChangedColumns = JsonSerializer.Serialize(new[] { field }),
        });
    }

    // The role name goes on both sides so the entry reads on its own, without looking the id up.
    private static string Serialize(string roleName, string field, IReadOnlyList<string> items) =>
        JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Role"] = roleName,
            [field] = items,
        });
}
