namespace RonSijm.Blazyload.Components;

public sealed class BlazyComponentTypeException(string name, string assemblyName, string typeName, string reason, Exception? innerException = null) : BlazyComponentException($"Cannot render Blazy component '{name}': type '{typeName}' in assembly '{assemblyName}' {reason}.", innerException);
