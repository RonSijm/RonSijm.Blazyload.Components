namespace RonSijm.Blazyload.Components;

public sealed class BlazyComponentLoadException(string name, string assemblyName, Exception? innerException = null) : BlazyComponentException($"Failed to load Blazy component '{name}' from assembly '{assemblyName}'. Blazyload did not complete the load; see its diagnostics for download, dependency or bootstrap failures.", innerException);
