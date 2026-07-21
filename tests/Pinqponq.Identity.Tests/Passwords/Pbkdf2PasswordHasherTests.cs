using FluentAssertions;
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

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotContain("s3cret!");
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_differ()
    {
        _hasher.Hash("s3cret!").Should().NotBe(_hasher.Hash("s3cret!"));
    }

    [Fact]
    public void Verify_returns_success_for_correct_password()
    {
        var hash = _hasher.Hash("s3cret!");

        _hasher.Verify(hash, "s3cret!").Should().Be(PasswordVerificationOutcome.Success);
    }

    [Fact]
    public void Verify_returns_failed_for_wrong_password()
    {
        var hash = _hasher.Hash("s3cret!");

        _hasher.Verify(hash, "wrong").Should().Be(PasswordVerificationOutcome.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Hash_rejects_empty_password(string? password)
    {
        var act = () => _hasher.Hash(password!);

        act.Should().Throw<ArgumentException>();
    }
}
