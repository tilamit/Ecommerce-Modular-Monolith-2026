using NetArchTest.Rules;

namespace ShopHub.ArchitectureTests;

/// <summary>
/// Spec §4.4 rules 1, 2 and 7 - the reference rules that separate a modular monolith
/// from a layered monolith with folders named "Modules".
/// </summary>
/// <remarks>
/// These assert against the <em>assembly reference table</em> rather than against
/// namespace strings. That is deliberate. A module's Contracts live in a separate
/// assembly, so "does Catalog reference Ordering's implementation" has an exact answer in
/// metadata, while a namespace check has to distinguish
/// <c>ShopHub.Modules.Ordering</c> from <c>ShopHub.Modules.Ordering.Contracts</c> by
/// string prefix - and gets it wrong in both directions. The compiler cannot resolve a
/// type without an assembly reference, so this oracle catches a violation anywhere in the
/// assembly, including inside a method body where a signature-only scan would miss it.
/// </remarks>
public sealed class ModuleBoundaryTests
{
    /// <summary>
    /// Rule 1. A module must not depend on another module's <em>implementation</em>.
    /// Runs for all 12 ordered pairs, so the symmetric cases are covered without
    /// hand-writing them.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModulePairs), MemberType = typeof(ModuleCatalog))]
    public void Module_DoesNotDependOn_AnotherModulesImplementation(string source, string target)
    {
        var sourceAssembly = ModuleCatalog.Implementations[source];
        var forbiddenAssembly = ModuleCatalog.ImplementationNamespace(target);

        var referencesForbiddenAssembly = sourceAssembly
            .GetReferencedAssemblies()
            .Any(a => string.Equals(a.Name, forbiddenAssembly, StringComparison.Ordinal));

        Assert.False(
            referencesForbiddenAssembly,
            $"{source} references {target}'s implementation assembly ({forbiddenAssembly}). " +
            $"Cross-module calls go through {ModuleCatalog.ContractsNamespace(target)}. " +
            $"Offending types: {DescribeOffendingTypes(source, target)}");
    }

    /// <summary>
    /// Rule 2. Cross-module dependencies are allowed, but only onto <c>*.Contracts</c>.
    /// This is the positive statement of rule 1: it keeps the legal communication path
    /// legal, so a future tightening of rule 1 cannot quietly ban module communication
    /// altogether.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void Module_ReachesOtherModules_OnlyThroughContracts(string module)
    {
        var referenced = ModuleCatalog.Implementations[module]
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("ShopHub.Modules.", StringComparison.Ordinal))
            .ToArray();

        var illegal = referenced
            .Where(name => !name.EndsWith(".Contracts", StringComparison.Ordinal))
            .Where(name => !string.Equals(name, ModuleCatalog.ImplementationNamespace(module), StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            illegal.Length == 0,
            $"{module} references another module's implementation assembly: {string.Join(", ", illegal)}. " +
            "Only *.Contracts assemblies may cross a module boundary.");
    }

    /// <summary>
    /// Rule 7. Types named <c>*Controller</c> or <c>*Repository</c> get the same treatment
    /// as everything else - the naming convention earns no exemption from the boundary.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModulePairs), MemberType = typeof(ModuleCatalog))]
    public void ControllersAndRepositories_DoNotReach_AcrossModules(string source, string target)
    {
        var suspects = Types.InAssembly(ModuleCatalog.Implementations[source])
            .That()
            .HaveNameEndingWith("Controller")
            .Or()
            .HaveNameEndingWith("Repository")
            .GetTypes()
            .ToArray();

        if (suspects.Length == 0)
        {
            // Vertical slices (spec §5.1) mean these names should not appear at all.
            return;
        }

        var offenders = suspects
            .Where(t => TypeDependsOnModule(t, target))
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{source} controllers/repositories reach into {target}: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The API host is the composition root: spec §4.1 says it references every module
    /// "for registration only". Module internals are already invisible to it because they
    /// are <c>internal</c>; this asserts each module's public surface stays exactly one
    /// type - its <c>IModule</c> implementation - so "registration only" stays true by
    /// construction rather than by convention.
    /// </summary>
    [Fact]
    public void Api_SeesOnlyPublicModuleRegistrationSurface()
    {
        var referenced = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        foreach (var module in ModuleCatalog.Names)
        {
            Assert.Contains(ModuleCatalog.ImplementationNamespace(module), referenced, StringComparer.Ordinal);

            var publicTypes = ModuleCatalog.AuthoredPublicTypes(module)
                .Select(t => t.FullName)
                .ToArray();

            Assert.True(
                publicTypes.Length == 1,
                $"{module} should expose exactly one public type (its IModule implementation). Found: " +
                string.Join(", ", publicTypes));
        }
    }

    /// <summary>
    /// Names the types responsible for a boundary violation, so a failure points at the
    /// code to fix rather than only at the assembly. Diagnostic only - the assertion above
    /// has already decided the verdict.
    /// </summary>
    private static string DescribeOffendingTypes(string source, string target)
    {
        var offenders = Types.InAssembly(ModuleCatalog.Implementations[source])
            .That()
            .HaveDependencyOn(ModuleCatalog.ImplementationNamespace(target))
            .GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        return offenders.Length == 0 ? "(none identified by name)" : string.Join(", ", offenders);
    }

    private static bool TypeDependsOnModule(Type type, string module) =>
        Types.InAssembly(type.Assembly)
            .That()
            .HaveName(type.Name)
            .And()
            .HaveDependencyOn(ModuleCatalog.ImplementationNamespace(module))
            .GetTypes()
            .Any();
}
