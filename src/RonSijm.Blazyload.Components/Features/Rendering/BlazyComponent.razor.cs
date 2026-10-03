namespace RonSijm.Blazyload.Components;

public partial class BlazyComponent : IDisposable
{
    private Type? _componentType;
    private string? _resolvedName;
    private Dictionary<string, object>? _parameters;
    private CancellationTokenSource? _resolutionCancellation;

    [Inject]
    public IBlazyComponentRegistry Registry { get; set; } = null!;

    [Parameter, EditorRequired]
    public string Name { get; set; } = null!;

    [Parameter]
    public IReadOnlyDictionary<string, object?>? Parameters { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _parameters = Parameters?.ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
        if (_resolvedName == Name)
        {
            return;
        }

        _resolutionCancellation?.Cancel();
        _resolutionCancellation?.Dispose();
        _resolutionCancellation = new CancellationTokenSource();
        var cancellationToken = _resolutionCancellation.Token;
        var name = Name;
        _componentType = null;
        _resolvedName = null;

        try
        {
            var descriptor = await Registry.ResolveAsync(name, cancellationToken);
            if (descriptor.ComponentType is null)
            {
                throw new BlazyComponentTypeException(name, descriptor.AssemblyName, descriptor.TypeName, "was returned unresolved by the component registry");
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _componentType = descriptor.ComponentType;
                _resolvedName = name;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer name or component disposal superseded this render, not the shared assembly load.
        }
    }

    public void Dispose()
    {
        _resolutionCancellation?.Cancel();
        _resolutionCancellation?.Dispose();
    }
}
