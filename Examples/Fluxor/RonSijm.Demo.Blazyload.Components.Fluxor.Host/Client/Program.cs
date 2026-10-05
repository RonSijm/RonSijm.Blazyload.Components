using Fluxor.Blazor.Web.ReduxDevTools;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RonSijm.Blazyload;
using RonSijm.Blazyload.Components;
using RonSijm.Demo.Fluxor.Diagnostics;
using RonSijm.Demo.Fluxor.WonderWharf.Models;

namespace RonSijm.Demo.Blazyload.Components.Fluxor.Host.Client;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.UseBlazyload(options =>
        {
            options.LoadOnNavigation(WonderWharfFeature.LoadingPath, $"{WonderWharfFeature.AssemblyName}.wasm");
            options.UseFluxor(fluxor =>
            {
                fluxor.WithLifetime(global::Fluxor.StoreLifetime.Singleton);
                fluxor.ScanAssemblies(typeof(Program).Assembly);
#if DEBUG
                fluxor.AddNativeExtension(native => native.UseReduxDevTools(settings =>
                {
                    settings.Name = "Blazyload Components - Fluxor";
                    settings.JsonSerializerOptions.Converters.Add(new LoadedAssemblyJsonConverter());
                    settings.AddActionFilter(action => action.GetType().Namespace?.StartsWith("RonSijm.Demo.", StringComparison.Ordinal) == true);
                }));
#endif
            });
        });
        builder.Services.AddBlazyloadComponents();
        await builder.Build().RunAsync();
    }
}
