using Microsoft.Extensions.Options;

namespace Pinqponq.ErrorHandling;

internal sealed class ResponseCodeExceptionOptionsValidator : IValidateOptions<ResponseCodeExceptionOptions>
{
    public ValidateOptionsResult Validate(string? name, ResponseCodeExceptionOptions options)
    {
        if (options.UnknownMessageBehavior == UnknownMessageBehavior.AnswerInternalError && options.ResponseCodes.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                $"No response code is registered: call {nameof(ResponseCodeExceptionOptions.AddResponseCodes)}, otherwise every mapped exception is answered 500.");
        }

        return ValidateOptionsResult.Success;
    }
}
