namespace RonSijm.Demo.WonderWharf.Services;

public sealed class WharfEventService()
{
    public IReadOnlyList<(string Name, DateOnly Date)> GetUpcomingEvents() =>
    [
        ("Fall festival", new DateOnly(2026, 10, 10)),
        ("Pier concert", new DateOnly(2026, 10, 17))
    ];
}
