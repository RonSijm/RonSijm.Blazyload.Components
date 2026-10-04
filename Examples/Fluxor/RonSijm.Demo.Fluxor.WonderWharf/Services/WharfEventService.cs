using System.Net.Http.Json;
using System.Text.Json;
using RonSijm.Demo.Fluxor.WonderWharf.Models;

namespace RonSijm.Demo.Fluxor.WonderWharf.Services;

public sealed class WharfEventService(HttpClient httpClient)
{
    public int RequestCount { get; private set; }

    public async Task<IReadOnlyList<WharfEvent>> GetEventsAsync(bool simulateFailure)
    {
        RequestCount++;
        if (simulateFailure)
        {
            throw new WharfEventsUnavailableException("Simulated event-service failure. Existing events and selection are retained; use Refresh to try again.");
        }

        var events = await httpClient.GetFromJsonAsync<WharfEvent[]>("_content/RonSijm.Demo.Fluxor.WonderWharf/events.json");
        if (events is null)
        {
            throw new JsonException("The event service returned null instead of an event list.");
        }

        return events;
    }
}

public sealed class WharfEventsUnavailableException(string message) : Exception(message);
