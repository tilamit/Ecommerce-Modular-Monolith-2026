namespace ShopHub.Shared.Kernel.Entities;

/// <summary>
/// Row-level provenance columns required on every table by spec §8.
/// Distinct from the audit *trail* (spec §6.6) - this is "who touched this row last",
/// the trail is "what changed, when, from which screen".
/// </summary>
public interface IAuditableColumns
{
    DateTime CreatedUtc { get; set; }

    string? CreatedBy { get; set; }

    DateTime? ModifiedUtc { get; set; }

    string? ModifiedBy { get; set; }
}

/// <summary>
/// Entities that are hidden rather than removed (spec A7). A global EF query filter
/// excludes these; the filter is opted out of explicitly where an admin needs to see them.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedUtc { get; set; }
}

/// <summary>
/// Marker: changes to this entity are written to the audit trail by the
/// SaveChangesInterceptor (spec §6.6). Opt in per entity - auditing everything is noise.
/// </summary>
public interface IAuditableEntity;

/// <summary>
/// Excludes a property from Old/New values in the audit trail (spec §6.6 redaction).
/// Applied to PasswordHash, TokenHash and anything else that must never be persisted twice.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NoAuditAttribute : Attribute;

/// <summary>
/// Convenience base for the common case: a Guid-keyed entity with provenance columns.
/// </summary>
public abstract class AuditableEntity : Entity, IAuditableColumns
{
    protected AuditableEntity(Guid id)
        : base(id)
    {
    }

    protected AuditableEntity()
    {
    }

    public DateTime CreatedUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
