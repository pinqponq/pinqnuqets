namespace Pinqponq.LiveKit.Server.Auth;

internal sealed class InvalidAccessTokenException : Exception
{
    public InvalidAccessTokenException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
