using Pinqponq.Auth.Sso.Abstractions;
using Pinqponq.Auth.Sso.Google;
using Pinqponq.Auth.Sso.Google.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for the SSO packages. The Google provider can only succeed against a real
/// Google-issued id_token, so the offline scenarios pin down its documented failure modes
/// and the success path is opt-in with a token you supply.
/// </summary>
public static class SsoScenarios
{
    private const string AbstractionsPackage = "Pinqponq.Auth.Sso.Abstractions";
    private const string GooglePackage = "Pinqponq.Auth.Sso.Google";

    public static IEnumerable<Scenario> Create()
    {
        yield return ContractShape();
        yield return CodeFlowUnsupported();
        yield return MalformedToken();
        yield return BringYourOwnToken();
    }

    private static Scenario ContractShape() => new(
        new ScenarioDescriptor
        {
            Id = "sso.abstractions.contract",
            PackageId = AbstractionsPackage,
            Title = "Contract types",
            Summary = "Shows the dependency-free contract package's factory methods and result "
                      + "types: FromIdToken / FromAuthorizationCode, Success / Failure.",
        },
        context =>
        {
            var fromToken = ExternalAuthRequest.FromIdToken("id-token-abc", "nonce-1");
            context.Require("FromIdToken carries the IdToken", fromToken.IdToken == "id-token-abc");
            context.Check("Nonce is carried", fromToken.Nonce == "nonce-1");

            var fromCode = ExternalAuthRequest.FromAuthorizationCode("code-xyz", "https://app/callback");
            context.Require("FromAuthorizationCode carries its fields",
                fromCode.AuthorizationCode == "code-xyz" && fromCode.RedirectUri == "https://app/callback");

            var failure = ExternalAuthResult.Failure("failed");
            context.Require("Failure is unsuccessful and has no user", !failure.Succeeded && failure.User is null);

            var user = new ExternalUserInfo
            {
                Subject = "1234",
                Provider = "Google",
                Email = "user@pinqponq.dev",
                EmailVerified = true,
                Name = "Test User",
            };
            var success = ExternalAuthResult.Success(user);
            context.Require("Success carries the user", success.Succeeded && success.User?.Subject == "1234");

            context.Artifact("requests", new { idToken = fromToken, authorizationCode = fromCode });
            context.Artifact("results", new
            {
                success = new { success.Succeeded, success.User?.Email },
                failure = new { failure.Succeeded, failure.Error },
            });

            return Task.CompletedTask;
        });

    private static Scenario CodeFlowUnsupported() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.code-flow",
            PackageId = GooglePackage,
            Title = "Authorization-code flow is not supported",
            Summary = "The package only validates id_token. When called with the code flow, it "
                      + "doesn't throw — it returns a failure result with an explanatory error message.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services =>
                services.AddPinqponqGoogleSso(google => google.ClientIds.Add("playground.apps.googleusercontent.com")));

            var provider = host.GetRequiredService<IExternalAuthProvider>();
            context.Check("Provider name is Google", provider.ProviderName == GoogleAuthProvider.Name);

            var result = await provider.AuthenticateAsync(
                ExternalAuthRequest.FromAuthorizationCode("dummy-code", "https://app/callback"),
                context.CancellationToken);

            context.Require("Result is unsuccessful", !result.Succeeded);
            context.Require(
                "The error says id_token is required",
                result.Error?.Contains("id_token", StringComparison.OrdinalIgnoreCase) == true,
                result.Error);
            context.Artifact("result", new { result.Succeeded, result.Error });
        });

    private static Scenario MalformedToken() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.malformed-token",
            PackageId = GooglePackage,
            Title = "A malformed id_token is rejected",
            Summary = "A value that isn't JWT-shaped is submitted. The Google library's "
                      + "InvalidJwtException is caught inside the package and turned into an error message.",
            NegativePath = true,
            Fields = [new ScenarioField("idToken", "id_token", ScenarioFieldKind.Text, "this-is-not-a-jwt")],
        },
        async context =>
        {
            await using var host = context.Host(services =>
                services.AddPinqponqGoogleSso(google => google.ClientIds.Add("playground.apps.googleusercontent.com")));

            var provider = host.GetRequiredService<IExternalAuthProvider>();

            ExternalAuthResult result;
            try
            {
                result = await provider.AuthenticateAsync(
                    ExternalAuthRequest.FromIdToken(context.Input.Text("idToken")),
                    context.CancellationToken);
            }
            catch (HttpRequestException exception)
            {
                // A structurally valid but fake token makes the library fetch Google's signing
                // certificates; without egress that is an environment limit, not a defect.
                ScenarioContext.Skip($"Could not reach Google's certificates: {exception.Message}");
                return;
            }

            context.Require("Result is unsuccessful", !result.Succeeded);
            context.Require(
                "The error says the id_token is invalid",
                result.Error?.Contains("Invalid Google id_token", StringComparison.Ordinal) == true,
                result.Error);
            context.Artifact("result", new { result.Succeeded, result.Error });
        });

    private static Scenario BringYourOwnToken() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.real-token",
            PackageId = GooglePackage,
            Title = "Validate with a real id_token",
            Summary = "Paste your own Google id_token. You can get one from the Google OAuth "
                      + "Playground or the 'credential' field returned by Google Identity Services. "
                      + "Requires access to Google's signing certificates.",
            NeedsInternet = true,
            Fields =
            [
                new ScenarioField("idToken", "id_token", ScenarioFieldKind.MultilineText, null,
                    "A three-part token starting with eyJhbGciOi…", Required: true),
                new ScenarioField("clientId", "Accepted Client ID", ScenarioFieldKind.Text, null,
                    "Google OAuth client id — the validator requires at least one.", Required: true),
            ],
        },
        async context =>
        {
            var idToken = context.Input.TextOrNull("idToken");
            if (string.IsNullOrWhiteSpace(idToken))
            {
                ScenarioContext.Skip("This scenario requires a real Google id_token.");
                return;
            }

            var clientId = context.Input.TextOrNull("clientId");
            if (string.IsNullOrWhiteSpace(clientId))
            {
                ScenarioContext.Skip("Enter a Client ID for GoogleAuthOptions.ClientIds.");
                return;
            }

            await using var host = context.Host(services => services.AddPinqponqGoogleSso(google =>
            {
                google.ClientIds.Add(clientId);
                google.RequireEmailVerified = true;
            }));

            ExternalAuthResult result;
            try
            {
                result = await host.GetRequiredService<IExternalAuthProvider>()
                    .AuthenticateAsync(ExternalAuthRequest.FromIdToken(idToken), context.CancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ScenarioContext.Skip($"Could not reach Google's certificates: {exception.Message}");
                return;
            }

            context.Step("Validation complete");
            context.Artifact("result", new { result.Succeeded, result.Error, user = result.User });
            context.Require("Token validated", result.Succeeded, result.Error);
        });
}
