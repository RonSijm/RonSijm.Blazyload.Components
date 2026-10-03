namespace RonSijm.Blazyload.Components;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BlazyComponentAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
