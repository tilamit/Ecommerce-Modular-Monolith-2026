using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Identity.Domain;

/// <summary>
/// A role is a database row, not an enum (spec A3). Adding role #3 is data, not a deploy.
/// </summary>
internal sealed class Role : AuditableEntity, IAuditableEntity
{
    private readonly List<RolePermission> _permissions = [];
    private readonly List<RoleMenuItem> _menuItems = [];

    private Role()
    {
    }

    private Role(Guid id, string name, string? description, bool isSystemRole)
        : base(id)
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        Description = description;
        IsSystemRole = isSystemRole;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>System roles (Admin, Customer) cannot be deleted (spec §8.1).</summary>
    public bool IsSystemRole { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public IReadOnlyCollection<RoleMenuItem> MenuItems => _menuItems;

    public static Role Create(string name, string? description, bool isSystemRole = false) =>
        new(SequentialGuid.New(), name.Trim(), description?.Trim(), isSystemRole);

    public void Rename(string name, string? description)
    {
        EnsureMutable();
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Description = description?.Trim();
    }

    public void SetActive(bool isActive)
    {
        if (!isActive)
        {
            EnsureMutable();
        }

        IsActive = isActive;
    }

    /// <summary>
    /// Guards the deletion path. A system role is referenced by seeded permission and menu
    /// grants; removing one would leave the seeded data dangling and lock every admin out.
    /// </summary>
    public void EnsureDeletable()
    {
        if (IsSystemRole)
        {
            throw new DomainRuleException("system_role_immutable", $"The '{Name}' role is a system role and cannot be deleted.");
        }
    }

    public void ReplacePermissions(IEnumerable<Guid> permissionIds)
    {
        ArgumentNullException.ThrowIfNull(permissionIds);

        var target = permissionIds.Distinct().ToArray();

        _permissions.RemoveAll(p => !target.Contains(p.PermissionId));

        foreach (var permissionId in target.Where(id => !_permissions.Exists(p => p.PermissionId == id)))
        {
            _permissions.Add(new RolePermission(Id, permissionId));
        }
    }

    /// <summary>
    /// Replaces menu visibility for this role (spec §8.1 <c>RoleMenuItems</c> - the
    /// "admin grants access to dynamic menus" table).
    /// </summary>
    public void ReplaceMenuItems(IEnumerable<(Guid MenuItemId, bool IsVisible)> menuItems)
    {
        ArgumentNullException.ThrowIfNull(menuItems);

        var target = menuItems.DistinctBy(m => m.MenuItemId).ToArray();
        var targetIds = target.Select(m => m.MenuItemId).ToArray();

        _menuItems.RemoveAll(m => !targetIds.Contains(m.MenuItemId));

        foreach (var (menuItemId, isVisible) in target)
        {
            var existing = _menuItems.Find(m => m.MenuItemId == menuItemId);

            if (existing is null)
            {
                _menuItems.Add(new RoleMenuItem(Id, menuItemId, isVisible));
            }
            else
            {
                existing.SetVisible(isVisible);
            }
        }
    }

    private void EnsureMutable()
    {
        if (IsSystemRole)
        {
            throw new DomainRuleException("system_role_immutable", $"The '{Name}' role is a system role and cannot be modified.");
        }
    }
}
