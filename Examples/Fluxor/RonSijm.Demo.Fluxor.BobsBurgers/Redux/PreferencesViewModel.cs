using Fluxor;
using RonSijm.Syringe;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

[FeatureState(Name = "Fluxor.Preferences")]
public sealed record PreferencesViewModel : IDispatchFirstTimeOnAfterRender
{
    public bool IsDarkMode { get; init; }
    public bool IsApplyingTheme { get; init; }
    public string BrowserMessage { get; init; } = "Waiting for the first rendered shell";
    public string? Error { get; init; }
    public int FirstRenderCount { get; init; }
    public string? LastNavigation { get; init; }
}

public sealed record ToggleDemoTheme;
public sealed record DemoThemeApplied(bool IsDarkMode);
public sealed record DemoBrowserReady(string Message);
public sealed record DemoBrowserFailed(string Message);
public sealed record NavigateDemoSection(string Section);
public sealed record DemoSectionVisited(string Section);

public static class PreferencesReducers
{
    [ReducerMethod(typeof(ToggleDemoTheme))]
    public static PreferencesViewModel ApplyingTheme(PreferencesViewModel state)
    {
        return state with { IsApplyingTheme = true, Error = null };
    }

    [ReducerMethod]
    public static PreferencesViewModel ThemeApplied(PreferencesViewModel state, DemoThemeApplied action)
    {
        return state with { IsDarkMode = action.IsDarkMode, IsApplyingTheme = false };
    }

    [ReducerMethod]
    public static PreferencesViewModel BrowserReady(PreferencesViewModel state, DemoBrowserReady action)
    {
        return state with { BrowserMessage = action.Message, Error = null, FirstRenderCount = state.FirstRenderCount + 1 };
    }

    [ReducerMethod]
    public static PreferencesViewModel BrowserFailed(PreferencesViewModel state, DemoBrowserFailed action)
    {
        return state with { Error = action.Message, IsApplyingTheme = false };
    }

    [ReducerMethod]
    public static PreferencesViewModel SectionVisited(PreferencesViewModel state, DemoSectionVisited action)
    {
        return state with { LastNavigation = action.Section };
    }
}
