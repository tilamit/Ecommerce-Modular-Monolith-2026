using Microsoft.AspNetCore.Http;

namespace ShopHub.Shared.Infrastructure.Auditing;

/// <summary>
/// Decides whether opening a single record is written to the audit trail.
/// </summary>
public static class ReadAudit
{
    /// <summary>
    /// True for a signed-in caller whose request is still being answered.
    /// <para>
    /// Anonymous reads are not recorded: there is no user to attribute them to. A request the
    /// browser has already abandoned is not recorded either, because nobody saw the record.
    /// React's development double-mount cancels the first request of every page, which would
    /// otherwise put two reads in the trail for every record opened.
    /// </para>
    /// </summary>
    public static bool ShouldRecordRead(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return http.User.Identity?.IsAuthenticated == true && !http.RequestAborted.IsCancellationRequested;
    }
}
