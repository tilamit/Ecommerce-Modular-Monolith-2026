namespace ShopHub.Modules.Auditing.Contracts;

/// <summary>
/// What happened (spec §6.6). Insert/Update/Delete come from the EF interceptor; the rest
/// are non-EF events that no interceptor could observe.
/// </summary>
public enum AuditAction
{
    Insert = 1,
    Update = 2,
    Delete = 3,
    Login = 4,
    LoginFailed = 5,
    Logout = 6,
    Export = 7,
    PermissionChange = 8,

    /// <summary>A signed-in user opened a single record, such as a product or their profile.</summary>
    Read = 9,
}

/// <summary>
/// Writes audit entries for things the EF interceptor cannot see (spec §9.4): a successful
/// login, a failed one, a logout, an export, a permission change.
/// <para>
/// Also the escape hatch for bulk operations. <c>ExecuteUpdateAsync</c> and
/// <c>ExecuteDeleteAsync</c> bypass the change tracker and therefore the interceptor
/// (spec §6.7), so any caller using them must write an entry through here explicitly.
/// </para>
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Queues an entry. Returns as soon as it is queued, not when it is persisted -
    /// auditing must never sit on the request's critical path (spec §6.6).
    /// </summary>
    void Write(AuditEntryDto entry);
}

/// <summary>
/// One audit entry. Everything the caller knows; the writer fills in what it can infer
/// from the request context.
/// </summary>
public sealed record AuditEntryDto
{
    public required AuditAction Action { get; init; }

    /// <summary>Owning module, matching the SQL schema name.</summary>
    public required string Module { get; init; }

    /// <summary>CLR type name of the entity, or a label for non-entity events such as "Auth".</summary>
    public required string EntityName { get; init; }

    /// <summary>Primary key as a string, or null where the event has no single subject.</summary>
    public string? EntityId { get; init; }

    /// <summary>JSON of changed properties only, never the whole row.</summary>
    public string? OldValues { get; init; }

    public string? NewValues { get; init; }

    /// <summary>JSON array of the property names that changed.</summary>
    public string? ChangedColumns { get; init; }

    /// <summary>
    /// Overrides the acting user. Normally left null so the writer takes it from
    /// <c>ICurrentUser</c> - a failed login is the exception, because there is no
    /// authenticated user but the attempted identity is still worth recording.
    /// </summary>
    public Guid? UserId { get; init; }

    public string? UserName { get; init; }
}

/// <summary>
/// Read side of the audit trail, for the admin dashboard tile (spec §10.1).
/// <para>
/// Separate from <see cref="IAuditWriter"/> on purpose: writing is a fire-and-forget
/// capability almost every module needs, reading is an administrative query only the
/// dashboard and the audit screen use.
/// </para>
/// </summary>
public interface IAuditModuleApi
{
    /// <summary>
    /// The most recent entries. Bounded - the tile shows page one and the audit screen
    /// pages independently from there (spec §10.1).
    /// </summary>
    Task<IReadOnlyList<RecentAuditDto>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}

public sealed record RecentAuditDto(
    long Id,
    DateTime OccurredUtc,
    string Action,
    string Module,
    string EntityName,
    string? UserName,
    string? ScreenName);
