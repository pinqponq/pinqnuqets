using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Pinqponq.ErrorHandling;

/// <summary>
/// Configuration for exceptions whose message is the response code, for example
/// <c>throw new InvalidOperationException(ErrorCodes.UserNotFound)</c>. Set by <c>AddPinqponqResponseCodeExceptions</c>.
/// </summary>
public sealed class ResponseCodeExceptionOptions
{
    private readonly HashSet<string> _responseCodes = new(StringComparer.Ordinal);

    private readonly Dictionary<Type, int> _statusCodes = new()
    {
        [typeof(UnauthorizedAccessException)] = StatusCodes.Status401Unauthorized,
        [typeof(KeyNotFoundException)] = StatusCodes.Status404NotFound,
        [typeof(ArgumentException)] = StatusCodes.Status400BadRequest,
        [typeof(FormatException)] = StatusCodes.Status400BadRequest,
        [typeof(InvalidOperationException)] = StatusCodes.Status400BadRequest,
    };

    /// <summary>How a mapped exception whose message is not a registered response code is answered. Defaults to <see cref="UnknownMessageBehavior.AnswerInternalError"/>.</summary>
    public UnknownMessageBehavior UnknownMessageBehavior { get; set; } = UnknownMessageBehavior.AnswerInternalError;

    internal IReadOnlySet<string> ResponseCodes => _responseCodes;

    internal IReadOnlyDictionary<Type, int> StatusCodesByExceptionType => _statusCodes;

    /// <summary>Registers the values of the <c>public const string</c> fields of <paramref name="responseCodesType"/> as response codes.</summary>
    /// <exception cref="ArgumentException">The type has no <c>public const string</c> field.</exception>
    public ResponseCodeExceptionOptions AddResponseCodes(Type responseCodesType)
    {
        ArgumentNullException.ThrowIfNull(responseCodesType);

        var responseCodes = responseCodesType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        if (responseCodes.Count == 0)
        {
            throw new ArgumentException($"{responseCodesType.Name} has no public const string field.", nameof(responseCodesType));
        }

        return AddResponseCodes(responseCodes);
    }

    /// <summary>Registers <paramref name="responseCodes"/> as response codes.</summary>
    /// <exception cref="ArgumentException">A code is empty or white space.</exception>
    public ResponseCodeExceptionOptions AddResponseCodes(IEnumerable<string> responseCodes)
    {
        ArgumentNullException.ThrowIfNull(responseCodes);

        foreach (var responseCode in responseCodes)
        {
            if (string.IsNullOrWhiteSpace(responseCode))
            {
                throw new ArgumentException("A response code must not be empty.", nameof(responseCodes));
            }

            _responseCodes.Add(responseCode);
        }

        return this;
    }

    /// <summary>
    /// Answers <typeparamref name="TException"/> and its subtypes with <paramref name="statusCode"/>, replacing an earlier
    /// mapping of the same type. The mapping of the most specific type in the exception's type hierarchy is used.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is not between 400 and 599.</exception>
    public ResponseCodeExceptionOptions Map<TException>(int statusCode)
        where TException : Exception
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, StatusCodes.Status400BadRequest);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);

        _statusCodes[typeof(TException)] = statusCode;
        return this;
    }

    /// <summary>Removes the mapping of <typeparamref name="TException"/>, so the next resolver or the built-in mapping answers it.</summary>
    public ResponseCodeExceptionOptions RemoveMapping<TException>()
        where TException : Exception
    {
        _statusCodes.Remove(typeof(TException));
        return this;
    }
}
