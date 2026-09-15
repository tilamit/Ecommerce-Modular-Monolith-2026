namespace ShopHub.Shared.Infrastructure.Modules;

/// <summary>
/// A module's startup database work: apply its own migrations, then seed (spec §13).
/// <para>
/// Each module owns <em>what</em> to migrate and seed; the host decides <em>whether</em>
/// that happens at all - it only runs in Development. Keeping the interface here rather
/// than in the API project means a module never has to reference the host.
/// </para>
/// </summary>
public interface IModuleInitializer
{
    string ModuleName { get; }

    Task InitializeAsync(CancellationToken cancellationToken);
}
