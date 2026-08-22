using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ShopHub.ArchitectureTests;

/// <summary>
/// Spec §4.4 rule 5, asserted against the EF model metadata at runtime rather than
/// against the configuration source - what matters is the schema EF actually resolved,
/// not the schema someone meant to configure.
/// <para>
/// Schema-per-module (spec §4.2) is what makes "move this module to its own database"
/// a migration rather than an archaeology project.
/// </para>
/// </summary>
public sealed class SchemaMappingTests
{
    /// <summary>
    /// Every entity a module's DbContext maps must land in that module's own schema.
    /// <para>
    /// No DbContext exists before Phase 2, so this passes vacuously in Phase 0 and starts
    /// biting the moment the first context appears - which is exactly when it is needed.
    /// <see cref="ModuleDbContexts_AreDiscoverable"/> guards against it staying vacuous
    /// by accident once contexts exist.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void EveryEntity_MapsToItsOwnModuleSchema(string module)
    {
        var expectedSchema = module.ToLowerInvariant();
        var violations = new List<string>();

        foreach (var context in CreateModuleDbContexts(module))
        {
            using (context)
            {
                foreach (var entityType in context.Model.GetEntityTypes())
                {
                    var schema = entityType.GetSchema();

                    if (!string.Equals(schema, expectedSchema, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{entityType.ClrType.Name} -> schema '{schema ?? "(default)"}', expected '{expectedSchema}'");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{module} maps entities outside its own schema:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Once a module owns a DbContext, this test proves the schema assertion above is
    /// actually running against something. It is skipped for modules that have no context
    /// yet, so it never blocks an earlier phase, and reports loudly if a context exists but
    /// cannot be constructed for inspection.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleCatalog.ModuleNames), MemberType = typeof(ModuleCatalog))]
    public void ModuleDbContexts_AreDiscoverable(string module)
    {
        var declared = ModuleCatalog.Implementations[module]
            .GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        if (declared.Length == 0)
        {
            return;
        }

        var constructed = CreateModuleDbContexts(module).ToArray();

        try
        {
            Assert.Equal(declared.Length, constructed.Length);
        }
        finally
        {
            foreach (var context in constructed)
            {
                context.Dispose();
            }
        }
    }

    /// <summary>
    /// Builds each of a module's DbContexts far enough to read its model. EF resolves the
    /// model without opening a connection, so the connection string here is never dialled.
    /// </summary>
    private static IEnumerable<DbContext> CreateModuleDbContexts(string module)
    {
        const string UnusedConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=ShopHub_ArchTests";

        var contextTypes = ModuleCatalog.Implementations[module]
            .GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var contextType in contextTypes)
        {
            var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
            var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
            var builder = Activator.CreateInstance(builderType)!;

            typeof(SqlServerDbContextOptionsExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(SqlServerDbContextOptionsExtensions.UseSqlServer)
                            && m.GetParameters().Length == 3
                            && m.GetParameters()[1].ParameterType == typeof(string)
                            && !m.IsGenericMethod)
                .Invoke(null, [builder, UnusedConnectionString, null]);

            // DeclaredOnly: DbContextOptionsBuilder<TContext> hides the base class's
            // Options property with a more-derived one, so an unqualified lookup finds
            // both and throws AmbiguousMatchException.
            var options = builderType
                .GetProperty(
                    nameof(DbContextOptionsBuilder.Options),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                .GetValue(builder);

            var constructor = contextType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                [optionsType],
                modifiers: null)
                ?? throw new InvalidOperationException(
                    $"{contextType.Name} needs a constructor taking DbContextOptions<{contextType.Name}> " +
                    "so the architecture tests can inspect its model.");

            yield return (DbContext)constructor.Invoke([options]);
        }
    }
}
