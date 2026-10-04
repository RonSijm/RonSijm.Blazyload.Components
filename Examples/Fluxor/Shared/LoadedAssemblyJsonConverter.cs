using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RonSijm.Demo.Fluxor.Diagnostics;

internal sealed class LoadedAssemblyJsonConverter : JsonConverter<Assembly>
{
    public override Assembly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var name = reader.GetString() ?? throw new JsonException("An assembly identity must be a string.");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.FullName == name);
        if (assembly is null)
        {
            throw new JsonException($"Assembly '{name}' is not already loaded. Restoring a diagnostics snapshot cannot load code.");
        }

        return assembly;
    }

    public override void Write(Utf8JsonWriter writer, Assembly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.FullName);
    }
}
