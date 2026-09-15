using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in (spec §8.1). Behaviour lives here rather than in handlers, so
/// the lockout and activation rules hold regardless of which caller reaches them.
/// </summary>
internal sealed class User : AuditableEntity, ISoftDeletable, IAuditableEntity
{
    private readonly List<UserRole> _roles = [];
    private readonly List<UserAddress> _addresses = [];

    private User()
    {
    }

    private User(Guid id, string email, string passwordHash, string firstName, string lastName, string? phoneNumber)
        : base(id)
    {
        Email = email;
        NormalizedEmail = Normalize(email);
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        IsActive = true;
    }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Upper-cased email, uniquely indexed. Lookups go through this, never through <see cref="Email"/>.</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>Never written to the audit trail (spec §6.6 redaction).</summary>
    [NoAudit]
    public string PasswordHash { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? PhoneNumber { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsEmailConfirmed { get; private set; }

    public DateTime? LastLoginUtc { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    /// <summary>Optimistic concurrency token (spec §6.7).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<UserRole> Roles => _roles;

    public IReadOnlyCollection<UserAddress> Addresses => _addresses;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public static User Create(string email, string passwordHash, string firstName, string lastName, string? phoneNumber = null) =>
        new(SequentialGuid.New(), email.Trim(), passwordHash, firstName.Trim(), lastName.Trim(), phoneNumber?.Trim());

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber?.Trim();
    }

    public void ChangeEmail(string email)
    {
        Email = email.Trim();
        NormalizedEmail = Normalize(email);
        IsEmailConfirmed = false;
    }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void ConfirmEmail() => IsEmailConfirmed = true;

    /// <summary>
    /// True while a lockout window is open. Checked before password verification so a
    /// locked account cannot be probed for a correct password.
    /// </summary>
    public bool IsLockedOut(DateTime nowUtc) => LockoutEndUtc is not null && LockoutEndUtc > nowUtc;

    /// <summary>
    /// Records a failed sign-in and locks the account once the threshold is reached
    /// (spec §7.3: 5 consecutive failures, 15 minutes).
    /// </summary>
    public void RegisterFailedLogin(DateTime nowUtc, int maxAttempts, int lockoutMinutes)
    {
        AccessFailedCount++;

        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEndUtc = nowUtc.AddMinutes(lockoutMinutes);
            AccessFailedCount = 0;
        }
    }

    /// <summary>Clears the failure counter. A successful sign-in resets the streak.</summary>
    public void RegisterSuccessfulLogin(DateTime nowUtc)
    {
        AccessFailedCount = 0;
        LockoutEndUtc = null;
        LastLoginUtc = nowUtc;
    }

    public void AssignRole(Guid roleId)
    {
        if (_roles.Exists(r => r.RoleId == roleId))
        {
            return;
        }

        _roles.Add(new UserRole(Id, roleId));
    }

    public void ReplaceRoles(IEnumerable<Guid> roleIds)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        var target = roleIds.Distinct().ToArray();

        if (target.Length == 0)
        {
            throw new DomainRuleException("user_requires_role", "A user must have at least one role.");
        }

        _roles.RemoveAll(r => !target.Contains(r.RoleId));

        foreach (var roleId in target.Where(id => !_roles.Exists(r => r.RoleId == id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }
}
