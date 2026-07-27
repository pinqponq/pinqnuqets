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
            Title = "Sözleşme tipleri",
            Summary = "Bağımlılığı olmayan sözleşme paketinin fabrika metotlarını ve sonuç "
                      + "tiplerini gösterir: FromIdToken / FromAuthorizationCode, Success / Failure.",
        },
        context =>
        {
            var fromToken = ExternalAuthRequest.FromIdToken("id-token-abc", "nonce-1");
            context.Require("FromIdToken IdToken'ı taşıyor", fromToken.IdToken == "id-token-abc");
            context.Check("Nonce taşınıyor", fromToken.Nonce == "nonce-1");

            var fromCode = ExternalAuthRequest.FromAuthorizationCode("code-xyz", "https://app/callback");
            context.Require("FromAuthorizationCode alanları taşıyor",
                fromCode.AuthorizationCode == "code-xyz" && fromCode.RedirectUri == "https://app/callback");

            var failure = ExternalAuthResult.Failure("olmadı");
            context.Require("Failure başarısız ve kullanıcısız", !failure.Succeeded && failure.User is null);

            var user = new ExternalUserInfo
            {
                Subject = "1234",
                Provider = "Google",
                Email = "user@pinqponq.dev",
                EmailVerified = true,
                Name = "Test Kullanıcı",
            };
            var success = ExternalAuthResult.Success(user);
            context.Require("Success kullanıcıyı taşıyor", success.Succeeded && success.User?.Subject == "1234");

            context.Artifact("istekler", new { idToken = fromToken, authorizationCode = fromCode });
            context.Artifact("sonuçlar", new
            {
                basarili = new { success.Succeeded, success.User?.Email },
                basarisiz = new { failure.Succeeded, failure.Error },
            });

            return Task.CompletedTask;
        });

    private static Scenario CodeFlowUnsupported() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.code-flow",
            PackageId = GooglePackage,
            Title = "Authorization-code akışı desteklenmiyor",
            Summary = "Paket yalnızca id_token doğrular. Kod akışıyla çağrıldığında exception "
                      + "fırlatmaz, açıklayıcı bir hata mesajıyla başarısız sonuç döner.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services =>
                services.AddPinqponqGoogleSso(google => google.ClientIds.Add("playground.apps.googleusercontent.com")));

            var provider = host.GetRequiredService<IExternalAuthProvider>();
            context.Check("Sağlayıcı adı Google", provider.ProviderName == GoogleAuthProvider.Name);

            var result = await provider.AuthenticateAsync(
                ExternalAuthRequest.FromAuthorizationCode("dummy-code", "https://app/callback"),
                context.CancellationToken);

            context.Require("Sonuç başarısız", !result.Succeeded);
            context.Require(
                "Hata id_token gerektiğini söylüyor",
                result.Error?.Contains("id_token", StringComparison.OrdinalIgnoreCase) == true,
                result.Error);
            context.Artifact("sonuç", new { result.Succeeded, result.Error });
        });

    private static Scenario MalformedToken() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.malformed-token",
            PackageId = GooglePackage,
            Title = "Bozuk id_token reddedilir",
            Summary = "JWT biçiminde olmayan bir değer gönderilir. Google kütüphanesinin "
                      + "InvalidJwtException'ı paket içinde yakalanır ve hata mesajına dönüşür.",
            NegativePath = true,
            Fields = [new ScenarioField("idToken", "id_token", ScenarioFieldKind.Text, "bu-bir-jwt-degil")],
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
                ScenarioContext.Skip($"Google sertifikalarına erişilemedi: {exception.Message}");
                return;
            }

            context.Require("Sonuç başarısız", !result.Succeeded);
            context.Require(
                "Hata geçersiz id_token diyor",
                result.Error?.Contains("Invalid Google id_token", StringComparison.Ordinal) == true,
                result.Error);
            context.Artifact("sonuç", new { result.Succeeded, result.Error });
        });

    private static Scenario BringYourOwnToken() => new(
        new ScenarioDescriptor
        {
            Id = "sso.google.real-token",
            PackageId = GooglePackage,
            Title = "Gerçek id_token ile doğrulama",
            Summary = "Kendi Google id_token'ınızı yapıştırın. Google OAuth Playground'dan veya "
                      + "Google Identity Services'in döndürdüğü 'credential' alanından alabilirsiniz. "
                      + "Google'ın imza sertifikalarına erişim gerektirir.",
            NeedsInternet = true,
            Fields =
            [
                new ScenarioField("idToken", "id_token", ScenarioFieldKind.MultilineText, null,
                    "eyJhbGciOi… ile başlayan üç bölümlü token.", Required: true),
                new ScenarioField("clientId", "Kabul edilen Client ID", ScenarioFieldKind.Text, null,
                    "Boş bırakılırsa audience doğrulaması yapılmaz."),
            ],
        },
        async context =>
        {
            var idToken = context.Input.TextOrNull("idToken");
            if (string.IsNullOrWhiteSpace(idToken))
            {
                ScenarioContext.Skip("Bu senaryo gerçek bir Google id_token'ı gerektirir.");
                return;
            }

            var clientId = context.Input.TextOrNull("clientId");

            await using var host = context.Host(services => services.AddPinqponqGoogleSso(google =>
            {
                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    google.ClientIds.Add(clientId);
                }
            }));

            ExternalAuthResult result;
            try
            {
                result = await host.GetRequiredService<IExternalAuthProvider>()
                    .AuthenticateAsync(ExternalAuthRequest.FromIdToken(idToken), context.CancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ScenarioContext.Skip($"Google sertifikalarına erişilemedi: {exception.Message}");
                return;
            }

            context.Step("Doğrulama tamamlandı");
            context.Artifact("sonuç", new { result.Succeeded, result.Error, user = result.User });
            context.Require("Token doğrulandı", result.Succeeded, result.Error);
        });
}
