namespace ShopHub.Shared.Infrastructure.Errors;

/// <summary>
/// Status codes this application uses that <see cref="Microsoft.AspNetCore.Http.StatusCodes"/>
/// does not name.
/// </summary>
public static class ShopHubStatusCodes
{
    /// <summary>
    /// Nginx's 499, used when the client disconnects before the response is written.
    /// <para>
    /// Nothing ever reads it - the socket is closed by the time it is set - but it keeps
    /// the access log honest: a request nobody was waiting for is not a server error, and
    /// recording it as one is how a normally-browsing shopper ends up looking like an
    /// incident.
    /// </para>
    /// </summary>
    public const int ClientClosedRequest = 499;
}
