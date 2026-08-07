using Pinqponq.Auth.Totp;
using Pinqponq.Auth.Totp.DependencyInjection;
using Pinqponq.Playground.Scenarios.Support;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Scenarios for <c>Pinqponq.Auth.Totp</c>. No infrastructure required.</summary>
public static class TotpScenarios
{
    private const string Package = "Pinqponq.Auth.Totp";

    private static readonly ScenarioField DigitsField =
        new("digits", "Digits", ScenarioFieldKind.Number, "6");

    private static readonly ScenarioField PeriodField =
        new("periodSeconds", "PeriodSeconds", ScenarioFieldKind.Number, "30");

    private static readonly ScenarioField AlgorithmField =
        new("algorithm", "Algorithm", ScenarioFieldKind.Enum, "Sha1", null, ["Sha1", "Sha256", "Sha512"]);

    public static IEnumerable<Scenario> Create()
    {
        yield return GenerateAndValidate();
        yield return DriftWindow();
        yield return WrongCode();
        yield return ReplayProtection();
        yield return MissingReplayStore();
        yield return Base32RoundTrip();
    }

    private static Scenario GenerateAndValidate() => new(
        new ScenarioDescriptor
        {
            Id = "totp.generate-validate",
            PackageId = Package,
            Title = "Generate a secret, compute a code, validate",
            Summary = "Generates a new secret, builds the otpauth:// URI that Authenticator apps "
                      + "read, computes the current code, and validates it.",
            Fields =
            [
                new ScenarioField("account", "Account name", ScenarioFieldKind.Text, "user@pinqponq.dev"),
                new ScenarioField("issuer", "Issuer", ScenarioFieldKind.Text, "Pinqponq"),
                DigitsField,
                PeriodField,
                AlgorithmField,
            ],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.Digits = context.Input.Int("digits");
                totp.PeriodSeconds = context.Input.Int("periodSeconds");
                totp.Algorithm = context.Input.Enum<TotpAlgorithm>("algorithm");
                totp.Issuer = context.Input.Text("issuer");
            }));

            var service = host.GetRequiredService<ITotpService>();

            var secret = service.GenerateSecret();
            context.Step("Secret generated", $"{secret.Length} Base32 characters");
            context.Artifact("secret", secret, "text");

            var uri = service.GetProvisioningUri(secret, context.Input.Text("account"));
            context.Artifact("otpauth URI", uri, "uri");
            context.Check("URI starts with otpauth://totp/", uri.StartsWith("otpauth://totp/", StringComparison.Ordinal));

            var code = service.ComputeCode(secret);
            context.Step("Current code computed", code);
            context.Artifact("code", code, "text");

            context.Require("Code validated", service.Validate(secret, code));
            context.Check(
                "Code has the requested length",
                code.Length == context.Input.Int("digits"),
                $"{code.Length} digits");
        });

    private static Scenario DriftWindow() => new(
        new ScenarioDescriptor
        {
            Id = "totp.drift-window",
            PackageId = Package,
            Title = "Clock drift window (ValidationWindow)",
            Summary = "The previous period's code is accepted when ValidationWindow=1, and rejected "
                      + "when it's 0. This is what happens when the user's clock is a few seconds behind.",
            Fields = [PeriodField],
        },
        async context =>
        {
            var period = context.Input.Int("periodSeconds");
            var now = DateTimeOffset.UtcNow;
            var previousPeriod = now.AddSeconds(-period);

            await using var tolerant = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.PeriodSeconds = period;
                totp.ValidationWindow = 1;
            }));

            var service = tolerant.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var oldCode = service.ComputeCode(secret, previousPeriod);
            context.Step("Previous period's code computed", oldCode);

            context.Require("Accepted with ValidationWindow=1", service.Validate(secret, oldCode, now));

            await using var strict = context.Host(services => services.AddPinqponqTotp(totp =>
            {
                totp.PeriodSeconds = period;
                totp.ValidationWindow = 0;
            }));

            context.Require(
                "Rejected with ValidationWindow=0",
                !strict.GetRequiredService<ITotpService>().Validate(secret, oldCode, now));
        });

    private static Scenario WrongCode() => new(
        new ScenarioDescriptor
        {
            Id = "totp.wrong-code",
            PackageId = Package,
            Title = "A wrong code is rejected",
            Summary = "A random code and an empty code are tried; both must be rejected.",
            NegativePath = true,
            Fields = [new ScenarioField("code", "Code to try", ScenarioFieldKind.Text, "000000")],
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqTotp());
            var service = host.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var real = service.ComputeCode(secret);
            var attempt = context.Input.Text("code");

            if (string.Equals(attempt, real, StringComparison.Ordinal))
            {
                // A one-in-a-million collision would otherwise look like a package defect.
                attempt = real == "000000" ? "111111" : "000000";
                context.Step("The tried code collided with the real code, changed it", attempt);
            }

            context.Require("Wrong code rejected", !service.Validate(secret, attempt));
            context.Require("Empty code rejected", !service.Validate(secret, string.Empty));
            context.Artifact("comparison", new { realCode = real, tried = attempt });
        });

    private static Scenario ReplayProtection() => new(
        new ScenarioDescriptor
        {
            Id = "totp.replay",
            PackageId = Package,
            Title = "ValidateAsync replay protection",
            Summary = "With an ITotpReplayStore, the same code is rejected on a second ValidateAsync "
                      + "call. Sync Validate is unaffected; replay protection only applies on the async path.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services =>
            {
                services.AddPinqponqTotp();
                services.AddSingleton<ITotpReplayStore, InMemoryTotpReplayStore>();
            });

            var service = host.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var code = service.ComputeCode(secret);

            const string Subject = "user@pinqponq.dev";
            context.Require(
                "First ValidateAsync accepted",
                await service.ValidateAsync(secret, code, Subject, cancellationToken: context.CancellationToken));
            context.Require(
                "Same code rejected the second time",
                !await service.ValidateAsync(secret, code, Subject, cancellationToken: context.CancellationToken));
            context.Check(
                "Sync Validate still passes (doesn't use the replay store)",
                service.Validate(secret, code));
        });

    private static Scenario MissingReplayStore() => new(
        new ScenarioDescriptor
        {
            Id = "totp.missing-replay-store",
            PackageId = Package,
            Title = "ValidateAsync fails without a replay store",
            Summary = "ValidateAsync requires an ITotpReplayStore; if none is registered, the error "
                      + "shows an example AddScoped call. Sync Validate keeps working without a store.",
            NegativePath = true,
        },
        async context =>
        {
            await using var host = context.Host(services => services.AddPinqponqTotp());
            var service = host.GetRequiredService<ITotpService>();
            var secret = service.GenerateSecret();
            var code = service.ComputeCode(secret);

            context.Require("Sync Validate works without a store", service.Validate(secret, code));

            Exception? thrown = null;
            try
            {
                await service.ValidateAsync(secret, code, "user@pinqponq.dev", cancellationToken: context.CancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("ValidateAsync InvalidOperationException", thrown is not null);
            context.Check(
                "The error names ITotpReplayStore",
                thrown!.Message.Contains("ITotpReplayStore", StringComparison.Ordinal),
                thrown.Message);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });
        });

    private static Scenario Base32RoundTrip() => new(
        new ScenarioDescriptor
        {
            Id = "totp.base32",
            PackageId = Package,
            Title = "Base32 encode/decode",
            Summary = "The RFC 4648 Base32 helper the package exposes: text → Base32 → back. Also "
                      + "shows that an invalid character throws a FormatException.",
            Fields = [new ScenarioField("text", "Text", ScenarioFieldKind.Text, "Pinqponq")],
        },
        context =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(context.Input.Text("text"));
            var encoded = Base32.Encode(bytes);
            context.Step("Encoded", encoded);

            var decoded = Base32.Decode(encoded);
            var roundTrip = System.Text.Encoding.UTF8.GetString(decoded);

            context.Require("Round-trip produced the same text", roundTrip == context.Input.Text("text"), roundTrip);
            context.Artifact("result", new { input = context.Input.Text("text"), base32 = encoded, decoded = roundTrip });

            Exception? thrown = null;
            try
            {
                Base32.Decode("!!!invalid!!!");
            }
            catch (FormatException exception)
            {
                thrown = exception;
            }

            context.Require("Invalid character threw FormatException", thrown is not null);
            context.Artifact("exception", new { type = thrown!.GetType().Name, message = thrown.Message });

            return Task.CompletedTask;
        });
}
