using Fluxor;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

[FeatureState(Name = "Fluxor.Diagnostics")]
public sealed record TraceViewModel
{
    public int Sequence { get; init; }
    public IReadOnlyList<ActionTraceEntry> Entries { get; init; } = [];
}

public sealed record ActionTraceEntry(int Sequence, string Description);
public sealed record ActionObserved(string Description);

public static class TraceReducers
{
    [ReducerMethod]
    public static TraceViewModel Observed(TraceViewModel state, ActionObserved action)
    {
        var sequence = state.Sequence + 1;
        return state with { Sequence = sequence, Entries = state.Entries.Append(new ActionTraceEntry(sequence, action.Description)).TakeLast(15).ToArray() };
    }
}
