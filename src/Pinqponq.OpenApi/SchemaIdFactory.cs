using System.Text.Json.Serialization.Metadata;

namespace Pinqponq.OpenApi;

internal static class SchemaIdFactory
{
    public static string CreateFullNameId(JsonTypeInfo typeInfo)
    {
        var fullName = typeInfo.Type.FullName ?? typeInfo.Type.Name;
        return fullName.Replace('.', '_').Replace('+', '_');
    }
}
