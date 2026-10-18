using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3;

/// <summary>Validates <see cref="ObjectStorageOptions"/> at startup.</summary>
internal sealed class ObjectStorageOptionsValidator : IValidateOptions<ObjectStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, ObjectStorageOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            failures.Add($"{nameof(ObjectStorageOptions.BucketName)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            failures.Add($"{nameof(ObjectStorageOptions.AccessKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add($"{nameof(ObjectStorageOptions.SecretKey)} is required.");
        }

        var hasServiceUrl = !string.IsNullOrWhiteSpace(options.ServiceUrl);
        var hasRegion = !string.IsNullOrWhiteSpace(options.Region);

        if (hasServiceUrl == hasRegion)
        {
            failures.Add(
                $"Set exactly one of {nameof(ObjectStorageOptions.ServiceUrl)} (S3-compatible store) " +
                $"or {nameof(ObjectStorageOptions.Region)} (AWS S3).");
        }

        if (hasServiceUrl && !IsHttpUrl(options.ServiceUrl!))
        {
            failures.Add($"{nameof(ObjectStorageOptions.ServiceUrl)} must be an absolute http or https URL.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpUrl(string serviceUrl) =>
        Uri.TryCreate(serviceUrl, UriKind.Absolute, out var serviceUri)
        && (serviceUri.Scheme == Uri.UriSchemeHttp || serviceUri.Scheme == Uri.UriSchemeHttps);
}
