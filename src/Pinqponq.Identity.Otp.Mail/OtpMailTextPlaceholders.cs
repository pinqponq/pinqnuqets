namespace Pinqponq.Identity.Otp.Mail;

/// <summary>The placeholders that <see cref="OtpMailTexts"/> values may contain.</summary>
public static class OtpMailTextPlaceholders
{
    /// <summary>Replaced with <see cref="OtpMailOptions.ProductName"/>.</summary>
    public const string ProductName = "{ProductName}";

    /// <summary>Replaced with the code.</summary>
    public const string Code = "{Code}";

    /// <summary>Replaced with the code lifetime in whole minutes, rounded up.</summary>
    public const string Minutes = "{Minutes}";
}
