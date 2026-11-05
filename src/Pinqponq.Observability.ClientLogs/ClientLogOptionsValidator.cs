using Microsoft.Extensions.Options;

namespace Pinqponq.Observability.ClientLogs;

internal sealed class ClientLogOptionsValidator : IValidateOptions<ClientLogOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientLogOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CollectionName))
        {
            failures.Add($"{nameof(ClientLogOptions)}.{nameof(ClientLogOptions.CollectionName)} is required.");
        }

        AddFailureWhenNotPositive(failures, nameof(ClientLogOptions.MaxEntriesPerBatch), options.MaxEntriesPerBatch);
        AddFailureWhenNotPositive(failures, nameof(ClientLogOptions.MaxBodyBytes), options.MaxBodyBytes);
        AddFailureWhenNotPositive(failures, nameof(ClientLogOptions.MaxFieldsPerEntry), options.MaxFieldsPerEntry);
        AddFailureWhenNotPositive(failures, nameof(ClientLogOptions.MaxValueLength), options.MaxValueLength);

        // The body reader buffers one byte past the limit to detect a longer body.
        if (options.MaxBodyBytes >= Array.MaxLength)
        {
            failures.Add($"{nameof(ClientLogOptions)}.{nameof(ClientLogOptions.MaxBodyBytes)} must be less than {Array.MaxLength}.");
        }

        // An empty part is contained in every key, so it would redact every field.
        if (options.RedactedKeyParts.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{nameof(ClientLogOptions)}.{nameof(ClientLogOptions.RedactedKeyParts)} must not contain an empty entry.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void AddFailureWhenNotPositive(List<string> failures, string optionName, int? limit)
    {
        if (limit <= 0)
        {
            failures.Add($"{nameof(ClientLogOptions)}.{optionName} must be greater than 0 when it is set.");
        }
    }
}
