# Pinqponq.Playground — paket ve log test konsolu

Reponun 13 `Pinqponq.*` paketini tarayıcıdan, **gerçek** bağımlılıklara karşı çalıştıran
bir ASP.NET Core uygulaması. Her çalıştırma iki şeyi birden gösterir: paketin ne yaptığı
ve o sırada **hangi yapılandırılmış log kayıtlarını ürettiği**.

```bash
dotnet run --project samples/Pinqponq.Playground
# → http://127.0.0.1:5199
```

Paketler **kaynak** olarak referanslanır (`ProjectReference`), yani `src/` altındaki bir
değişiklik konsolda anında görünür.

## Docker gerekli mi?

Hayır — açılışta hiçbir konteyner başlatılmaz, uygulama anında ayağa kalkar.
**47 senaryonun 32'si Docker olmadan çalışır**: Identity, TOTP, SSO negatif yolları,
ErrorHandling ve SMS'in tamamı (SMS trafiği konsolun kendi içindeki sahte NetGSM ucuna
gider, böylece `IHttpClientFactory` + sorgu kurulumu + Polly retry yolu gerçekten koşar).

Docker varsa üst şeritteki servise tıklayıp **Başlat** deyin; Testcontainers ile konteyner
ayağa kalkar ve o servise bağlı senaryolar açılır. Servisi **Durdur** ile kapatmak,
health-check'in `Unhealthy`'ye düşmesini ve retry davranışını göstermek için kullanılır.

| Servis | İmaj | Açtığı senaryolar |
|---|---|---|
| PostgreSQL | `postgres:16-alpine` | Database.Postgres |
| Redis | `redis:7.4-alpine` | Cache |
| RabbitMQ | `rabbitmq:3.13-alpine` | Messaging.RabbitMq |
| MongoDB | `mongo:7.0` | Database.Mongo |
| MailHog | `mailhog/mailhog:v1.0.1` | Mail, Otp'nin e-posta kanalı |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` | Database.Mssql (ağır, ~1,5 GB, ARM64 yok) |

İmaj etiketleri `tests/Pinqponq.TestSupport/Fixtures/` ile birebir aynıdır, böylece konsol
ve entegrasyon testleri aynı sunucu sürümlerine karşı çalışır.

### Alternatifler

- **Sabit portlu ortam**: `docker compose -f samples/Pinqponq.Playground/docker-compose.yml up -d`
- **Mevcut sunucular**: `appsettings.json` → `Playground:ExternalServices` altına bağlantı
  dizesini yazın; o servis "Harici" olarak işaretlenir ve konteyner başlatılmaz. Karışık
  kullanım (harici Redis + konteyner Postgres) desteklenir.

## Nasıl çalışıyor

**Her senaryo kendi DI konteynerinde koşar.** Senaryo gövdesi paketin kendi
`AddPinqponqXxx(...)` uzantısını çağırır — yani kayıt kodu da test edilmiş olur. Konteyner
koşu bitince `DisposeAsync` edilir, bu yüzden `IOptions<T>` önbelleği koşular arasında
sızmaz ve arayüzden değiştirdiğiniz her options alanı gerçekten etkili olur.
`NpgsqlDataSource` / `IConnectionMultiplexer` gibi singleton kaynaklar da böylece sızmaz.

**Loglar koşuya bağlanır.** Koşunun log sağlayıcısı `runId` ile kurulur, yani kayıt
yapısal olarak damgalanır — arka planda çalışan RabbitMQ consumer'ı gibi ambient bağlamın
akmadığı yerlerde de doğru çalışır. Sonuç kartındaki **"N log →"** düğmesi alt konsolu o
koşuya filtreler. Bir satırı açtığınızda `messageTemplate` ve yapılandırılmış alanlar ham
hâlleriyle görünür — `Pinqponq.ErrorHandling`'in ürettiği `TraceId` / `CorrelationId` /
`ResponseCode` alan adları ancak böyle doğrulanabilir.

## Öne çıkan senaryolar

| Senaryo | Ne kanıtlıyor |
|---|---|
| `errorhandling.log-shape` | Middleware'in log kaydı: message template'i, alan adları, seviyesi (500 → Error, 4xx → Warning). |
| `errorhandling.correlation` | Gelen `X-Correlation-ID` → yanıttaki `traceId` **ve** logdaki `CorrelationId`; logdaki `TraceId` ise isteğin kendi kimliği, ikisi farklı. |
| `sms.retry` | Sahte uç ilk N isteği 500 döner; denemeler ve aralarındaki üstel gecikme tablo hâlinde görünür. |
| `otp.sms` / `otp.email` | Kod yalnızca kanaldan çıktığı için sahte SMS kaydından veya MailHog kutusundan geri okunup doğrulanır. |
| `identity.refresh.rotate` | Depoda yalnızca SHA-256 hash tutulduğu, eski kaydın revoke edilip yenisine zincirlendiği. |
| `cache.lock` | İki ayrı konteynerden aynı kaynağa kilit: ikincisi `Acquired=false`, bırakılınca üçüncü deneme başarılı. |
| `rabbit.dead-letter` | Handler hata verince mesajın `{kuyruk}.dead`'e düşmesi **ve** paketin ürettiği hata logunun `Queue` alanı. |

## Klavye

| Kısayol | İş |
|---|---|
| <kbd>Ctrl/⌘ K</kbd> | Senaryo ara |
| <kbd>Ctrl/⌘ ↵</kbd> | Açık senaryoyu çalıştır |
| <kbd>/</kbd> | Kenar çubuğu filtresine odaklan |

## HTTP API

Arayüz tamamen bu uçları kullanır; curl ile de sürülebilir.

| Uç | İş |
|---|---|
| `GET /api/catalog` | Paketler, senaryolar, form alanları, hangi senaryonun neden kapalı olduğu |
| `POST /api/scenarios/{id}/run` | `{"input":{...}}` → adımlar, çıktılar ve koşunun logları |
| `GET /api/infra`, `POST /api/infra/{id}/start\|stop\|restart` | Servis durumu ve kontrolü |
| `GET /api/logs`, `GET /api/logs/stream` | Log geçmişi ve canlı akış (SSE) |
| `GET /api/mail`, `GET /api/sms` | MailHog gelen kutusu, sahte NetGSM'in aldığı istekler |
| `GET /sandbox/errors/{vaka}` | `UsePinqponqErrorHandling()` uygulanmış gerçek boru hattı |

```bash
curl -i -H 'X-Correlation-ID: pinq-123' http://127.0.0.1:5199/sandbox/errors/unauthorized
curl -s 'http://127.0.0.1:5199/api/logs?q=pinq-123' | jq '.entries[0].state'
```

## Notlar

- Uygulama yalnızca `127.0.0.1` dinler: gerçek kimlik bilgileri girilebilen ve dışarı
  bağlantı açan bir araçtır.
- `dotnet watch` her derlemede konteynerleri yeniden yaratır; `dotnet run` tercih edin.
- Uygulama kapanırken başlattığı konteynerleri kaldırır. Beklenmedik bir sonlanmadan sonra
  artakalanlar için Testcontainers'ın resource reaper'ı devrededir.
