using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Redux;
using RonSijm.Syringe;
using RonSijm.Syringe.DependencyInjection;

namespace RonSijm.Demo.Fluxor.WonderWharf.Publications.Services;

public sealed class WharfRuntimeExtensions(SyringeServiceProvider provider, IDispatcher dispatcher)
{
    public async Task RegisterPublicationTrackingAsync(bool simulateMissingDependency = false)
    {
        var services = new ServiceCollection();
        if (!simulateMissingDependency)
        {
            services.AddSingleton<IWharfPublicationCounter, WharfPublicationCounter>();
        }
        services.AddFluxorLibrary(options => options.ScanAssemblies<WharfRuntimeExtensions>());
        await provider.LoadServiceDescriptors(services);
        provider.Build();
        dispatcher.Dispatch(new WharfPublicationTrackingEnabled());
    }
}

public interface IWharfPublicationCounter
{
    int Calls { get; }
    int Record();
}

public sealed class WharfPublicationCounter() : IWharfPublicationCounter
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public int Record() => Interlocked.Increment(ref _calls);
}
