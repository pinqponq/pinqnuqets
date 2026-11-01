using System.Text;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;

namespace Pinqponq.OpenApi;

internal static class SchemaIdFactory
{
    public static string? CreateFullNameId(JsonTypeInfo typeInfo)
    {
        var isInlinedByDefault = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo) is null;
        if (isInlinedByDefault)
        {
            return null;
        }

        return RemoveCharactersNotAllowedInIds(CreateTypeId(typeInfo.Type));
    }

    private static string CreateTypeId(Type type)
    {
        var qualifiedName = CreateQualifiedName(type).Replace('.', '_');
        if (!type.IsGenericType)
        {
            return qualifiedName;
        }

        var typeArgumentIds = type.GetGenericArguments().Select(CreateTypeId);
        return $"{qualifiedName}Of{string.Join("And", typeArgumentIds)}";
    }

    private static string CreateQualifiedName(Type type)
    {
        var name = RemoveGenericArity(type.Name);
        if (type.DeclaringType is not null)
        {
            return $"{CreateQualifiedName(type.DeclaringType)}_{name}";
        }

        return type.Namespace is null ? name : $"{type.Namespace}.{name}";
    }

    private static string RemoveGenericArity(string typeName)
    {
        var arityStart = typeName.IndexOf('`', StringComparison.Ordinal);
        return arityStart < 0 ? typeName : typeName[..arityStart];
    }

    private static string RemoveCharactersNotAllowedInIds(string id)
    {
        var allowed = new StringBuilder(id.Length);
        foreach (var character in id)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                allowed.Append(character);
            }
        }

        return allowed.ToString();
    }
}
