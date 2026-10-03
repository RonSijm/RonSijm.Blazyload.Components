using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RonSijm.Blazyload;
using RonSijm.Blazyload.Components;

namespace RonSijm.Demo.Blazyload.Components.Orchestrator.Client;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.UseBlazyload();
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddBlazyloadComponents();
        await builder.Build().RunAsync();
    }
}
