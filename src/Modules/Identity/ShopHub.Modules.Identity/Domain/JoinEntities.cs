using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Modules.Identity.Domain;

/// <summary>
/// Many-to-many from day one, even though a user has one role today (spec §8.1) -
/// changing this later is a migration nobody wants.
/// </summary>
internal sealed class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;
}

/// <summary>A capability a role can grant (spec §8.1).</summary>
internal sealed class Permission : Entity
{
    private Permission()
    {
    }

    private Permission(Guid id, string code, string displayName, string group, string? description)
        : base(id)
    {
        Code = code;
        DisplayName = displayName;
        Group = group;
        Description = description;
    }

    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Grouping for the admin UI, e.g. "Catalog".</summary>
    public string Group { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public static Permission Create(string code, string displayName, string group, string? description) =>
        new(SequentialGuid.New(), code, displayName, group, description);

    public void Update(string displayName, string group, string? description)
    {
        DisplayName = displayName;
        Group = group;
        Description = description;
    }
}

internal sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    public Permission Permission { get; private set; } = null!;
}

/// <summary>
/// A node in the dynamic sidebar (spec §8.1). Self-referencing for nested menus; the SPA
/// renders the tree returned by <c>/auth/me</c> rather than a hard-coded array.
/// </summary>
internal sealed class MenuItem : AuditableEntity
{
    private MenuItem()
    {
    }

    private MenuItem(Guid id, string title, string? icon, string? route, Guid? parentId, Guid? requiredPermissionId, int displayOrder)
        : base(id)
    {
        Title = title;
        Icon = icon;
        Route = route;
        ParentId = parentId;
        RequiredPermissionId = requiredPermissionId;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public Guid? ParentId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Icon { get; private set; }

    public string? Route { get; private set; }

    /// <summary>
    /// Optional gate. Client-side hiding is UX, not security - the endpoint behind the
    /// route enforces its own permission regardless (spec §11.2).
    /// </summary>
    public Guid? RequiredPermissionId { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public Permission? RequiredPermission { get; private set; }

    public static MenuItem Create(
        string title,
        string? icon,
        string? route,
        Guid? parentId,
        Guid? requiredPermissionId,
        int displayOrder) =>
        new(SequentialGuid.New(), title, icon, route, parentId, requiredPermissionId, displayOrder);

    public void Update(string title, string? icon, string? route, int displayOrder, bool isActive)
    {
        Title = title;
        Icon = icon;
        Route = route;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}

/// <summary>Menu visibility granted to a role (spec §8.1).</summary>
internal sealed class RoleMenuItem
{
    private RoleMenuItem()
    {
    }

    internal RoleMenuItem(Guid roleId, Guid menuItemId, bool isVisible)
    {
        RoleId = roleId;
        MenuItemId = menuItemId;
        IsVisible = isVisible;
    }

    public Guid RoleId { get; private set; }

    public Guid MenuItemId { get; private set; }

    public bool IsVisible { get; private set; }

    public MenuItem MenuItem { get; private set; } = null!;

    internal void SetVisible(bool isVisible) => IsVisible = isVisible;
}

/// <summary>An entry in the user's address book (spec §8.1).</summary>
internal sealed class UserAddress : AuditableEntity
{
    private UserAddress()
    {
    }

    private UserAddress(Guid id, Guid userId, string label, string line1, string? line2, string city, string? state, string postalCode, string country, bool isDefault)
        : base(id)
    {
        UserId = userId;
        Label = label;
        Line1 = line1;
        Line2 = line2;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        IsDefault = isDefault;
    }

    public Guid UserId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string? State { get; private set; }

    public string PostalCode { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public static UserAddress Create(
        Guid userId,
        string label,
        string line1,
        string? line2,
        string city,
        string? state,
        string postalCode,
        string country,
        bool isDefault) =>
        new(SequentialGuid.New(), userId, label, line1, line2, city, state, postalCode, country, isDefault);

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
