using Xunit;

namespace Pinqponq.Auth.ApiKey.Tests;

public sealed class ApiKeyComparisonTests
{
    [Fact]
    public void Same_key_matches() => Assert.True(ApiKeyComparison.IsMatch("s3cret-key", "s3cret-key"));

    [Theory]
    [InlineData("x3cret-key")]
    [InlineData("s3cret-kex")]
    [InlineData("s3cret-ke")]
    [InlineData("s3cret-key ")]
    [InlineData("S3CRET-KEY")]
    [InlineData("")]
    public void Different_key_does_not_match(string providedKey) =>
        Assert.False(ApiKeyComparison.IsMatch(providedKey, "s3cret-key"));

    [Fact]
    public void Multibyte_keys_compare_by_value() => Assert.True(ApiKeyComparison.IsMatch("anahtar-şifre", "anahtar-şifre"));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a-much-longer-key-than-the-configured-one-0123456789")]
    public void Digest_length_does_not_depend_on_key_length(string key)
    {
        Assert.Equal(
            ApiKeyComparison.CreateDigest("s3cret-key").Length,
            ApiKeyComparison.CreateDigest(key).Length);
    }

    [Fact]
    public void Digest_changes_when_any_byte_changes()
    {
        var expectedDigest = ApiKeyComparison.CreateDigest("s3cret-key");

        Assert.NotEqual(expectedDigest, ApiKeyComparison.CreateDigest("x3cret-key"));
        Assert.NotEqual(expectedDigest, ApiKeyComparison.CreateDigest("s3cret-kex"));
        Assert.Equal(expectedDigest, ApiKeyComparison.CreateDigest("s3cret-key"));
    }
}
