namespace RonSijm.Blazyload.Components;

public interface IBlazyComponentManifestProvider
{
    Task<IReadOnlyList<BlazyComponentDescriptor>> GetComponentsAsync(CancellationToken cancellationToken = default);
}
