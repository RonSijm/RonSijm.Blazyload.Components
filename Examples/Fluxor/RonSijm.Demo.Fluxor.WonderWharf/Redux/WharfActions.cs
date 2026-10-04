using RonSijm.Demo.Fluxor.WonderWharf.Models;

namespace RonSijm.Demo.Fluxor.WonderWharf.Redux;

public sealed record LoadWharfEvents(bool SimulateFailure = false);
public sealed record WharfEventsLoaded(IReadOnlyList<WharfEvent> Events);
public sealed record WharfEventsLoadFailed(string Message);
public sealed record SelectWharfEvent(Guid EventId);
public sealed record PublishSelectedWharfEvent;
