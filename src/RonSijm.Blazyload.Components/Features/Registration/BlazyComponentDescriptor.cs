namespace RonSijm.Blazyload.Components;

public sealed record BlazyComponentDescriptor(string Name, string AssemblyName, string TypeName)
{
    public Type? ComponentType { get; init; }
}
