using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Fluxor.BobsBurgers.Redux;
using RonSijm.Demo.Fluxor.BobsBurgers.Services;
using RonSijm.Syringe;
using RonSijm.Syringe.DependencyInjection;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Properties;

public sealed class BlazyBootstrap : IBootstrapper
{
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DemoBrowserInterop>();
        services.AddFluxorLibrary(options =>
        {
            options.ScanAssemblies<BlazyBootstrap>();
            options.AddMiddleware<DemoTraceMiddleware>();
        });
        return Task.FromResult<IEnumerable<ServiceDescriptor>>(services);
    }
}
