using Microsoft.EntityFrameworkCore;

namespace ShopHub.ArchitectureTests;

/// <summary>
/// Spec §4.4 rules 4 and 6, plus the §5.1 statement that only <c>*.Contracts</c> types
/// are public. This is the compiler doing boundary enforcement: a type another module
/// cannot see is a type another module cannot couple to.
/// </summary>
public sealed class VisibilityTests
{
    /// <summary>Rule 4. A public DbContext is an open invitation to a cross-module query.</summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void DbContexts_AreNotPublic(string module)
    {
        var publicContexts = ModuleCatalog.Implementations[module]
            .GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && t.IsPublic)
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        Assert.True(
            publicContexts.Length == 0,
            $"{module} exposes a public DbContext: {string.Join(", ", publicContexts)}");
    }

    /// <summary>Rule 6. Endpoints are wired up by the module itself and by nothing else.</summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void Endpoints_AreInternal(string module)
    {
        var publicEndpoints = ModuleCatalog.Implementations[module]
            .GetTypes()
            .Where(t => t.Name.EndsWith("Endpoint", StringComparison.Ordinal) && t.IsPublic)
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        Assert.True(
            publicEndpoints.Length == 0,
            $"{module} exposes a public endpoint class: {string.Join(", ", publicEndpoints)}");
    }

    /// <summary>
    /// The general form of the rule: a module's public surface is its IModule
    /// implementation, full stop. Handlers, entities and DTOs stay internal - the
    /// Contracts assembly is where a module publishes anything others may use.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void OnlyTheModuleRegistrationType_IsPublic(string module)
    {
        var exported = ModuleCatalog.AuthoredPublicTypes(module)
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        var expected = $"{ModuleCatalog.ImplementationNamespace(module)}.{module}Module";

        Assert.Equal([expected], exported);
    }
}
