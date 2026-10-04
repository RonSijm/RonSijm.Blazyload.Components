using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using RonSijm.Demo.Fluxor.BobsBurgers.Components;
using RonSijm.Demo.Fluxor.BobsBurgers.Services;
using RonSijm.Syringe;
using RonSijm.Syringe.Models;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

public sealed class ShellFirstRenderedEffect() : PageEffect<BrowserStatus>(PageEventType.FirstTimeOnAfterRender)
{
    [Inject] public DemoBrowserInterop Browser { get; set; } = null!;
    [Inject] public IState<PreferencesViewModel> State { get; set; } = null!;
    [Inject] public ILogger<ShellFirstRenderedEffect> Logger { get; set; } = null!;

    protected override async Task HandleAsync(PageEvent action, IDispatcher dispatcher)
    {
        try
        {
            var message = await Browser.InitializeShellAsync(State.Value.IsDarkMode);
            dispatcher.Dispatch(new DemoBrowserReady(message));
        }
        catch (JSException exception)
        {
            Logger.LogWarning(exception, "Fluxor demo browser initialization failed");
            dispatcher.Dispatch(new DemoBrowserFailed(exception.Message));
        }
    }
}

public sealed class ToggleDemoThemeEffect : Effect<ToggleDemoTheme>
{
    [Inject] public DemoBrowserInterop Browser { get; set; } = null!;
    [Inject] public IState<PreferencesViewModel> State { get; set; } = null!;
    [Inject] public ILogger<ToggleDemoThemeEffect> Logger { get; set; } = null!;

    public override async Task HandleAsync(ToggleDemoTheme action, IDispatcher dispatcher)
    {
        try
        {
            var isDarkMode = !State.Value.IsDarkMode;
            await Browser.ApplyThemeAsync(isDarkMode);
            dispatcher.Dispatch(new DemoThemeApplied(isDarkMode));
        }
        catch (JSException exception)
        {
            Logger.LogWarning(exception, "Fluxor demo theme change failed");
            dispatcher.Dispatch(new DemoBrowserFailed(exception.Message));
        }
    }
}

public sealed class NavigateDemoSectionEffect : Effect<NavigateDemoSection>
{
    [Inject] public NavigationManager Navigation { get; set; } = null!;

    public override Task HandleAsync(NavigateDemoSection action, IDispatcher dispatcher)
    {
        var target = new UriBuilder(Navigation.Uri) { Fragment = action.Section };
        Navigation.NavigateTo(target.Uri.AbsoluteUri);
        dispatcher.Dispatch(new DemoSectionVisited(action.Section));
        return Task.CompletedTask;
    }
}
