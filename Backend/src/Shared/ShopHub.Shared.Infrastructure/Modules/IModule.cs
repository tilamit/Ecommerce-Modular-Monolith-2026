using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopHub.Shared.Infrastructure.Modules;

/// <summary>
/// The registration contract every module implements (spec §5.2).
/// <para>
/// Adding module #5 must cost exactly one line in <c>Program.cs</c> and nothing else -
/// see <c>docs/MODULE_TEMPLATE.md</c>. Modules are discovered from a hard-coded list
/// rather than by assembly scanning: explicit is debuggable and keeps startup fast.
/// </para>
/// </summary>
public interface IModule
{
    /// <summary>Module name, also used as the SQL schema and the audit trail's Module column.</summary>
    string Name { get; }

    /// <summary>Registers the module's services. Called once at startup, before the app is built.</summary>
    IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints. Called once after the app is built.</summary>
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints);
}
