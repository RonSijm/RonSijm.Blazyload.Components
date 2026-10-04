using System.Text.Json;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using RonSijm.Demo.Fluxor.WonderWharf.Components;
using RonSijm.Demo.Fluxor.WonderWharf.Models;
using RonSijm.Demo.Fluxor.WonderWharf.Services;
using RonSijm.Syringe;
using RonSijm.Syringe.Models;

namespace RonSijm.Demo.Fluxor.WonderWharf.Redux;

public sealed class CalendarInitializedEffect() : PageEffect<EventCalendar>(PageEventType.OnInitialized)
{
    [Inject] public IState<WonderWharfViewModel> State { get; set; } = null!;

    protected override Task HandleAsync(PageEvent action, IDispatcher dispatcher)
    {
        if (!State.Value.HasLoaded && !State.Value.IsLoading)
        {
            dispatcher.Dispatch(new LoadWharfEvents());
        }

        return Task.CompletedTask;
    }
}

public sealed class WharfSelectionEffect : Effect<PublishSelectedWharfEvent>
{
    [Inject] public IState<WonderWharfViewModel> State { get; set; } = null!;

    public override Task HandleAsync(PublishSelectedWharfEvent action, IDispatcher dispatcher)
    {
        var selected = State.Value.Events.FirstOrDefault(item => item.Id == State.Value.SelectedEventId);
        if (selected is null)
        {
            throw new InvalidOperationException("Select a Wonder Wharf event before publishing an application event.");
        }

        dispatcher.Dispatch(new WharfEventSelected(new WharfEventSelection(selected.Id, selected.Name, selected.Date)));
        return Task.CompletedTask;
    }
}

public sealed class LoadWharfEventsEffect : Effect<LoadWharfEvents>
{
    [Inject] public WharfEventService Service { get; set; } = null!;
    [Inject] public ILogger<LoadWharfEventsEffect> Logger { get; set; } = null!;

    public override async Task HandleAsync(LoadWharfEvents action, IDispatcher dispatcher)
    {
        try
        {
            var events = await Service.GetEventsAsync(action.SimulateFailure);
            dispatcher.Dispatch(new WharfEventsLoaded(events));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or WharfEventsUnavailableException)
        {
            Logger.LogWarning(exception, "Wonder Wharf event loading failed");
            dispatcher.Dispatch(new WharfEventsLoadFailed(exception.Message));
        }
    }
}
