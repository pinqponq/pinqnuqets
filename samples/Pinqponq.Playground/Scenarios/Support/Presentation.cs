using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pinqponq.Playground.Scenarios.Support;

/// <summary>Display projections. Several framework types cannot be serialised directly.</summary>
public static class Presentation
{
    /// <summary>
    /// Flattens a principal into something serialisable — <see cref="ClaimsPrincipal"/>
    /// itself has a Claim → ClaimsIdentity → Claim cycle that System.Text.Json rejects.
    /// </summary>
    public static object Principal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new
        {
            isAuthenticated = principal.Identity?.IsAuthenticated ?? false,
            authenticationType = principal.Identity?.AuthenticationType,
            name = principal.Identity?.Name,
            claims = principal.Claims
                .Select(claim => new { type = claim.Type, value = claim.Value, issuer = claim.Issuer })
                .ToArray(),
        };
    }

    /// <summary>Decodes a JWT's header and payload for display. Does not validate anything.</summary>
    public static object Jwt(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return new { error = "Token is not three-part." };
        }

        return new
        {
            header = DecodeSegment(parts[0]),
            payload = DecodeSegment(parts[1]),
            signatureLength = parts.Length > 2 ? parts[2].Length : 0,
        };
    }

    private static JsonNode? DecodeSegment(string segment)
    {
        try
        {
            var padded = segment.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return JsonNode.Parse(json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return JsonValue.Create(segment);
        }
    }
}
