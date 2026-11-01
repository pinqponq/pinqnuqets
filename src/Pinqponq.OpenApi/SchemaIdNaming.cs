namespace Pinqponq.OpenApi;

/// <summary>How schema ids in <c>components.schemas</c> are built from .NET types.</summary>
public enum SchemaIdNaming
{
    /// <summary>The ASP.NET Core default, which is the short type name.</summary>
    Default = 0,

    /// <summary>
    /// The namespace and the declaring types joined to the type name with <c>_</c>, so two types with the same
    /// short name in different namespaces get different ids. A generic type adds <c>Of</c> and the ids of its
    /// type arguments joined with <c>And</c>, for example <c>My_App_PageOfMy_App_Order</c>. Characters that
    /// OpenAPI does not allow in an id are removed. Types that ASP.NET Core writes inline (primitives, arrays,
    /// lists and dictionaries) stay inline.
    /// </summary>
    FullName = 1,
}
