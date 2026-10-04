using Fluxor;
using RonSijm.Blazyload;
using RonSijm.Demo.Fluxor.WonderWharf.Models;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

[FeatureState(Name = "Fluxor.BobsBurgers")]
public sealed record RestaurantViewModel
{
    public WharfEventSelection? SelectedEvent { get; init; }
    public IReadOnlyList<string> LoadedAssemblies { get; init; } = [];
    public WharfEventSelection? BroadcastEvent { get; init; }
    public int PublicEventCount { get; init; }
    public bool PreloadPending { get; init; }
    public string? PreloadStrategy { get; init; }
}

public sealed record RecordWharfSelection(WharfEventSelection Selection);

public static class RestaurantReducers
{
    [ReducerMethod]
    public static RestaurantViewModel AssemblyAvailable(RestaurantViewModel state, AssemblyLoaded action)
    {
        var name = action.LoadedAssembly.GetName().Name!;
        return state with { LoadedAssemblies = [.. state.LoadedAssemblies, name], PreloadPending = state.PreloadPending && name != WonderWharfFeature.AssemblyName };
    }

    [ReducerMethod]
    public static RestaurantViewModel Preloading(RestaurantViewModel state, PreloadWharf action)
    {
        var strategy = action.UsePath ? "LoadAssemblyForPath" : "LoadAssembly";
        return state with { PreloadPending = true, PreloadStrategy = strategy };
    }

    [ReducerMethod(typeof(WharfPreloadCompleted))]
    public static RestaurantViewModel Preloaded(RestaurantViewModel state)
    {
        return state with { PreloadPending = false };
    }

    [ReducerMethod]
    public static RestaurantViewModel PublicEventReceived(RestaurantViewModel state, WharfEventSelected action)
    {
        return state with { BroadcastEvent = action.Selection, PublicEventCount = state.PublicEventCount + 1 };
    }

    [ReducerMethod]
    public static RestaurantViewModel Selected(RestaurantViewModel state, RecordWharfSelection action)
    {
        return state with { SelectedEvent = action.Selection };
    }
}
