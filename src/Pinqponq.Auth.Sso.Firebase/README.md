# Pinqponq.Auth.Sso.Firebase

Validates Firebase ID tokens on the server. A mobile app gets a Firebase ID token when it signs
in through Firebase Authentication, for example with Sign in with Apple, or with Google sign-in
on a client that signs into Firebase after Google returns. The package implements the shared
[`IExternalAuthProvider`](../Pinqponq.Auth.Sso.Abstractions/README.md) contract and returns the
same `ExternalUserInfo` shape as [Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md).

The public keys that sign Firebase ID tokens are downloaded through a named `HttpClient` and kept
for the `max-age` the key response sends. While the cached keys are valid, a sign-in does not
download them again. "Key downloads" below lists the cases where it does.

## Install

```bash
dotnet add package Pinqponq.Auth.Sso.Firebase
```

## Requirements

- .NET 8.0, 9.0, or 10.0
- The Firebase project id of the app (Firebase console, Project settings)
- The client sends the Firebase ID token it received after sign-in. This package only validates
  that token. It does not sign anyone in.

## Quick start

```csharp
using Pinqponq.Auth.Sso.Abstractions;
using Pinqponq.Auth.Sso.Firebase;
using Pinqponq.Auth.Sso.Firebase.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPinqponqFirebaseSso(options =>
{
    options.ProjectId = builder.Configuration["FirebaseSso:ProjectId"];
    // options.AllowedSignInProviders.Add("apple.com"); // empty (default) accepts every sign-in method
    // options.RequireEmailVerified = true;             // default false, because Apple can hide the email
});

var app = builder.Build();

app.MapPost("/auth/firebase", async (
    FirebaseSignInRequest request,
    FirebaseAuthProvider firebaseProvider,
    CancellationToken cancellationToken) =>
{
    var firebaseResult = await firebaseProvider.AuthenticateFirebaseAsync(
        ExternalAuthRequest.FromIdToken(request.IdToken),
        cancellationToken);

    if (!firebaseResult.Result.Succeeded)
    {
        return Results.Unauthorized();
    }

    var user = firebaseResult.Result.User!;
    // user.Provider == "Firebase", user.Subject == the Firebase user id.
    // firebaseResult.SignInProvider is the sign-in method, for example "apple.com" or "google.com".
    // Look up or create the local account, then issue your own tokens.
    return Results.Ok();
});

app.Run();
```

`AuthenticateAsync` from `IExternalAuthProvider` returns only the shared `ExternalAuthResult`.
Use `AuthenticateFirebaseAsync` when you need the sign-in method.

## Using it together with Pinqponq.Auth.Sso.Google

Some clients send a Google ID token on one platform and a Firebase ID token on another for the
same Google sign-in. Register both providers and choose by the token's issuer:

```csharp
builder.Services.AddPinqponqGoogleSso(options => options.ClientIds.Add(googleClientId));
builder.Services.AddPinqponqFirebaseSso(options => options.ProjectId = firebaseProjectId);

app.MapPost("/auth/google", async (
    GoogleSignInRequest request,
    IExternalAuthProvider googleProvider,
    FirebaseAuthProvider firebaseProvider,
    CancellationToken cancellationToken) =>
{
    var signInRequest = ExternalAuthRequest.FromIdToken(request.IdToken);
    var provider = firebaseProvider.IsFirebaseIdToken(request.IdToken) ? firebaseProvider : googleProvider;
    var result = await provider.AuthenticateAsync(signInRequest, cancellationToken);
    // ...
});
```

`IsFirebaseIdToken` reads the issuer without validating the token. It only chooses the provider.
The chosen provider still validates the token in full.

`AddPinqponqFirebaseSso` registers `FirebaseAuthProvider` as itself, not as
`IExternalAuthProvider`. So `IExternalAuthProvider` still resolves to the Google provider when
both packages are registered, in either order.

## Configuration

`FirebaseAuthOptions` (configured with `AddPinqponqFirebaseSso`, validated when the host starts):

| Option | Default | Notes |
|---|---|---|
| `ProjectId` | `null` | Required. The token's issuer must be `https://securetoken.google.com/<ProjectId>` and its audience must be `<ProjectId>`. A missing value stops the host. |
| `AllowedSignInProviders` | empty | The accepted values of the `firebase.sign_in_provider` claim. Empty accepts every sign-in method. |
| `RequireEmailVerified` | `false` | When `true`, a token without `email_verified: true` fails. |
| `ClockSkew` | 5 minutes | Allowed difference between this server's clock and the token's `exp`, `nbf`, `iat` and `auth_time`. |
| `KeysCacheDurationWithoutMaxAge` | 1 hour | How long the keys are kept when the key response has no `Cache-Control: max-age`. |
| `MinimumUnknownKeyRefreshInterval` | 1 minute | The shortest time between two key downloads caused by an unknown key id. |
| `PublicKeysUri` | Google's JSON Web Key Set for Firebase ID tokens | Must be an absolute HTTPS address. |

## What is validated

- The signature (RS256) with Google's published keys.
- The issuer `https://securetoken.google.com/<ProjectId>` and the audience `<ProjectId>`.
- The lifetime: `exp` is required and must not have passed. `nbf`, when present, must have passed.
- `iat` and `auth_time` are required and must not be in the future.
- `sub` must be present and not empty.
- `email_verified` when `RequireEmailVerified` is on, and the sign-in method when
  `AllowedSignInProviders` is not empty.

Every failed check returns `ExternalAuthResult.Failure("Invalid Firebase ID token.")`, without
saying which check failed. A request without an ID token returns a separate failure message.

## Notes / behavior

- **Key downloads.** The keys are downloaded on the first sign-in and again when the cached keys
  expire. A token whose key id is not in the cache causes one extra download, at most once per
  `MinimumUnknownKeyRefreshInterval`. So tokens with made-up key ids cannot cause a download per
  request, unless that interval is set to zero.
- **When every sign-in downloads the keys.** A failed download stores nothing, so the next sign-in
  tries again. A key response with `max-age=0` expires as soon as it is stored. A
  `MinimumUnknownKeyRefreshInterval` of zero lets every unknown key id cause a download. In these
  cases each sign-in makes one request to the key endpoint.
- **A failed key download throws.** When the key endpoint does not answer with a success status,
  or its body is not a JSON Web Key Set, `AuthenticateAsync` throws `HttpRequestException` instead
  of returning a failure, because the
  token itself may be valid. Let your error handling turn it into a server error.
- **Profile fields.** `ExternalUserInfo` gets `sub`, `email`, `email_verified`, `name` and
  `picture`. Firebase ID tokens do not carry a given or family name, so those stay `null`.
- **Account linking by email.** Sign in with Apple can hide the user's real email address, and
  `email_verified` is not always set. Key local accounts by `(Provider, Subject)` and treat the
  email only as a hint.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Related packages

- [Pinqponq.Auth.Sso.Abstractions](../Pinqponq.Auth.Sso.Abstractions/README.md): the
  `IExternalAuthProvider` contract this package implements.
- [Pinqponq.Auth.Sso.Google](../Pinqponq.Auth.Sso.Google/README.md): validates Google ID tokens.
- [Pinqponq.Identity](../Pinqponq.Identity/README.md): issue your own access and refresh tokens
  after the sign-in succeeds.

## Repository

https://github.com/pinqponq/pinqnuqets
