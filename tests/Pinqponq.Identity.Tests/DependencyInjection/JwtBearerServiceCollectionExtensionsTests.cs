using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Passwords;
using Xunit;

namespace Pinqponq.Identity.Tests.DependencyInjection;

public class JwtBearerServiceCollectionExtensionsTests
{
    private const string SigningKey = "a-signing-key-that-is-long-enough-for-hmac";

    [Fact]
    public void DerivesValidationParametersFromJwtOptions()
    {
        var bearerOptions = ResolveBearerOptions(services => services
            .AddPinqponqIdentity(ConfigureIssuer)
            .AddPinqponqJwtBearer());

        var parameters = bearerOptions.TokenValidationParameters;
        Assert.Equal("https://auth.example.com", parameters.ValidIssuer);
        Assert.Equal("example-clients", parameters.ValidAudience);
        Assert.Equal(TimeSpan.FromSeconds(45), parameters.ClockSkew);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.IsType<SymmetricSecurityKey>(parameters.IssuerSigningKey);
    }

    [Fact]
    public void AppliesTheCallersDelegateAfterTheDerivedParameters()
    {
        var bearerOptions = ResolveBearerOptions(services => services
            .AddPinqponqIdentity(ConfigureIssuer)
            .AddPinqponqJwtBearer(bearer =>
            {
                bearer.SaveToken = true;
                bearer.TokenValidationParameters.ValidAudience = "overridden";
            }));

        Assert.True(bearerOptions.SaveToken);
        Assert.Equal("overridden", bearerOptions.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public void ValidateOnlyOverloadBindsTheOptionsWithoutTheIssuingServices()
    {
        var services = new ServiceCollection();
        services.AddPinqponqJwtBearer(ConfigureIssuer);

        using var provider = services.BuildServiceProvider();
        var parameters = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        Assert.Equal("https://auth.example.com", parameters.ValidIssuer);
        Assert.True(
            provider.GetService<IPasswordHasher>() is null,
            "a service that only validates tokens has no use for the password hasher");
    }

    [Fact]
    public void RejectsASigningKeyThatIsTooShort()
    {
        var services = new ServiceCollection();
        services.AddPinqponqJwtBearer(jwt =>
        {
            ConfigureIssuer(jwt);
            jwt.SymmetricKey = "too-short";
        });

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        Assert.ThrowsAny<OptionsValidationException>(resolve);
    }

    [Fact]
    public void RegistersTheHandlerUnderACustomScheme()
    {
        var bearerOptions = ResolveBearerOptions(
            services => services.AddPinqponqJwtBearer(ConfigureIssuer, authenticationScheme: "Mobile"),
            scheme: "Mobile");

        Assert.Equal("https://auth.example.com", bearerOptions.TokenValidationParameters.ValidIssuer);
    }

    private static void ConfigureIssuer(JwtOptions jwt)
    {
        jwt.Issuer = "https://auth.example.com";
        jwt.Audience = "example-clients";
        jwt.SymmetricKey = SigningKey;
        jwt.ClockSkew = TimeSpan.FromSeconds(45);
    }

    private static JwtBearerOptions ResolveBearerOptions(
        Action<IServiceCollection> configure,
        string scheme = JwtBearerDefaults.AuthenticationScheme)
    {
        var services = new ServiceCollection();
        configure(services);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme);
    }
}
