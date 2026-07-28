# PinqNugets — Pinqponq ortak altyapı paketleri

[![CI](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml/badge.svg)](https://github.com/pinqponq/pinqnuqets/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

12 backend'de tekrarlanan **jenerik altyapı** kesitlerini (cache, SMS, mail, DB
bağlantısı, mesajlaşma, kimlik doğrulama, hata yönetimi) tek monorepo'da
`Pinqponq.*` NuGet paketlerine taşır. Amaç: **sürüm dağınıklığını** bitirmek,
aynı bug'ların tekrarını önlemek ve isim/sözleşme dağınıklığını (ör.
`ISmsService` vs `IGSMService`) tek standart arayüzle gidermek (Linear PIN-381).

Paketler **davranışı sabit, dış bağımlılığı sarmalayan** koddan oluşur;
proje-özel iş mantığı (domain repository, mesaj contract'ları, OTP-rol kuralları)
**paketlenmez**.

- Hedef: `net8.0`, `net9.0`, `net10.0` — .NET 8 ve sonrası. Liste tek noktada
  (`Directory.Build.props` içindeki `PinqponqTargetFrameworks`) tanımlıdır.
- Bağımlılık sürümleri **Central Package Management** (`Directory.Packages.props`)
  ile tek merkezden yönetilir.
- Lisans: [MIT](LICENSE)

## Paketler

| Paket | İşlev |
|---|---|
| **Pinqponq.Identity** | JWT üretimi/doğrulaması (HMAC+RSA), refresh token issue/rotate/revoke, PBKDF2 parola hash/verify |
| **Pinqponq.Identity.Otp** | OTP üret/gönder/doğrula; mail/sms kanal routing; saklama arayüz (`IOtpStore`) |
| **Pinqponq.Auth.Totp** | RFC 6238 TOTP 2FA; `otpauth://` provisioning URI |
| **Pinqponq.Auth.Sso.Abstractions** | `IExternalAuthProvider` sözleşmesi |
| **Pinqponq.Auth.Sso.Google** | Google id_token doğrulaması (`Google.Apis.Auth`) |
| **Pinqponq.Cache** | Redis: get/set/remove/exists, dağıtık kilit, health-check |
| **Pinqponq.Sms** | NetGSM SMS gönderimi (`ISmsSender`) + retry |
| **Pinqponq.Mail** | SMTP mail gönderimi (`IEmailSender`, System.Net.Mail) |
| **Pinqponq.Database.Postgres/Mongo/Mssql** | Bağlantı + (Postgres/Mssql) retry + health-check (repository/entity hariç) |
| **Pinqponq.Messaging.RabbitMq** | Publish (confirms + mandatory) / consume, reconnect, DLX veya MaxRedelivery |
| **Pinqponq.ErrorHandling** | Global exception middleware + standart hata contract + Pinqloq-uyumlu yapılandırılmış log |

Her paket bir `AddPinqponqXxx(...)` DI uzantısı ve bir `XxxOptions` sınıfı sunar.

## Hızlı kullanım

### Identity — JWT, refresh token, parola
```csharp
builder.Services.AddPinqponqIdentity(jwt =>
{
    jwt.Issuer = "pinqponq";
    jwt.Audience = "pinqponq-clients";
    jwt.Algorithm = JwtSigningAlgorithm.HmacSha256;
    jwt.SymmetricKey = builder.Configuration["Jwt:SymmetricKey"]; // >= 32 byte
});
builder.Services.AddScoped<IRefreshTokenStore, MyRefreshTokenStore>(); // depolama uygulamaya ait
```
`IJwtTokenGenerator` (singleton) / `IJwtTokenValidator` (scoped — revocation store ile
aynı lifetime), `IRefreshTokenService`, `IPasswordHasher`. Refresh token'ın yalnızca
**hash**'i saklanır; `RotateAsync`
`TryRevokeActiveAsync` + `CompleteRotationAsync` (add+link atomik) kullanır.
Rotate edilmiş bir token, `ReuseDetectionGrace` (default 5s) **sonrasında**
yeniden sunulursa `RevokeAllForSubjectAsync` ile subject ailesi iptal edilir
(grace içinde concurrent double-submit family revoke tetiklemez).
Access token'lara otomatik `jti` eklenir; logout için isteğe bağlı store:

```csharp
builder.Services.AddScoped<IAccessTokenRevocationStore, MyJtiStore>();
// IAccessTokenRevocationService.RevokeAccessTokenAsync(token) → jti'yi store'a yazar
// JwtTokenValidator store kayıtlıysa revoked jti için null döner
```

Yalnızca JWT ve parola özetleme kullanan uygulamalar `IRefreshTokenStore`
kaydetmeyebilir; eksik store `IRefreshTokenService` ilk çözümlendiğinde bildirilir.

### Cache — Redis
```csharp
builder.Services.AddPinqponqCache(o => o.ConnectionString = "localhost:6379");
builder.Services.AddHealthChecks().AddPinqponqRedis();

await cache.SetAsync("k", myObj, TimeSpan.FromMinutes(5));
await using var handle = await distributedLock.AcquireAsync(
    "resource",
    TimeSpan.FromSeconds(30),
    new DistributedLockAcquireOptions
    {
        IssueFencingToken = true,
        RenewInterval = TimeSpan.FromSeconds(10), // watchdog; null = kapalı
    });
if (handle.Acquired)
{
    // handle.FencingToken — DB tarafında enforce uygulamaya aittir
    await handle.TryExtendAsync(TimeSpan.FromSeconds(30));
}
```

### Sms / Mail
```csharp
builder.Services.AddPinqponqSms(o =>
{
    // Legacy GET (default):
    // o.Transport = SmsTransport.GetQuery;
    // o.ApiUrl = "https://api.netgsm.com.tr/sms/send/get/";
    // o.Password = "..."; // GET query'de gider — URL loglamayın

    o.Transport = SmsTransport.RestV2; // POST + Basic Auth; ApiUrl boşsa default HTTPS
    o.UserCode = "...";
    o.Password = "...";
    o.MsgHeader = "PINQ";
    // o.AllowNoOp = true; // yalnızca GetQuery + local; default false
});
builder.Services.AddPinqponqMail(o =>
{
    o.SmtpHost = "localhost";
    o.SmtpPort = 1025;
    o.FromEmail = "noreply@example.com";
    o.AttachmentRoot = @"D:\secure-attachments"; // ek gönderirken zorunlu
});

await sms.SendAsync(new SmsMessage { To = "+90555...", Text = "..." });
await mail.SendAsync(new EmailMessage { To = "a@b.com", Subject = "...", Body = "..." });
```

### OTP — mail/sms routing
```csharp
builder.Services.AddPinqponqSms(...);
builder.Services.AddPinqponqMail(...);
builder.Services.AddPinqponqOtp(o =>
{
    o.Ttl = TimeSpan.FromMinutes(3);
    o.MinSendInterval = TimeSpan.FromSeconds(30); // IOtpSendRateLimiter'a geçer
    o.HashPepper = builder.Configuration["Otp:HashPepper"]!; // >= 32 karakter
});
builder.Services.AddScoped<IOtpStore, MyOtpStore>(); // TryConsumeAsync + TryRemoveAsync atomik olmalı
// Default limiter no-op; Redis vb. ile değiştirin:
// builder.Services.AddSingleton<IOtpSendRateLimiter, RedisOtpSendRateLimiter>();

await otp.GenerateAndSendAsync("user@example.com");           // Auto → email
var status = await otp.VerifyAsync("user@example.com", code); // OtpVerifyStatus.Success
```
Gönderici yalnızca kullanılan kanal için gerekir: sadece e-posta gönderen bir
uygulama `AddPinqponqSms` çağırmak zorunda değildir.

### TOTP 2FA
```csharp
builder.Services.AddPinqponqTotp(o => o.Issuer = "Pinqponq"); // ITotpService scoped
builder.Services.AddScoped<ITotpReplayStore, MyTotpReplayStore>(); // ctor bağımlılığı
var secret = totp.GenerateSecret();
var uri = totp.GetProvisioningUri(secret, "user@example.com"); // QR → Authenticator
bool ok = await totp.ValidateAsync(secret, userCode, subjectKey: userId);
// Sync Validate hâlâ mevcut (replay yok); production'da ValidateAsync tercih edin.
```

### Google SSO
```csharp
builder.Services.AddPinqponqGoogleSso(o =>
{
    o.ClientIds.Add(builder.Configuration["Google:ClientId"]!);
    o.RequireEmailVerified = true; // default
    // o.RequireNonce = true; // browser OIDC için
    // o.HostedDomain = "example.com";
});

var result = await provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken(idToken));
if (result.Succeeded) { var email = result.User!.Email; }
// Email ile otomatik account-link yapmadan önce Google otoritesini (hd / gmail) değerlendirin.
```

### Database (Postgres/Mongo/Mssql)
```csharp
builder.Services.AddPinqponqPostgres(o => o.ConnectionString = cs);
builder.Services.AddHealthChecks().AddPinqponqPostgres(); // shared NpgsqlDataSource üzerinden
await using var conn = await connectionFactory.OpenConnectionAsync(); // Postgres/Mssql: retry
```

### RabbitMQ
```csharp
builder.Services.AddPinqponqRabbitMq(o =>
{
    o.HostName = "rabbit";
    o.UserName = "guest";
    o.Password = "guest";
    // o.UseSsl = true; o.Port = 5671; // AMQPS
});
builder.Services.AddRabbitMqConsumer<MyHandler>(o =>
{
    o.Queue = "chat-messages"; // DLX default açık
    // o.EnableDeadLetter = false; o.MaxRedeliveryCount = 5; // DLX kapalıysa poison limiti
});

// Publish: publisher confirms + mandatory — route edilemeyen mesaj exception fırlatır.
await publisher.PublishAsync(exchange: "", routingKey: "chat-messages", "payload");
```

### ErrorHandling
```csharp
builder.Services.AddPinqponqErrorHandling();
// ...
app.UsePinqponqErrorHandling(); // pipeline'ın başında
```
Yakalanan exception → standart `ErrorResponse` (camelCase) + `traceId`/`correlationId`
ve alan adları Pinqloq'un beklediği yapılandırılmış formatta loglanır.

## Playground — paketleri ve loglarını tarayıcıdan deneyin

`samples/Pinqponq.Playground`, 13 paketin tamamını gerçek bağımlılıklara karşı çalıştıran
bir test konsoludur. Her çalıştırma hem sonucu hem de paketin o sırada ürettiği
**yapılandırılmış log kayıtlarını** gösterir — `ErrorHandling`'in `traceId`/`correlationId`
alan adları ancak ham hâlleriyle görülünce doğrulanabilir.

```bash
dotnet run --project samples/Pinqponq.Playground   # → http://127.0.0.1:5199
```

Açılışta hiçbir konteyner başlatılmaz; 47 senaryonun 32'si Docker olmadan çalışır. Redis,
Postgres, RabbitMQ, Mongo, MailHog ve SQL Server üst şeritten tek tıkla (Testcontainers
ile) kaldırılır. Ayrıntılar: [samples/README.md](samples/README.md).

## Derleme & test

Gereksinimler: .NET 10 SDK — tüm hedefleri derlemeye tek başına yeter. Testleri her
hedefte **çalıştırmak** için ayrıca .NET 8 ve .NET 9 runtime'ları gerekir. Entegrasyon
testleri için çalışan bir Docker ortamı (Testcontainers: Redis, RabbitMQ, PostgreSQL,
MongoDB, SQL Server, MailHog).

```bash
dotnet build -c Release   # net8.0 + net9.0 + net10.0
dotnet test -c Release --collect:"XPlat Code Coverage"
dotnet pack -c Release    # her paket için .nupkg (CPM ile tek tutarlı sürüm)
```

> Not: Bağımlılık sürümleri `Directory.Packages.props` içinde tek noktada tanımlıdır.
> Entegrasyon testleri `[Trait("Category", "Integration")]` ile işaretlenir.

## Katkı & güvenlik

- Katkı rehberi: [CONTRIBUTING.md](CONTRIBUTING.md)
- Davranış kuralları: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Güvenlik açığı bildirimi: [SECURITY.md](SECURITY.md)
- Değişiklik günlüğü: [CHANGELOG.md](CHANGELOG.md)
