using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Pinqponq.Billing.RevenueCat;

/// <summary>Constant-time Authorization header check against <see cref="RevenueCatOptions.WebhookAuthHeader"/>.</summary>
public sealed class RevenueCatWebhookAuthenticator(IOptions<RevenueCatOptions> options) : IRevenueCatWebhookAuthenticator
{
    private readonly string _expected = options.Value.WebhookAuthHeader;

    public bool IsAuthorized(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(_expected) || authorizationHeader is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(authorizationHeader),
            Encoding.UTF8.GetBytes(_expected));
    }
}
