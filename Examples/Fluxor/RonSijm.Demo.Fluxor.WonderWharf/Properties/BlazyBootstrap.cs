using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Fluxor.WonderWharf.Redux;
using RonSijm.Demo.Fluxor.WonderWharf.Services;
using RonSijm.Syringe;
using RonSijm.Syringe.DependencyInjection;

namespace RonSijm.Demo.Fluxor.WonderWharf.Properties;

public sealed class BlazyBootstrap : IBootstrapper
{
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WharfEventService>();
        services.AddFluxorLibrary(options =>
        {
            options.ScanAssemblies<BlazyBootstrap>();
            options.AddMiddleware<WharfTraceMiddleware>();
        });
        return Task.FromResult<IEnumerable<ServiceDescriptor>>(services);
    }
}
