using Xunit;

namespace Pinqponq.Auth.Totp.Tests;

public sealed class Base32Tests
{
    [Fact]
    public void Encode_Decode_roundtrip()
    {
        var bytes = "HelloPinq"u8.ToArray();
        var encoded = Base32.Encode(bytes);
        Assert.Equal(bytes, Base32.Decode(encoded));
    }

    [Fact]
    public void Decode_invalid_character_throws()
    {
        var act = () => Base32.Decode("ABC!");
        var exception = Assert.ThrowsAny<FormatException>(act);
        Assert.Contains("Invalid Base32", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_empty_returns_empty()
    {
        Assert.Empty(Base32.Decode(""));
        Assert.Empty(Base32.Decode("   "));
    }

    [Fact]
    public void Encode_empty_returns_empty()
    {
        Assert.Empty(Base32.Encode([]));
    }
}
