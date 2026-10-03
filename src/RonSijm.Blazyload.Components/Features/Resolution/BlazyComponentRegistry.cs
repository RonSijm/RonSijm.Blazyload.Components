using System.Collections.Concurrent;

namespace RonSijm.Blazyload.Components;

internal sealed class BlazyComponentRegistry(IBlazyComponentManifestProvider manifestProvider, IBlazyAssemblyLoader assemblyLoader, IBlazyLogger logger, BlazyComponentOptions options) : IBlazyComponentRegistry
{
    private readonly Lazy<Task<Dictionary<string, BlazyComponentDescriptor>>> _manifest = new(() => ReadManifestAsync(manifestProvider, logger));
    private readonly ConcurrentDictionary<string, Lazy<Task<BlazyComponentDescriptor>>> _resolutions = new(StringComparer.Ordinal);

    public async ValueTask<BlazyComponentDescriptor> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = _resolutions.GetOrAdd(name, key => new Lazy<Task<BlazyComponentDescriptor>>(() => ResolveInternalAsync(key)));
        return await resolution.Value.WaitAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, BlazyComponentDescriptor>> ReadManifestAsync(IBlazyComponentManifestProvider provider, IBlazyLogger logger)
    {
        try
        {
            var entries = await provider.GetComponentsAsync();
            var descriptors = new Dictionary<string, BlazyComponentDescriptor>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.AssemblyName) || string.IsNullOrWhiteSpace(entry.TypeName))
                {
                    throw new BlazyComponentRegistrationException($"Invalid registration for component '{entry.Name}': name, assembly and type must be nonempty.");
                }

                if (!descriptors.TryAdd(entry.Name, entry))
                {
                    var original = descriptors[entry.Name];
                    throw new BlazyComponentRegistrationException($"Duplicate Blazy component '{entry.Name}': '{original.AssemblyName}:{original.TypeName}' and '{entry.AssemblyName}:{entry.TypeName}'.");
                }
            }

            return descriptors;
        }
        catch (BlazyComponentException exception)
        {
            logger.WriteLine(exception);
            throw;
        }
    }

    private async Task<BlazyComponentDescriptor> ResolveInternalAsync(string name)
    {
        var manifest = await _manifest.Value;
        try
        {
            if (!manifest.TryGetValue(name, out var descriptor))
            {
                throw new BlazyComponentNotFoundException(name);
            }

            if (options.EnableLogging)
            {
                logger.WriteLine($"Resolving Blazy component '{name}' from assembly '{descriptor.AssemblyName}'.");
            }

            var assembly = await LoadAssemblyAsync(descriptor);
            Type? type;
            try
            {
                type = assembly.GetType(descriptor.TypeName, throwOnError: false, ignoreCase: false);
            }
            catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                throw new BlazyComponentTypeException(name, descriptor.AssemblyName, descriptor.TypeName, "could not be resolved", exception);
            }

            if (type is null)
            {
                throw new BlazyComponentTypeException(name, descriptor.AssemblyName, descriptor.TypeName, "was not found");
            }

            if (!type.IsClass || !type.IsVisible || type.IsAbstract || type.ContainsGenericParameters || !typeof(IComponent).IsAssignableFrom(type))
            {
                throw new BlazyComponentTypeException(name, descriptor.AssemblyName, descriptor.TypeName, "must be a public, concrete, closed Blazor IComponent");
            }

            var attribute = type.GetCustomAttribute<BlazyComponentAttribute>(inherit: false);
            if (attribute is not null && !string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                throw new BlazyComponentTypeException(name, descriptor.AssemblyName, descriptor.TypeName, $"declares the different logical name '{attribute.Name}' (stale manifest)");
            }

            return descriptor with { ComponentType = type };
        }
        catch (BlazyComponentException exception)
        {
            logger.WriteLine(exception);
            throw;
        }
    }

    private async Task<Assembly> LoadAssemblyAsync(BlazyComponentDescriptor descriptor)
    {
        try
        {
            var assemblies = await assemblyLoader.LoadAssemblyAsync($"{descriptor.AssemblyName}.wasm");
            var assembly = assemblies.Concat(assemblyLoader.AdditionalAssemblies).Concat(AppDomain.CurrentDomain.GetAssemblies()).FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, descriptor.AssemblyName, StringComparison.OrdinalIgnoreCase));
            if (assembly is null)
            {
                throw new BlazyComponentLoadException(descriptor.Name, descriptor.AssemblyName, new InvalidOperationException($"The requested assembly '{descriptor.AssemblyName}' was not available after loading."));
            }

            return assembly;
        }
        catch (BlazyAssemblyLoadException exception)
        {
            throw new BlazyComponentLoadException(descriptor.Name, descriptor.AssemblyName, exception);
        }
    }
}
