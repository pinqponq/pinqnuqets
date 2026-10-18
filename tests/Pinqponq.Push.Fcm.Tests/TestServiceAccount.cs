using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pinqponq.Push.Fcm.Tests;

internal static class TestServiceAccount
{
    public static string CreateJson()
    {
        using var rsa = RSA.Create(2048);
        var serviceAccount = new JsonObject
        {
            ["type"] = "service_account",
            ["project_id"] = "test-project",
            ["private_key_id"] = "test-key-id",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "sender@test-project.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        };
        return serviceAccount.ToJsonString();
    }
}
