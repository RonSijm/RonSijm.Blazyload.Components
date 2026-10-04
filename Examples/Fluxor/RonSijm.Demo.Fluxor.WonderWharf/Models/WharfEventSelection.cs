namespace RonSijm.Demo.Fluxor.WonderWharf.Models;

[BlazyContract]
public sealed record WharfEventSelection(Guid EventId, string Name, DateOnly Date);
