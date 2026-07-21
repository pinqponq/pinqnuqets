# Pinqponq.Identity

JWT üretimi/doğrulaması, refresh token issue/rotate/revoke ve parola hash/verify
gibi **jenerik kimlik doğrulama primitiflerini** tek pakette toplar. Proje-özel
mantık içermez; 5 backend'de tekrarlanan bu altyapı ortak pakete taşınmıştır
(Linear PIN-389).

- **JWT** — `JsonWebTokenHandler` üzerinden üretim + doğrulama, **HMAC** ve **RSA** imza
- **Refresh token** — kripto-güvenli üretim, issue / rotate / revoke, depoya yalnızca **hash** yazılır
- **Parola** — ASP.NET Core PBKDF2 `PasswordHasher` sarmalayıcısı (versiyonlanmış format, rehash sinyali)

Hedef: `net8.0` ve `net9.0`.

## Kurulum

```bash
dotnet add package Pinqponq.Identity
```

## DI ile kayıt

```csharp
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.Jwt;

builder.Services.AddPinqponqIdentity(
    jwt =>
    {
        jwt.Issuer = "pinqponq";
        jwt.Audience = "pinqponq-clients";
        jwt.Lifetime = TimeSpan.FromMinutes(15);

        // HMAC (simetrik) — en az 32 byte:
        jwt.Algorithm = JwtSigningAlgorithm.HmacSha256;
        jwt.SymmetricKey = builder.Configuration["Jwt:SymmetricKey"];

        // veya RSA (asimetrik):
        // jwt.Algorithm = JwtSigningAlgorithm.RsaSha256;
        // jwt.RsaPrivateKeyPem = File.ReadAllText("private.pem"); // imzalama
        // jwt.RsaPublicKeyPem  = File.ReadAllText("public.pem");  // doğrulama
    },
    refresh =>
    {
        refresh.Lifetime = TimeSpan.FromDays(14);
    });

// Refresh token DEPOSU uygulamaya özeldir — kendiniz kaydedersiniz:
builder.Services.AddScoped<IRefreshTokenStore, MyEfRefreshTokenStore>();
```

> `IRefreshTokenStore` bilinçli olarak pakete dahil edilmedi; EF Core / Dapper /
> Redis gibi kalıcı depolamayı tüketici uygulama sağlar.

## JWT üret & doğrula

```csharp
public sealed class TokenEndpoints(IJwtTokenGenerator generator, IJwtTokenValidator validator)
{
    public string Login(string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, "user"),
        };
        return generator.GenerateToken(claims);
    }

    public async Task<bool> IsValid(string token)
    {
        ClaimsPrincipal? principal = await validator.ValidateAsync(token);
        return principal is not null; // geçersiz/expired token'da null döner
    }
}
```

## Parola hash & verify

```csharp
public sealed class PasswordService(IPasswordHasher hasher)
{
    public string Register(string plaintext) => hasher.Hash(plaintext);

    public bool SignIn(string storedHash, string plaintext)
    {
        var outcome = hasher.Verify(storedHash, plaintext);
        return outcome is PasswordVerificationOutcome.Success
            or PasswordVerificationOutcome.SuccessRehashNeeded;
        // SuccessRehashNeeded → başarılı girişten sonra hash'i yenileyip tekrar saklayın.
    }
}
```

## Refresh token: issue / rotate / revoke

```csharp
public sealed class SessionService(IRefreshTokenService refreshTokens)
{
    // Ham token yalnızca burada bir kez elde edilir; istemciye onu döndürün.
    public async Task<string> Issue(string userId)
    {
        var result = await refreshTokens.IssueAsync(userId);
        return result.Token;
    }

    // Eski token revoke edilir, yerine yenisi verilir (reuse-detection zinciri kurulur).
    public async Task<string> Refresh(string presentedToken)
    {
        var rotated = await refreshTokens.RotateAsync(presentedToken);
        return rotated.Token;
    }

    public Task Logout(string presentedToken) => refreshTokens.RevokeAsync(presentedToken);
}
```

Geçersiz/expired/revoke edilmiş token'da `RotateAsync` ve `RevokeAsync`,
`InvalidRefreshTokenException` fırlatır.

## Güvenlik notları

- Refresh token'ın **ham değeri asla saklanmaz**; depoya yalnızca SHA-256 hash'i yazılır.
- `RotateAsync` eski kaydı `RevokedAt` + `ReplacedByTokenHash` ile işaretler; bu zincir
  token yeniden kullanımını (reuse) tespit etmek için kullanılabilir.
- HMAC anahtarı en az 32 byte (256 bit) olmalıdır.

## Derleme & test

```bash
dotnet build -c Release
dotnet test
dotnet pack -c Release   # Pinqponq.Identity.<versiyon>.nupkg üretir
```
