using Fluxor;
using RonSijm.Demo.Fluxor.WonderWharf.Models;
using RonSijm.Syringe;

namespace RonSijm.Demo.Fluxor.WonderWharf.Redux;

[FeatureState(Name = "Fluxor.WonderWharf")]
public sealed record WonderWharfViewModel : IDispatchOnInitialized
{
    public bool IsLoading { get; init; }
    public bool HasLoaded { get; init; }
    public IReadOnlyList<WharfEvent> Events { get; init; } = [];
    public Guid? SelectedEventId { get; init; }
    public string? Error { get; init; }
    public int LoadCount { get; init; }
    public int AttemptCount { get; init; }
}
