using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Pinqponq.Configuration.Vault;

/// <summary>
/// Loads one Vault KV v2 record and exposes its nested JSON as configuration keys. The record is read once, at startup.
/// </summary>
public sealed class VaultConfigurationProvider : ConfigurationProvider
{
    private const string TOKEN_HEADER = "X-Vault-Token";
    private const string NAMESPACE_HEADER = "X-Vault-Namespace";
    private const string DATA_PROPERTY = "data";

    private readonly VaultConfigurationSource _source;

    public VaultConfigurationProvider(VaultConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
    }

    /// <inheritdoc />
    public override void Load()
    {
        var record = new VaultRecordRequest(
            Address: _source.CredentialResolver.ResolveAddress(_source.Options.Address),
            Token: _source.CredentialResolver.ResolveToken(_source.Options.Token),
            Mount: GetRequiredSetting(_source.Options.Mount, nameof(VaultConfigurationOptions.Mount)),
            Path: GetRequiredSetting(_source.Options.Path, nameof(VaultConfigurationOptions.Path)));

        using var recordDocument = ReadRecordWithRetry(record);

        Data = FlattenRecord(recordDocument.RootElement);
    }

    private JsonDocument ReadRecordWithRetry(VaultRecordRequest record)
    {
        var attemptCount = 0;

        while (true)
        {
            attemptCount++;

            try
            {
                return ReadRecord(record);
            }
            catch (Exception exception) when (IsTransientFailure(exception) && attemptCount < _source.Options.MaxAttemptCount)
            {
                Thread.Sleep(_source.Options.RetryDelay);
            }
            catch (Exception exception) when (IsTransientFailure(exception))
            {
                throw new VaultConfigurationException(
                    $"Vault is unreachable at '{record.Address}'. Configuration record '{record.Location}' could not be read.",
                    exception);
            }
        }
    }

    private JsonDocument ReadRecord(VaultRecordRequest record)
    {
        using var httpClient = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, record.Url);
        request.Headers.Add(TOKEN_HEADER, record.Token.Value);
        if (!string.IsNullOrWhiteSpace(_source.Options.Namespace))
        {
            request.Headers.Add(NAMESPACE_HEADER, _source.Options.Namespace);
        }

        using var response = httpClient.Send(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new VaultConfigurationException($"Vault configuration record '{record.Location}' does not exist.");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new VaultConfigurationException(DescribeRejectedToken(record));
        }

        if (IsServerError(response.StatusCode))
        {
            throw new HttpRequestException($"Vault responded with status code {(int)response.StatusCode}.", inner: null, response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new VaultConfigurationException(
                $"Vault configuration record '{record.Location}' could not be read. Status code: {(int)response.StatusCode}.");
        }

        using var contentStream = response.Content.ReadAsStream();
        using var responseDocument = JsonDocument.Parse(contentStream);

        return ExtractRecord(responseDocument, record);
    }

    private static JsonDocument ExtractRecord(JsonDocument responseDocument, VaultRecordRequest record)
    {
        var hasRecord = responseDocument.RootElement.ValueKind == JsonValueKind.Object
            && responseDocument.RootElement.TryGetProperty(DATA_PROPERTY, out var versionedData)
            && versionedData.ValueKind == JsonValueKind.Object
            && versionedData.TryGetProperty(DATA_PROPERTY, out var recordData)
            && recordData.ValueKind == JsonValueKind.Object;
        if (!hasRecord)
        {
            throw new VaultConfigurationException(
                $"Vault configuration record '{record.Location}' is missing or malformed. The mount must be a KV version 2 secrets engine.");
        }

        var recordJson = responseDocument.RootElement.GetProperty(DATA_PROPERTY).GetProperty(DATA_PROPERTY).GetRawText();
        return JsonDocument.Parse(recordJson);
    }

    private static string DescribeRejectedToken(VaultRecordRequest record)
    {
        return record.Token.Source switch
        {
            VaultTokenSource.TokenFile =>
                $"Vault at '{record.Address}' rejected the token saved by the Vault CLI. The login has expired, was made against a different Vault server, or its policy cannot read '{record.Location}'. Log in to this server: vault login -address={record.Address}",
            VaultTokenSource.EnvironmentVariable =>
                $"Vault at '{record.Address}' rejected the token from the {VaultCredentialResolver.TOKEN_ENVIRONMENT_VARIABLE} environment variable. Check that it is valid for this server and that its policy can read '{record.Location}'.",
            _ =>
                $"Vault at '{record.Address}' rejected the configured token. Check that it is valid for this server and that its policy can read '{record.Location}'."
        };
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = _source.Options.HttpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(_source.Options.HttpMessageHandler, disposeHandler: false);
        httpClient.Timeout = _source.Options.RequestTimeout;
        return httpClient;
    }

    private static string GetRequiredSetting(string? settingValue, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(settingValue)) return settingValue.Trim('/', ' ');

        throw new VaultConfigurationException($"Vault configuration setting '{settingName}' is required.");
    }

    private static bool IsTransientFailure(Exception exception)
    {
        return exception is HttpRequestException or TaskCanceledException;
    }

    private static bool IsServerError(HttpStatusCode statusCode)
    {
        return (int)statusCode >= (int)HttpStatusCode.InternalServerError;
    }

    private static Dictionary<string, string?> FlattenRecord(JsonElement record)
    {
        var configurationValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in record.EnumerateObject())
        {
            AddElement(configurationValues, property.Name, property.Value);
        }

        return configurationValues;
    }

    private static void AddElement(Dictionary<string, string?> configurationValues, string configurationKey, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddElement(configurationValues, ConfigurationPath.Combine(configurationKey, property.Name), property.Value);
                }
                break;
            case JsonValueKind.Array:
                var elementIndex = 0;
                foreach (var arrayElement in element.EnumerateArray())
                {
                    var elementKey = ConfigurationPath.Combine(configurationKey, elementIndex.ToString(CultureInfo.InvariantCulture));
                    AddElement(configurationValues, elementKey, arrayElement);
                    elementIndex++;
                }
                break;
            case JsonValueKind.Null:
                configurationValues[configurationKey] = null;
                break;
            case JsonValueKind.String:
                configurationValues[configurationKey] = element.GetString();
                break;
            default:
                configurationValues[configurationKey] = element.GetRawText();
                break;
        }
    }

    private readonly record struct VaultRecordRequest(string Address, VaultToken Token, string Mount, string Path)
    {
        public string Location => $"{Mount}/{Path}";

        public string Url => $"{Address.TrimEnd('/')}/v1/{Mount}/{DATA_PROPERTY}/{Path}";
    }
}
