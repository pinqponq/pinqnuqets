using Pinqponq.Identity.Passwords;
using Xunit;

namespace Pinqponq.Identity.Tests.Passwords;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_does_not_return_plaintext()
    {
        var hash = _hasher.Hash("s3cret!");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.DoesNotContain("s3cret!", hash);
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_differ()
    {
        Assert.NotEqual(_hasher.Hash("s3cret!"), _hasher.Hash("s3cret!"));
    }

    [Fact]
    public void Verify_returns_success_for_correct_password()
    {
        var hash = _hasher.Hash("s3cret!");

        Assert.Equal(PasswordVerificationOutcome.Success, _hasher.Verify(hash, "s3cret!"));
    }

    [Fact]
    public void Verify_returns_failed_for_wrong_password()
    {
        var hash = _hasher.Hash("s3cret!");

        Assert.Equal(PasswordVerificationOutcome.Failed, _hasher.Verify(hash, "wrong"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Hash_rejects_empty_password(string? password)
    {
        var act = () => _hasher.Hash(password!);

        Assert.ThrowsAny<ArgumentException>(act);
    }
}
