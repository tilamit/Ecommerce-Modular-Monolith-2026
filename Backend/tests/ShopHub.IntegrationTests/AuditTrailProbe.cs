using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests;

/// <summary>An audit entry as the detail endpoint returns it, with its values parsed.</summary>
internal sealed record AuditEntry(
    long Id,
    string Action,
    string EntityName,
    string? EntityId,
    string? UserId,
    string[] Changed,
    JsonElement? OldValues,
    JsonElement? NewValues)
{
    public string[] OldList(string field) => List(OldValues, field);

    public string[] NewList(string field) => List(NewValues, field);

    private static string[] List(JsonElement? values, string field) =>
        [.. values!.Value.GetProperty(field).EnumerateArray().Select(i => i.GetString()!)];
}

/// <summary>
/// Finds audit entries written by the background writer.
/// <para>
/// The writer batches with a 2-second flush interval, so a test that read the trail straight
/// after acting would race it. Polling rather than sleeping a fixed time keeps the suite fast
/// when the write lands early.
/// </para>
/// </summary>
internal static class AuditTrailProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Waits for the newest entry with this action on this record, written after <paramref name="afterId"/>.</summary>
    internal static async Task<AuditEntry> WaitForAsync(
        HttpClient admin,
        string action,
        string entityName,
        Guid entityId,
        long afterId = 0,
        Func<AuditEntry, bool>? match = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        var id = entityId.ToString();

        while (DateTime.UtcNow < deadline)
        {
            using var response = await admin.GetAsync(new Uri("/api/v1/audit-trails?pageSize=100", UriKind.Relative));

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var page = await response.Content.ReadFromJsonAsync<JsonElement>();

                foreach (var item in page.GetProperty("items").EnumerateArray())
                {
                    if (item.GetProperty("action").GetString() != action
                        || item.GetProperty("entityName").GetString() != entityName
                        || item.GetProperty("entityId").GetString() != id
                        || item.GetProperty("id").GetInt64() <= afterId)
                    {
                        continue;
                    }

                    var entry = await DetailAsync(admin, item.GetProperty("id").GetInt64());

                    if (match is null || match(entry))
                    {
                        return entry;
                    }
                }
            }

            await Task.Delay(250);
        }

        throw new Xunit.Sdk.XunitException($"No {action} entry for {entityName} {entityId} appeared within {Timeout.TotalSeconds} seconds.");
    }

    private static async Task<AuditEntry> DetailAsync(HttpClient admin, long id)
    {
        using var response = await admin.GetAsync(new Uri($"/api/v1/audit-trails/{id}", UriKind.Relative));
        var full = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new AuditEntry(
            id,
            full.GetProperty("action").GetString()!,
            full.GetProperty("entityName").GetString()!,
            full.GetProperty("entityId").GetString(),
            full.TryGetProperty("userId", out var user) && user.ValueKind == JsonValueKind.String ? user.GetString() : null,
            Parse<string[]>(full, "changedColumns") ?? [],
            Parse<JsonElement?>(full, "oldValues"),
            Parse<JsonElement?>(full, "newValues"));
    }

    private static T? Parse<T>(JsonElement full, string property) =>
        full.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? JsonSerializer.Deserialize<T>(value.GetString()!)
            : default;
}
