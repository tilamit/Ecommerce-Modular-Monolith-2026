using System.Globalization;
using System.Text.Json;

namespace ShopHub.Modules.Auditing.Contracts;

/// <summary>
/// The two kinds of audit entry that modules write by hand rather than through change
/// tracking: a change to a list a record holds and a read of a single record.
/// </summary>
public static class AuditWriterExtensions
{
    /// <summary>
    /// Records a change to a list held by a record - the roles of a user, the permissions or
    /// menus of a role, the images of a product - as one entry with the whole list before and
    /// after.
    /// <para>
    /// These lists live in link or child rows, which change tracking does not capture as a
    /// change to the record itself. Recording each row on its own would leave a reader
    /// matching ids by hand, so the entry stores readable values instead. Nothing is written
    /// when the list is the same before and after.
    /// </para>
    /// </summary>
    /// <param name="subjectLabel">Names the record in the stored values, for example "Role".</param>
    /// <param name="subjectValue">The record's readable name, for example "Customer".</param>
    /// <param name="field">The list's name in the stored values, for example "Permissions".</param>
    /// <param name="before">The list before the change, in a stable order.</param>
    /// <param name="after">The list after the change, in the same order.</param>
    public static void WriteListChange(
        this IAuditWriter audit,
        AuditAction action,
        string module,
        string entityName,
        Guid entityId,
        string subjectLabel,
        string subjectValue,
        string field,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.SequenceEqual(after, StringComparer.Ordinal))
        {
            return;
        }

        audit.Write(new AuditEntryDto
        {
            Action = action,
            Module = module,
            EntityName = entityName,
            EntityId = entityId.ToString("D", CultureInfo.InvariantCulture),
            OldValues = SerializeList(subjectLabel, subjectValue, field, before),
            NewValues = SerializeList(subjectLabel, subjectValue, field, after),
            ChangedColumns = JsonSerializer.Serialize(new[] { field }),
        });
    }

    /// <summary>Records that the current user opened a single record.</summary>
    public static void WriteRead(this IAuditWriter audit, string module, string entityName, Guid entityId)
    {
        ArgumentNullException.ThrowIfNull(audit);

        audit.Write(new AuditEntryDto
        {
            Action = AuditAction.Read,
            Module = module,
            EntityName = entityName,
            EntityId = entityId.ToString("D", CultureInfo.InvariantCulture),
        });
    }

    private static string SerializeList(string subjectLabel, string subjectValue, string field, IReadOnlyList<string> items) =>
        JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [subjectLabel] = subjectValue,
            [field] = items,
        });
}
