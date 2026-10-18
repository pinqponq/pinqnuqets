using FirebaseAdmin.Messaging;
using Xunit;

namespace Pinqponq.Push.Fcm.Tests;

public sealed class FcmOptionsValidatorTests
{
    private static readonly FcmOptionsValidator Validator = new();

    [Fact]
    public void Credential_json_of_a_service_account_is_valid()
    {
        var result = Validator.Validate(null, new FcmOptions { CredentialJson = TestServiceAccount.CreateJson() });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Credential_file_relative_to_the_app_directory_is_valid()
    {
        var fileName = $"fcm-test-{Guid.NewGuid():N}.json";
        var fullPath = Path.Combine(AppContext.BaseDirectory, fileName);
        File.WriteAllText(fullPath, TestServiceAccount.CreateJson());
        try
        {
            var result = Validator.Validate(null, new FcmOptions { CredentialPath = fileName });

            Assert.True(result.Succeeded);
        }
        finally
        {
            File.Delete(fullPath);
        }
    }

    [Fact]
    public void Disabled_sender_needs_no_credential()
    {
        var result = Validator.Validate(null, new FcmOptions { IsEnabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_credential_fails()
    {
        var result = Validator.Validate(null, new FcmOptions { CredentialPath = " " });

        Assert.True(result.Failed);
        Assert.Contains(nameof(FcmOptions.CredentialPath), result.FailureMessage);
        Assert.Contains(nameof(FcmOptions.CredentialJson), result.FailureMessage);
    }

    [Fact]
    public void Both_credential_path_and_json_fail()
    {
        var result = Validator.Validate(null, new FcmOptions
        {
            CredentialPath = "firebase.json",
            CredentialJson = TestServiceAccount.CreateJson(),
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Missing_credential_file_fails_with_the_resolved_path()
    {
        var result = Validator.Validate(null, new FcmOptions { CredentialPath = "missing-firebase.json" });

        Assert.True(result.Failed);
        Assert.Contains(Path.Combine(AppContext.BaseDirectory, "missing-firebase.json"), result.FailureMessage);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"authorized_user","client_id":"a","client_secret":"b","refresh_token":"c"}""")]
    [InlineData("""{"type":"service_account","client_email":"a@b.c","private_key":"not a key"}""")]
    public void Credential_that_is_not_a_service_account_fails(string credentialJson)
    {
        var result = Validator.Validate(null, new FcmOptions { CredentialJson = credentialJson });

        Assert.True(result.Failed);
        Assert.Contains("service account", result.FailureMessage);
    }

    [Fact]
    public void Undefined_android_priority_fails_even_when_disabled()
    {
        var result = Validator.Validate(null, new FcmOptions { IsEnabled = false, DataOnlyAndroidPriority = (Priority)42 });

        Assert.True(result.Failed);
        Assert.Contains(nameof(FcmOptions.DataOnlyAndroidPriority), result.FailureMessage);
    }

    [Fact]
    public void Defaults_are_enabled_with_normal_data_only_priority()
    {
        var fcmOptions = new FcmOptions();

        Assert.True(fcmOptions.IsEnabled);
        Assert.Equal(Priority.Normal, fcmOptions.DataOnlyAndroidPriority);
        Assert.Equal("Fcm", FcmOptions.SectionName);
    }
}
