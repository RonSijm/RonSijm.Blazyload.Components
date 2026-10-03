using System.Text.Json;

namespace RonSijm.Blazyload.Components;

internal sealed class BlazyComponentManifestProvider(HttpClient httpClient, NavigationManager navigationManager, BlazyComponentOptions options) : IBlazyComponentManifestProvider
{
    public async Task<IReadOnlyList<BlazyComponentDescriptor>> GetComponentsAsync(CancellationToken cancellationToken = default)
    {
        var components = options.Components.ToList();
        if (options.ManifestPath is null)
        {
            return components;
        }

        var manifestUri = new Uri(new Uri(navigationManager.BaseUri), options.ManifestPath);
        try
        {
            using var response = await httpClient.GetAsync(manifestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count(entry => entry.NameEquals("components")) != 1 || !document.RootElement.TryGetProperty("components", out var entries) || entries.ValueKind != JsonValueKind.Object)
            {
                throw new BlazyComponentRegistrationException($"Component manifest '{manifestUri}' must contain exactly one 'components' object.");
            }

            foreach (var entry in entries.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("assembly", out var assembly) || assembly.ValueKind != JsonValueKind.String || !entry.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                {
                    throw new BlazyComponentRegistrationException($"Component '{entry.Name}' in manifest '{manifestUri}' must specify string 'assembly' and 'type' values.");
                }

                components.Add(new BlazyComponentDescriptor(entry.Name, assembly.GetString()!, type.GetString()!));
            }

            return components;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            throw new BlazyComponentRegistrationException($"Failed to read component manifest '{manifestUri}'.", exception);
        }
    }
}
