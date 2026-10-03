namespace RonSijm.Blazyload.Components;

public sealed class BlazyComponentOptions
{
    public string? ManifestPath { get; set; } = "blazy-components.json";

    public bool EnableLogging { get; set; }

    public IList<BlazyComponentDescriptor> Components { get; } = new List<BlazyComponentDescriptor>();

    public BlazyComponentOptions Register(string name, string assemblyName, string typeName)
    {
        Components.Add(new BlazyComponentDescriptor(name, assemblyName, typeName));
        return this;
    }
}
