namespace RonSijm.Blazyload.Components;

public sealed class BlazyComponentNotFoundException(string name) : BlazyComponentException($"Blazy component '{name}' is not registered.");
