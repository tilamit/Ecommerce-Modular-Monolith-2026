using System.Reflection;

namespace ShopHub.ArchitectureTests;

/// <summary>
/// The set of assemblies under test, in one place so a new module is added to the
/// architecture suite by editing a single array (spec §5.2: adding module #5 must be cheap).
/// </summary>
public static class ModuleCatalog
{
    /// <summary>Module names, matching the SQL schema per spec §4.2.</summary>
    public static readonly string[] Names = ["Identity", "Catalog", "Ordering", "Auditing"];

    public static readonly Assembly SharedKernel = typeof(Shared.Kernel.Results.Result).Assembly;

    public static readonly Assembly SharedContracts = typeof(Shared.Contracts.Events.IIntegrationEvent).Assembly;

    public static readonly Assembly SharedInfrastructure = typeof(Shared.Infrastructure.Modules.IModule).Assembly;

    public static readonly Assembly Identity = typeof(Modules.Identity.IdentityModule).Assembly;

    public static readonly Assembly Catalog = typeof(Modules.Catalog.CatalogModule).Assembly;

    public static readonly Assembly Ordering = typeof(Modules.Ordering.OrderingModule).Assembly;

    public static readonly Assembly Auditing = typeof(Modules.Auditing.AuditingModule).Assembly;

    /// <summary>Every module implementation assembly, keyed by module name.</summary>
    public static IReadOnlyDictionary<string, Assembly> Implementations { get; } =
        new Dictionary<string, Assembly>(StringComparer.Ordinal)
        {
            ["Identity"] = Identity,
            ["Catalog"] = Catalog,
            ["Ordering"] = Ordering,
            ["Auditing"] = Auditing,
        };

    /// <summary>Root namespace of a module's implementation assembly.</summary>
    public static string ImplementationNamespace(string module) => $"ShopHub.Modules.{module}";

    /// <summary>Root namespace of a module's Contracts assembly - the only surface others may touch.</summary>
    public static string ContractsNamespace(string module) => $"ShopHub.Modules.{module}.Contracts";

    /// <summary>
    /// Public types a module is allowed to expose beyond its IModule implementation.
    /// <para>
    /// EF Core generates migration classes as <c>public</c> and regenerates them on every
    /// <c>migrations add</c>, so hand-editing them to <c>internal</c> would not survive.
    /// They are exempt because they carry no module API: a migration exposes schema
    /// operations to the EF tooling, not behaviour another module could couple to.
    /// </para>
    /// </summary>
    public static bool IsGeneratedMigrationType(Type type) =>
        type.Namespace?.Contains(".Persistence.Migrations", StringComparison.Ordinal) == true;

    /// <summary>A module's authored public types - what the boundary rules actually care about.</summary>
    public static IReadOnlyList<Type> AuthoredPublicTypes(string module) =>
        [.. Implementations[module].GetExportedTypes().Where(t => !IsGeneratedMigrationType(t))];

    /// <summary>
    /// Every ordered pair of distinct modules. Drives the symmetric isolation cases
    /// required by spec §4.4 rule 1 without hand-writing twelve near-identical tests.
    /// </summary>
    public static TheoryData<string, string> ModulePairs()
    {
        var data = new TheoryData<string, string>();

        foreach (var source in Names)
        {
            foreach (var target in Names.Where(t => !string.Equals(t, source, StringComparison.Ordinal)))
            {
                data.Add(source, target);
            }
        }

        return data;
    }

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();

        foreach (var name in Names)
        {
            data.Add(name);
        }

        return data;
    }
}
