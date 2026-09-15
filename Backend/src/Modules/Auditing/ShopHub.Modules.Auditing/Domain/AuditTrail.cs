using ShopHub.Modules.Auditing.Contracts;

namespace ShopHub.Modules.Auditing.Domain;

/// <summary>
/// One row of the append-only audit trail (spec §6.6, §9.4).
/// </summary>
/// <remarks>
/// <para>
/// Keyed by <c>bigint identity</c>, not a Guid (spec A5): this is a high-volume append-only
/// table and a monotonic identity keeps inserts at the end of the clustered index.
/// </para>
/// <para>
/// Append-only means there is no mutator on this type, no update endpoint and no admin
/// override.
/// </para>
/// </remarks>
internal sealed class AuditTrail
{
    private AuditTrail()
    {
    }

    private AuditTrail(
        DateTime occurredUtc,
        AuditAction action,
        string module,
        string entityName,
        string? entityId)
    {
        OccurredUtc = occurredUtc;
        Action = action;
        Module = module;
        EntityName = entityName;
        EntityId = entityId;
    }

    /// <summary>Store-generated identity. The one place in the solution EF assigns the key.</summary>
    public long Id { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    public AuditAction Action { get; private set; }

    public string Module { get; private set; } = string.Empty;

    public string EntityName { get; private set; } = string.Empty;

    public string? EntityId { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>
    /// Snapshotted, not looked up. The trail has to survive the user being renamed or
    /// deleted (spec §6.6) - a join to <c>identity.Users</c> would also be a cross-schema
    /// join, which is forbidden anyway.
    /// </summary>
    public string? UserName { get; private set; }

    public string? UserRoles { get; private set; }

    /// <summary>
    /// The SPA route that issued the request, from <c>X-Client-Page</c> (spec §6.6).
    /// Cannot be inferred from the API route: the same endpoint is called from several
    /// screens.
    /// </summary>
    public string? ScreenName { get; private set; }

    public string? HttpMethod { get; private set; }

    public string? Path { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>JSON of changed properties only - never the whole row, never a redacted property.</summary>
    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public string? ChangedColumns { get; private set; }

    internal static AuditTrail Create(
        DateTime occurredUtc,
        AuditAction action,
        string module,
        string entityName,
        string? entityId,
        Guid? userId,
        string? userName,
        string? userRoles,
        string? screenName,
        string? httpMethod,
        string? path,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        string? oldValues,
        string? newValues,
        string? changedColumns) =>
        new(occurredUtc, action, module, entityName, entityId)
        {
            UserId = userId,
            UserName = userName,
            UserRoles = userRoles,
            ScreenName = screenName,
            HttpMethod = httpMethod,
            Path = path,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CorrelationId = correlationId,
            OldValues = oldValues,
            NewValues = newValues,
            ChangedColumns = changedColumns,
        };
}
