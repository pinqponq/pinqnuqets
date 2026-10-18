# Pinqponq.Storage.S3

Object storage for .NET over AWS S3 and S3-compatible stores (Cloudflare R2,
MinIO): upload, download, presigned upload and download URLs, metadata, copy
and delete on one bucket, plus a bucket health check. You give it a bucket,
credentials and an endpoint; it builds the `AWSSDK.S3` client with the right
settings and registers one `IObjectStorage` service.

## Install

```bash
dotnet add package Pinqponq.Storage.S3
```

## Requirements

- .NET SDK: `net8.0`, `net9.0`, or `net10.0`
- A bucket on AWS S3 or an S3-compatible store, and an access key that can
  read, write and delete objects in it

## Quick start

```csharp
using Pinqponq.Storage.S3;
using Pinqponq.Storage.S3.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqObjectStorage(builder.Configuration.GetSection("Storage"));
builder.Services.AddHealthChecks().AddPinqponqObjectStorageHealthCheck();
```

```json
{
  "Storage": {
    "BucketName": "my-bucket",
    "AccessKey": "<from Vault>",
    "SecretKey": "<from Vault>",
    "Region": "us-east-1"
  }
}
```

Keep `AccessKey` and `SecretKey` out of `appsettings*.json`; read them from
Vault with `Pinqponq.Configuration.Vault`.

Cloudflare R2, configured in code:

```csharp
builder.Services.AddPinqponqObjectStorage(storageOptions =>
{
    storageOptions.UseCloudflareR2(accountId);
    storageOptions.BucketName = bucketName;
    storageOptions.AccessKey = accessKey;
    storageOptions.SecretKey = secretKey;
});
```

Using the storage:

```csharp
public sealed class AvatarService(IObjectStorage storage)
{
    public async Task<string> Save(string userId, Stream image, CancellationToken cancellationToken)
    {
        var key = $"avatars/{userId}.jpg";
        await storage.Upload(key, image, "image/jpeg", cancellationToken);
        return await storage.CreateDownloadUrl(key, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
```

A client can upload straight to the store: `CreateUploadUrl(key, contentType, expiry)`
returns a presigned PUT URL. The content type is part of the signature, so the
client must send exactly that `Content-Type` header with its PUT request.

## Configuration

`ObjectStorageOptions`, bound from a configuration section or set in the
`Action<ObjectStorageOptions>` passed to `AddPinqponqObjectStorage`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `BucketName` | `string` | `""` (required) | The bucket every key refers to. |
| `AccessKey` | `string` | `""` (required) | Access key id. |
| `SecretKey` | `string` | `""` (required) | Secret access key. |
| `ServiceUrl` | `string?` | `null` | Endpoint of an S3-compatible store, e.g. `https://minio.example.com`. Set this or `Region`. |
| `Region` | `string?` | `null` | AWS region system name, e.g. `us-east-1`. Set this or `ServiceUrl`. |
| `ForcePathStyle` | `bool?` | `null` | Path-style addressing (`{endpoint}/{bucket}/{key}`). `null` means `true` when `ServiceUrl` is set and `false` for AWS. |
| `DisablePayloadSigning` | `bool` | `false` | Uploads send an unsigned payload. R2 needs `true`. |
| `RequestChecksumMode` | `ObjectStorageChecksumMode` | `WhenRequired` | `WhenRequired` adds checksums only where an operation requires one, which AWS and S3-compatible stores both accept. `WhenSupported` is the AWS SDK default. |
| `AuthenticationRegion` | `string?` | `null` | Region used to sign requests to `ServiceUrl`. `null` lets the SDK derive it. |

Options are validated at startup (`ValidateOnStart`): a missing bucket, access
key or secret key, neither or both of `ServiceUrl` and `Region`, or a
`ServiceUrl` that is not an absolute http(s) URL stops the application from
starting.

## S3-compatible stores and Cloudflare R2

`ServiceUrl` points the client at any store that speaks the S3 API. Such
stores usually serve buckets by path, which is why `ForcePathStyle` turns on
when `ServiceUrl` is set. A plain `http://` service URL (a local MinIO, for
example) produces `http://` presigned URLs; every other endpoint gets
`https://` URLs.

Cloudflare R2 needs the settings below, several of them easy to miss.
`UseCloudflareR2(accountId)` sets all of them:

| Setting | R2 value | Why |
|---|---|---|
| `ServiceUrl` | `https://{accountId}.r2.cloudflarestorage.com` | The account's R2 endpoint. |
| `ForcePathStyle` | `true` | R2's S3 endpoint serves buckets by path. |
| `DisablePayloadSigning` | `true` | R2 does not implement chunked payload signing (`STREAMING-AWS4-HMAC-SHA256-PAYLOAD`); an unsigned payload over HTTPS is the supported path. |
| `RequestChecksumMode` | `WhenRequired` | R2 rejects the CRC checksums the AWS SDK sends with every request by default. |
| `AuthenticationRegion` | `auto` | R2 has no regions and expects requests signed for `auto`. |

## Main types

| Type | Kind | Purpose |
|---|---|---|
| `AddPinqponqObjectStorage(IConfigurationSection)` / `AddPinqponqObjectStorage(Action<ObjectStorageOptions>)` | DI extension (`IServiceCollection`) | Registers `ObjectStorageOptions`, a shared `IAmazonS3` client and `IObjectStorage`. |
| `AddPinqponqObjectStorageHealthCheck(...)` | DI extension (`IHealthChecksBuilder`) | Sends a HEAD request for the bucket (name defaults to `"object-storage"`). |
| `IObjectStorage` | Interface | `Upload`, `CreateDownloadUrl`, `CreateUploadUrl`, `GetMetadata`, `DownloadToFile`, `Download`, `Copy`, `Delete`. |
| `StoredObjectMetadata` | Record | `ContentLength` and `ContentType` of a stored object. |
| `ObjectNotFoundException` | Exception | Thrown by `Download` and `DownloadToFile` for a missing key; `Key` names it. |
| `ObjectStorageOptions` | Options | Bucket, credentials, endpoint and compatibility settings; `UseCloudflareR2(accountId)`. |
| `S3ObjectStorage`, `ObjectStorageHealthCheck` | Implementations | Registered by the extensions above; not usually referenced directly. |

## Notes / behavior

- **Empty keys are rejected.** Every method throws `ArgumentException` for a
  null, empty or whitespace key; `Upload` and `CreateUploadUrl` do the same for
  the content type. `CreateDownloadUrl` / `CreateUploadUrl` throw
  `ArgumentOutOfRangeException` for an expiry that is not positive.
- **Missing objects.** `GetMetadata` returns `null` for a missing key;
  `Download` and `DownloadToFile` throw `ObjectNotFoundException`. `Delete` of
  a missing key succeeds, as it does on S3.
- **Expiry is UTC.** Presigned URLs expire `expiry` after `DateTime.UtcNow`.
- **One client.** `AddPinqponqObjectStorage` registers one `IAmazonS3`
  singleton with `TryAddSingleton`, so a client the application registers
  first wins.
- **No key layout.** Key naming, folders, bucket choice, allowed content
  types and expiry durations stay in the application. A service that needs a
  narrower interface keeps a thin adapter over `IObjectStorage`.

## Related packages

- [Pinqponq.Configuration.Vault](../Pinqponq.Configuration.Vault/README.md) — keeps the access and secret keys out of the settings files.

## Repository

https://github.com/pinqponq/pinqnuqets
