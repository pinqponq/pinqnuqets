namespace Pinqponq.ErrorHandling;

/// <summary>
/// How <c>AddPinqponqResponseCodeExceptions</c> answers a mapped exception whose message is not a registered response code.
/// </summary>
public enum UnknownMessageBehavior
{
    /// <summary>Answers 500 with <c>general_error</c>, so the message never reaches the client as a code.</summary>
    AnswerInternalError,

    /// <summary>
    /// Answers the mapped status with the message as the response code. With error localization on, a message the
    /// catalog does not know becomes <see cref="ErrorLocalizationOptions.FallbackResponseCode"/>.
    /// </summary>
    PassMessageThrough,
}
