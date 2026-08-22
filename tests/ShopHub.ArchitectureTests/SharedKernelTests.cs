namespace ShopHub.ArchitectureTests;

/// <summary>
/// Spec §4.4 rule 3. Shared.Kernel is the bottom of the dependency graph.
/// If it ever grows a reference to a module - or even to Shared.Infrastructure -
/// every module inherits that dependency and the graph stops being a graph.
/// </summary>
public sealed class SharedKernelTests
{
    [Fact]
    public void SharedKernel_DependsOnNoShopHubAssembly()
    {
        var referenced = ModuleCatalog.SharedKernel
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("ShopHub", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            "Shared.Kernel must reference nothing but the BCL. Found: " + string.Join(", ", referenced));
    }

    /// <summary>
    /// Shared.Contracts carries integration events across module boundaries, so it must
    /// stay as free of dependencies as the kernel - a consumer references it precisely to
    /// avoid dragging in a producer.
    /// </summary>
    [Fact]
    public void SharedContracts_DependsOnNoModuleAssembly()
    {
        var referenced = ModuleCatalog.SharedContracts
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("ShopHub.Modules", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            "Shared.Contracts must not reference a module. Found: " + string.Join(", ", referenced));
    }

    /// <summary>
    /// Shared.Infrastructure hosts the cross-cutting concerns every module consumes
    /// (spec §6). It must not know about any specific module, or "shared" becomes
    /// a back door between them.
    /// </summary>
    [Fact]
    public void SharedInfrastructure_DependsOnNoModuleAssembly()
    {
        var referenced = ModuleCatalog.SharedInfrastructure
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("ShopHub.Modules", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            "Shared.Infrastructure must not reference a module. Found: " + string.Join(", ", referenced));
    }
}
