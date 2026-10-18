using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Push.Fcm.Tests;

public sealed class FirebaseAppProviderTests
{
    [Fact]
    public void Creates_the_app_once_and_reuses_it()
    {
        var appName = $"fcm-test-{Guid.NewGuid():N}";
        var createdOptionsCount = 0;
        AppOptions CreateAppOptions()
        {
            createdOptionsCount++;
            return new AppOptions { Credential = GoogleCredential.FromAccessToken("token"), ProjectId = "test-project" };
        }

        var firstProvider = new FirebaseAppProvider(CreateAppOptions, appName);
        var secondProvider = new FirebaseAppProvider(CreateAppOptions, appName);
        try
        {
            var firstApp = firstProvider.GetOrCreateApp();

            Assert.Same(firstApp, firstProvider.GetOrCreateApp());
            Assert.Same(firstApp, secondProvider.GetOrCreateApp());
            Assert.Same(firstProvider.GetMessaging(), secondProvider.GetMessaging());
            Assert.Equal(1, createdOptionsCount);
            Assert.Equal(appName, firstApp.Name);
        }
        finally
        {
            FirebaseApp.GetInstance(appName)?.Delete();
        }
    }

    [Fact]
    public void Creates_the_package_app_from_the_configured_credential_without_touching_the_default_app()
    {
        var provider = new FirebaseAppProvider(Options.Create(new FcmOptions { CredentialJson = TestServiceAccount.CreateJson() }));
        try
        {
            var app = provider.GetOrCreateApp();

            Assert.Equal(FirebaseAppProvider.APP_NAME, app.Name);
            var serviceAccount = Assert.IsType<ServiceAccountCredential>(app.Options.Credential.UnderlyingCredential);
            Assert.Equal("test-project", serviceAccount.ProjectId);
            Assert.Null(FirebaseApp.DefaultInstance);
        }
        finally
        {
            FirebaseApp.GetInstance(FirebaseAppProvider.APP_NAME)?.Delete();
        }
    }
}
