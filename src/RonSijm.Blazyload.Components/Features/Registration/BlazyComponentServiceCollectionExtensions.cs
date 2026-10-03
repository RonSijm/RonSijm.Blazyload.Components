using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RonSijm.Blazyload.Components;

public static class BlazyComponentServiceCollectionExtensions
{
    public static IServiceCollection AddBlazyloadComponents(this IServiceCollection services, Action<BlazyComponentOptions>? configure = null)
    {
        var options = new BlazyComponentOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IBlazyLogger, ConsoleLogger>();
        services.TryAddScoped<HttpClient>();
        services.TryAddScoped<IBlazyComponentManifestProvider, BlazyComponentManifestProvider>();
        services.TryAddScoped<IBlazyComponentRegistry, BlazyComponentRegistry>();

        return services;
    }
}
