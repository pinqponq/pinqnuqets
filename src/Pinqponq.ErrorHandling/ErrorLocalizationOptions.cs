using System.Reflection;
using Pinqponq.Contracts;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Localized error responses: the response code is looked up in a message catalog and the
/// message is returned in the language of the request. Enabled by
/// <c>AddPinqponqErrorLocalization</c>.
/// </summary>
public sealed class ErrorLocalizationOptions
{
    /// <summary>Set by <c>AddPinqponqErrorLocalization</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Request header carrying the language. Defaults to <c>Accept-Language</c>. Its first tag is used
    /// (<c>tr-TR,tr;q=0.9</c> → <c>TR-TR</c>); without a catalog for the full tag, its primary language (<c>TR</c>) is tried.
    /// </summary>
    public string LanguageHeader { get; set; } = "Accept-Language";

    /// <summary>Language used when the header is missing or has no message for the code. Defaults to <c>EN</c>.</summary>
    public string DefaultLanguage { get; set; } = "EN";

    /// <summary>
    /// Code returned for an <c>IExceptionResolver</c> code the catalog does not know; its message also serves a
    /// mapped code (<c>StatusMappingResolver</c> or the built-in table) the catalog does not know. Defaults to <c>general_error</c>.
    /// </summary>
    public string FallbackResponseCode { get; set; } = CommonErrorCodes.GeneralError;

    /// <summary>Message returned when the catalog has no message for the code in any usable language.</summary>
    public string DefaultMessage { get; set; } = "An unexpected error occurred.";

    /// <summary>
    /// Directory with the application's catalog files (<c>EN.json</c>, <c>TR.json</c>, …, each a
    /// <c>{ "code": "message" }</c> object), relative to <see cref="AppContext.BaseDirectory"/>.
    /// Its entries override the embedded ones. Defaults to <c>Localization</c>; <see langword="null"/> reads none.
    /// </summary>
    public string? CatalogDirectory { get; set; } = "Localization";

    /// <summary>
    /// Assemblies whose embedded <c>*.Localization.&lt;LANGUAGE&gt;.json</c> resources form the
    /// built-in catalog. Contains this package's assembly (<c>general_error</c>, <c>api_*</c>).
    /// </summary>
    public IList<Assembly> CatalogAssemblies { get; } = [typeof(ErrorLocalizationOptions).Assembly];
}
