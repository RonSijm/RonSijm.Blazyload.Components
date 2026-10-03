namespace RonSijm.Blazyload.Components;

public interface IBlazyComponentRegistry
{
    ValueTask<BlazyComponentDescriptor> ResolveAsync(string name, CancellationToken cancellationToken = default);
}
