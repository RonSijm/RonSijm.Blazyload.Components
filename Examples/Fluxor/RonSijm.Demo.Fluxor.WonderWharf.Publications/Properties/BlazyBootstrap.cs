using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Services;
using RonSijm.Syringe;

namespace RonSijm.Demo.Fluxor.WonderWharf.Publications.Properties;

public sealed class BlazyBootstrap : IBootstrapper
{
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap()
    {
        var services = new ServiceCollection();
        // Normally register the dependencies and call AddFluxorLibrary(options => options.ScanAssemblies<BlazyBootstrap>()) here.
        // This demo defers that scan to WharfRuntimeExtensions so its controls can show a missing dependency,
        // corrected retry and duplicate coalescing against the already-running feature.
        services.AddScoped<WharfRuntimeExtensions>();
        return Task.FromResult<IEnumerable<ServiceDescriptor>>(services);
    }
}
