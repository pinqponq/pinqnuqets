using System.Globalization;
using Microsoft.Extensions.Options;

namespace Pinqponq.Identity.Otp;

/// <summary>
/// Default <see cref="IOtpMessageComposer"/>: fills <see cref="OtpOptions.EmailSubjectTemplate"/>
/// and <see cref="OtpOptions.EmailBodyTemplate"/> with the code.
/// </summary>
public sealed class TemplateOtpMessageComposer : IOtpMessageComposer
{
    private readonly OtpOptions _otpOptions;

    /// <summary>Creates the composer from the OTP options that hold the templates.</summary>
    public TemplateOtpMessageComposer(IOptions<OtpOptions> otpOptions)
    {
        ArgumentNullException.ThrowIfNull(otpOptions);
        _otpOptions = otpOptions.Value;
    }

    /// <inheritdoc />
    public Task<OtpEmailContent> ComposeEmailAsync(OtpMessageContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subject = Format(_otpOptions.EmailSubjectTemplate, context.Code);
        var body = Format(_otpOptions.EmailBodyTemplate, context.Code);
        return Task.FromResult(new OtpEmailContent { Subject = subject, Body = body });
    }

    private static string Format(string template, string code) =>
        string.Format(CultureInfo.InvariantCulture, template, code);
}
