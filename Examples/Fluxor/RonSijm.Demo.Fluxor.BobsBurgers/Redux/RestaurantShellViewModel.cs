using Fluxor;
using RonSijm.Syringe;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

[FeatureState(Name = "Fluxor.Shell")]
public sealed record RestaurantShellViewModel
{
    [ReduceInto]
    public RestaurantViewModel? Restaurant { get; init; }

    [ReduceInto]
    public PreferencesViewModel? Preferences { get; init; }
}
