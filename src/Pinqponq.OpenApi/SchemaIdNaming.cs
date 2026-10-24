namespace Pinqponq.OpenApi;

/// <summary>How schema ids in <c>components.schemas</c> are built from .NET types.</summary>
public enum SchemaIdNaming
{
    /// <summary>The ASP.NET Core default, which is the short type name.</summary>
    Default = 0,

    /// <summary>
    /// The full type name with <c>.</c> and <c>+</c> replaced by <c>_</c>, so two types with the same short
    /// name in different namespaces get different ids.
    /// </summary>
    FullName = 1,
}
