using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Framework;

namespace RonSijm.Blazyload.Components.Build;

public sealed class GenerateBlazyComponentManifest : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Assemblies { get; set; } = [];

    [Required]
    public string ManifestPath { get; set; } = null!;

    [Required]
    public string LinkerDescriptorPath { get; set; } = null!;

    public override bool Execute()
    {
        var components = new SortedDictionary<string, ComponentEntry>(StringComparer.Ordinal);
        foreach (var path in Assemblies.Select(item => item.GetMetadata("FullPath")).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                ReadAssembly(path, components);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
            {
                Log.LogError($"BLZCMP001: Cannot inspect component assembly '{path}': {exception.Message}");
            }
        }

        if (Log.HasLoggedErrors)
        {
            return false;
        }

        try
        {
            var entries = components.ToDictionary(entry => entry.Key, entry => new { assembly = entry.Value.Assembly, type = entry.Value.Type }, StringComparer.Ordinal);
            WriteIfChanged(ManifestPath, JsonSerializer.Serialize(new { components = entries }, new JsonSerializerOptions { WriteIndented = true }));

            var linker = new XElement("linker", components.Values.GroupBy(entry => entry.Assembly).Select(group => new XElement("assembly", new XAttribute("fullname", group.Key), group.Select(entry => new XElement("type", new XAttribute("fullname", entry.Type.Replace('+', '/')), new XAttribute("preserve", "all"))))));
            WriteIfChanged(LinkerDescriptorPath, linker.ToString());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"BLZCMP002: Cannot write component metadata: {exception.Message}");
            return false;
        }
    }

    private void ReadAssembly(string path, SortedDictionary<string, ComponentEntry> components)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return;
        }

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            return;
        }

        var assemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            foreach (var attributeHandle in definition.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (!IsComponentAttribute(reader, attribute))
                {
                    continue;
                }

                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                {
                    throw new BadImageFormatException("Invalid component attribute encoding.");
                }

                var name = blob.ReadSerializedString();
                var typeName = GetTypeName(reader, handle);
                if (string.IsNullOrWhiteSpace(name))
                {
                    Log.LogError($"BLZCMP003: Component '{assemblyName}:{typeName}' has an empty logical name.");
                    continue;
                }

                if ((definition.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0 || !IsVisibleClosedType(reader, handle))
                {
                    Log.LogError($"BLZCMP004: Component '{name}' ({assemblyName}:{typeName}) must be public, concrete and nongeneric.");
                    continue;
                }

                var entry = new ComponentEntry(assemblyName, typeName);
                if (!components.TryAdd(name, entry))
                {
                    var original = components[name];
                    Log.LogError($"BLZCMP005: Duplicate Blazy component '{name}': '{original.Assembly}:{original.Type}' and '{assemblyName}:{typeName}'.");
                }
            }
        }
    }

    private static bool IsComponentAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        return reader.GetString(type.Namespace) == "RonSijm.Blazyload.Components" && reader.GetString(type.Name) == "BlazyComponentAttribute";
    }

    private static bool IsVisibleClosedType(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        if (definition.GetGenericParameters().Count != 0)
        {
            return false;
        }

        var declaring = definition.GetDeclaringType();
        var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
        if (declaring.IsNil)
        {
            return visibility == TypeAttributes.Public;
        }

        return visibility == TypeAttributes.NestedPublic && IsVisibleClosedType(reader, declaring);
    }

    private static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{GetTypeName(reader, declaring)}+{name}";
        }

        var typeNamespace = reader.GetString(definition.Namespace);
        var result = string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
        return result;
    }

    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            File.WriteAllText(path, content);
        }
    }

    private sealed record ComponentEntry(string Assembly, string Type);
}
