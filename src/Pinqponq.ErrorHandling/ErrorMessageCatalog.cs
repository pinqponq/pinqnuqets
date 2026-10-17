using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Localized error messages by response code and language, built once from the embedded
/// catalogs of <see cref="ErrorLocalizationOptions.CatalogAssemblies"/> and then the
/// application's <see cref="ErrorLocalizationOptions.CatalogDirectory"/>.
/// </summary>
internal sealed class ErrorMessageCatalog
{
    private const string CatalogFileExtension = ".json";
    private const string CatalogFileSearchPattern = "*.json";
    private const string EmbeddedCatalogSegment = ".Localization.";
    private const char LanguageSubtagSeparator = '-';
    private const char LanguageListSeparator = ',';
    private const char LanguageWeightSeparator = ';';

    private readonly ErrorLocalizationOptions _localizationOptions;
    private readonly ILogger<ErrorMessageCatalog> _logger;
    private readonly Lazy<Dictionary<string, Dictionary<string, string>>> _messagesByCode;

    public ErrorMessageCatalog(
        IOptions<ErrorLocalizationOptions> localizationOptions,
        ILogger<ErrorMessageCatalog> logger)
    {
        _localizationOptions = localizationOptions.Value;
        _logger = logger;
        _messagesByCode = new Lazy<Dictionary<string, Dictionary<string, string>>>(LoadMessages);
    }

    public bool Contains(string responseCode) => _messagesByCode.Value.ContainsKey(responseCode);

    public string? FindMessage(string responseCode, string language)
    {
        if (!_messagesByCode.Value.TryGetValue(responseCode, out var messagesByLanguage))
        {
            return null;
        }

        if (messagesByLanguage.TryGetValue(language, out var message))
        {
            return message;
        }

        var subtagSeparatorIndex = language.IndexOf(LanguageSubtagSeparator);
        if (subtagSeparatorIndex > 0
            && messagesByLanguage.TryGetValue(language[..subtagSeparatorIndex], out var primaryLanguageMessage))
        {
            return primaryLanguageMessage;
        }

        return messagesByLanguage.TryGetValue(NormalizeLanguage(_localizationOptions.DefaultLanguage), out var defaultMessage)
            ? defaultMessage
            : null;
    }

    public static string NormalizeLanguage(string language) => language.ToUpperInvariant();

    // An Accept-Language value lists several tags with weights ("tr-TR,tr;q=0.9,en;q=0.8"); the first one is used.
    public static string FirstLanguageTag(string headerValue) =>
        headerValue.Split(LanguageListSeparator)[0].Split(LanguageWeightSeparator)[0].Trim();

    private Dictionary<string, Dictionary<string, string>> LoadMessages()
    {
        var messagesByCode = new Dictionary<string, Dictionary<string, string>>();

        foreach (var catalogAssembly in _localizationOptions.CatalogAssemblies)
        {
            LoadEmbeddedCatalogs(catalogAssembly, messagesByCode);
        }

        LoadDirectoryCatalogs(messagesByCode);
        return messagesByCode;
    }

    private void LoadEmbeddedCatalogs(Assembly catalogAssembly, Dictionary<string, Dictionary<string, string>> messagesByCode)
    {
        foreach (var resourceName in catalogAssembly.GetManifestResourceNames())
        {
            var language = FindEmbeddedCatalogLanguage(resourceName);
            if (language is null)
            {
                continue;
            }

            ReadCatalog(resourceName, () => catalogAssembly.GetManifestResourceStream(resourceName), language, messagesByCode);
        }
    }

    private void LoadDirectoryCatalogs(Dictionary<string, Dictionary<string, string>> messagesByCode)
    {
        if (string.IsNullOrWhiteSpace(_localizationOptions.CatalogDirectory))
        {
            return;
        }

        var catalogDirectoryPath = Path.Combine(AppContext.BaseDirectory, _localizationOptions.CatalogDirectory);
        if (!Directory.Exists(catalogDirectoryPath))
        {
            return;
        }

        foreach (var catalogFilePath in Directory.EnumerateFiles(catalogDirectoryPath, CatalogFileSearchPattern))
        {
            var language = NormalizeLanguage(Path.GetFileNameWithoutExtension(catalogFilePath));
            ReadCatalog(catalogFilePath, () => File.OpenRead(catalogFilePath), language, messagesByCode);
        }
    }

    internal static string? FindEmbeddedCatalogLanguage(string resourceName)
    {
        var segmentIndex = resourceName.LastIndexOf(EmbeddedCatalogSegment, StringComparison.Ordinal);
        var isCatalog = segmentIndex >= 0
            && resourceName.EndsWith(CatalogFileExtension, StringComparison.OrdinalIgnoreCase);
        if (!isCatalog)
        {
            return null;
        }

        var languageStart = segmentIndex + EmbeddedCatalogSegment.Length;
        var languageLength = resourceName.Length - CatalogFileExtension.Length - languageStart;
        return languageLength > 0
            ? NormalizeLanguage(resourceName.Substring(languageStart, languageLength))
            : null;
    }

    private void ReadCatalog(
        string catalogName,
        Func<Stream?> openCatalog,
        string language,
        Dictionary<string, Dictionary<string, string>> messagesByCode)
    {
        try
        {
            using var catalogStream = openCatalog()
                ?? throw new InvalidOperationException($"Error message catalog '{catalogName}' could not be opened.");
            var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(catalogStream) ?? [];

            foreach (var (responseCode, message) in messages)
            {
                if (!messagesByCode.TryGetValue(responseCode, out var messagesByLanguage))
                {
                    messagesByLanguage = new Dictionary<string, string>();
                    messagesByCode[responseCode] = messagesByLanguage;
                }

                messagesByLanguage[language] = message;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Error message catalog {CatalogName} could not be read; its messages are skipped.", catalogName);
        }
    }
}
