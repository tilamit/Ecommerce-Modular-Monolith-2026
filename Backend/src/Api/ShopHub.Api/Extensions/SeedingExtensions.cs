using ShopHub.Shared.Infrastructure.Modules;

namespace ShopHub.Api.Extensions;

/// <summary>
/// Applies each module's migrations and seed data at startup, Development only (spec §13).
/// </summary>
internal static class SeedingExtensions
{
    /// <summary>
    /// Runs every registered <see cref="IModuleInitializer"/>.
    /// <para>
    /// Deliberately not wrapped in a try/catch: a failed migration or a missing seed
    /// password should stop the host with a clear error, not leave it serving traffic
    /// against a half-built database (spec §13).
    /// </para>
    /// </summary>
    internal static async Task InitializeModulesAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        foreach (var initializer in scope.ServiceProvider.GetServices<IModuleInitializer>())
        {
            if (app.Logger.IsEnabled(LogLevel.Information))
            {
                app.Logger.LogInformation("Initializing module store: {Module}.", initializer.ModuleName);
            }

            await initializer.InitializeAsync(cancellationToken);
        }
    }
}
