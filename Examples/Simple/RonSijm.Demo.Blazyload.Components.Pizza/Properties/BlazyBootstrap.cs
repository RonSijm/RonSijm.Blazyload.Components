using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Blazyload.Components.Pizza.Services;
using RonSijm.Syringe;

namespace RonSijm.Demo.Blazyload.Components.Pizza.Properties;

public sealed class BlazyBootstrap : IBootstrapper
{
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<PizzaGreeting>();
        return Task.FromResult<IEnumerable<ServiceDescriptor>>(services);
    }
}
