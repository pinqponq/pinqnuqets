using FluentAssertions;
using Xunit;

namespace Pinqponq.Auth.Totp.Tests;

public sealed class Base32Tests
{
    [Fact]
    public void Encode_Decode_roundtrip()
    {
        var bytes = "HelloPinq"u8.ToArray();
        var encoded = Base32.Encode(bytes);
        Base32.Decode(encoded).Should().Equal(bytes);
    }

    [Fact]
    public void Decode_invalid_character_throws()
    {
        var act = () => Base32.Decode("ABC!");
        act.Should().Throw<FormatException>().WithMessage("*Invalid Base32*");
    }

    [Fact]
    public void Decode_empty_returns_empty()
    {
        Base32.Decode("").Should().BeEmpty();
        Base32.Decode("   ").Should().BeEmpty();
    }

    [Fact]
    public void Encode_empty_returns_empty()
    {
        Base32.Encode([]).Should().BeEmpty();
    }
}
