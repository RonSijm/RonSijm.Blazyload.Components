using Fluxor;

namespace RonSijm.Demo.Fluxor.WonderWharf.Redux;

public static class WharfReducers
{
    [ReducerMethod]
    public static WonderWharfViewModel Loading(WonderWharfViewModel state, LoadWharfEvents action)
    {
        return state with { IsLoading = true, Error = null, AttemptCount = state.AttemptCount + 1 };
    }

    [ReducerMethod]
    public static WonderWharfViewModel Loaded(WonderWharfViewModel state, WharfEventsLoaded action)
    {
        return state with { IsLoading = false, HasLoaded = true, Events = action.Events, LoadCount = state.LoadCount + 1 };
    }

    [ReducerMethod]
    public static WonderWharfViewModel Failed(WonderWharfViewModel state, WharfEventsLoadFailed action)
    {
        return state with { IsLoading = false, Error = action.Message };
    }
}

public sealed class SelectWharfEventReducer : Reducer<WonderWharfViewModel, SelectWharfEvent>
{
    public override WonderWharfViewModel Reduce(WonderWharfViewModel state, SelectWharfEvent action)
    {
        return state with { SelectedEventId = action.EventId };
    }
}
