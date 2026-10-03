using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.WonderWharf.Services;
using RonSijm.Syringe;

namespace RonSijm.Demo.WonderWharf.Properties;

public sealed class BlazyBootstrap : IBootstrapper
{
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WharfEventService>();
        return Task.FromResult<IEnumerable<ServiceDescriptor>>(services);
    }
}
