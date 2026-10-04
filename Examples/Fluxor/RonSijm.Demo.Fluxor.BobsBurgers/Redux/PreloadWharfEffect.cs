using Fluxor;
using Microsoft.AspNetCore.Components;
using RonSijm.Blazyload;
using RonSijm.Demo.Fluxor.WonderWharf.Models;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

public sealed record PreloadWharf(bool UsePath);
public sealed record WharfPreloadCompleted;

public sealed class PreloadWharfEffect : Effect<PreloadWharf>
{
    [Inject] public IState<RestaurantViewModel> State { get; set; } = null!;
    [Inject] public AssemblyLoadConfiguration Configuration { get; set; } = null!;

    public override Task HandleAsync(PreloadWharf action, IDispatcher dispatcher)
    {
        if (State.Value.LoadedAssemblies.Contains(WonderWharfFeature.AssemblyName))
        {
            dispatcher.Dispatch(new WharfPreloadCompleted());
            return Task.CompletedTask;
        }

        if (action.UsePath)
        {
            var assembly = Configuration.GetAssembly(WonderWharfFeature.LoadingPath);
            if (assembly != $"{WonderWharfFeature.AssemblyName}.wasm")
            {
                throw new InvalidOperationException($"The host must map '{WonderWharfFeature.LoadingPath}' to '{WonderWharfFeature.AssemblyName}.wasm' before path-based preloading.");
            }

            dispatcher.Dispatch(new LoadAssemblyForPath(WonderWharfFeature.LoadingPath));
        }
        else
        {
            dispatcher.Dispatch(new LoadAssembly($"{WonderWharfFeature.AssemblyName}.wasm"));
        }

        return Task.CompletedTask;
    }
}
