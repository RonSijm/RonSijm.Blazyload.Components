using Fluxor;
using RonSijm.Demo.Fluxor.WonderWharf.Models;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Services;
using RonSijm.Demo.Fluxor.WonderWharf.Redux;

namespace RonSijm.Demo.Fluxor.WonderWharf.Publications.Redux;

public sealed record WharfPublicationTrackingEnabled;
public sealed record WharfPublicationRecorded;
public sealed record WharfPublicationAudited(int Calls);

public static class WharfPublicationTrackingReducers
{
    [ReducerMethod]
    public static WonderWharfViewModel Enabled(WonderWharfViewModel state, WharfPublicationTrackingEnabled action)
    {
        return state with { IsPublicationTrackingEnabled = true };
    }

    [ReducerMethod]
    public static WonderWharfViewModel Recorded(WonderWharfViewModel state, WharfPublicationRecorded action)
    {
        return state with { PublicationCount = state.PublicationCount + 1 };
    }

    [ReducerMethod]
    public static WonderWharfViewModel Audited(WonderWharfViewModel state, WharfPublicationAudited action)
    {
        return state with { PublicationAuditCount = action.Calls };
    }
}

public sealed class WharfPublicationTrackingEffects(IWharfPublicationCounter counter)
{
    public IWharfPublicationCounter Counter { get; } = counter;

    [EffectMethod]
    public Task AuditPublication(WharfEventSelected action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new WharfPublicationAudited(Counter.Record()));
        return Task.CompletedTask;
    }

    [EffectMethod]
    public static Task RecordPublication(WharfEventSelected action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new WharfPublicationRecorded());
        return Task.CompletedTask;
    }
}
