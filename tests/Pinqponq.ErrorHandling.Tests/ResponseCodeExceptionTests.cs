using Microsoft.AspNetCore.Http;
using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ResponseCodeExceptionTests
{
    public static TheoryData<Exception, int> MappedExceptions() => new()
    {
        { new UnauthorizedAccessException(SampleResponseCodes.OrderLocked), StatusCodes.Status401Unauthorized },
        { new KeyNotFoundException(SampleResponseCodes.OrderLocked), StatusCodes.Status404NotFound },
        { new ArgumentException(SampleResponseCodes.OrderLocked), StatusCodes.Status400BadRequest },
        { new FormatException(SampleResponseCodes.OrderLocked), StatusCodes.Status400BadRequest },
        { new InvalidOperationException(SampleResponseCodes.OrderLocked), StatusCodes.Status400BadRequest },
    };

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public async Task Known_code_is_answered_with_the_status_of_the_exception_type(Exception exception, int expectedStatusCode)
    {
        var harness = CreateHarness(options => options.AddResponseCodes(typeof(SampleResponseCodes)));

        var httpContext = await harness.InvokeAsync(_ => throw exception);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
        Assert.Equal(SampleResponseCodes.OrderLocked, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Subtype_of_a_mapped_type_uses_its_mapping()
    {
        var harness = CreateHarness(options => options.AddResponseCodes(typeof(SampleResponseCodes)));

        var httpContext = await harness.InvokeAsync(_ => throw new ArgumentNullException(paramName: null, SampleResponseCodes.OrderMissing));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(SampleResponseCodes.OrderMissing, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Unknown_message_is_answered_500_general_error_by_default()
    {
        var harness = CreateHarness(options => options.AddResponseCodes(typeof(SampleResponseCodes)));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException("Sequence contains no elements"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal(CommonErrorCodes.GeneralError, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Unknown_message_keeps_the_status_and_the_message_when_passed_through()
    {
        var harness = CreateHarness(options => options.UnknownMessageBehavior = UnknownMessageBehavior.PassMessageThrough);

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException("order_archived"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.Equal("order_archived", body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Passed_through_message_unknown_to_the_catalog_becomes_the_fallback_code_with_localization_on()
    {
        var harness = CreateHarness(options => options.UnknownMessageBehavior = UnknownMessageBehavior.PassMessageThrough);
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException("order_archived"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.Equal(CommonErrorCodes.GeneralError, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Product_mapping_wins_over_the_default_mapping()
    {
        var harness = CreateHarness(options => options
            .AddResponseCodes(typeof(SampleResponseCodes))
            .Map<InvalidOperationException>(StatusCodes.Status409Conflict));

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException(SampleResponseCodes.OrderLocked));

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Product_exception_is_answered_with_its_mapped_status()
    {
        var harness = CreateHarness(options => options
            .AddResponseCodes(typeof(SampleResponseCodes))
            .Map<OrderLimitException>(StatusCodes.Status429TooManyRequests));

        var httpContext = await harness.InvokeAsync(_ => throw new OrderLimitException(SampleResponseCodes.OrderLocked));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal(SampleResponseCodes.OrderLocked, body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Most_specific_mapping_wins_regardless_of_registration_order()
    {
        var harness = CreateHarness(options => options
            .AddResponseCodes(typeof(SampleResponseCodes))
            .Map<ArgumentOutOfRangeException>(StatusCodes.Status422UnprocessableEntity)
            .Map<ArgumentException>(StatusCodes.Status409Conflict));

        var httpContext = await harness.InvokeAsync(_ => throw new ArgumentOutOfRangeException(paramName: null, SampleResponseCodes.OrderLocked));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Removed_mapping_leaves_the_exception_to_the_built_in_mapping()
    {
        var harness = CreateHarness(options => options
            .AddResponseCodes(typeof(SampleResponseCodes))
            .RemoveMapping<FormatException>());

        var httpContext = await harness.InvokeAsync(_ => throw new FormatException("Input string was not in a correct format."));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal("bad_request", body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public async Task Unmapped_exception_type_is_left_to_the_built_in_mapping()
    {
        var harness = CreateHarness(options => options.AddResponseCodes(typeof(SampleResponseCodes)));

        var httpContext = await harness.InvokeAsync(_ => throw new TimeoutException(SampleResponseCodes.OrderLocked));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, httpContext.Response.StatusCode);
        Assert.Equal("timeout", body.RootElement.GetProperty("responseCode").GetString());
    }

    [Fact]
    public void Codes_are_read_from_public_const_string_fields_only()
    {
        var options = new ResponseCodeExceptionOptions().AddResponseCodes(typeof(SampleResponseCodes));

        Assert.Equal(
            [SampleResponseCodes.OrderLocked, SampleResponseCodes.OrderMissing],
            options.ResponseCodes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Codes_can_be_added_from_a_list()
    {
        var options = new ResponseCodeExceptionOptions().AddResponseCodes([CommonErrorCodes.ApiBodyRequired]);

        Assert.Equal([CommonErrorCodes.ApiBodyRequired], options.ResponseCodes);
    }

    [Fact]
    public void Type_without_const_string_fields_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ResponseCodeExceptionOptions().AddResponseCodes(typeof(OrderLimitException)));

        Assert.Contains("OrderLimitException has no public const string field.", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_code_is_rejected(string responseCode)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ResponseCodeExceptionOptions().AddResponseCodes([responseCode]));

        Assert.StartsWith("A response code must not be empty.", exception.Message);
        Assert.Equal("responseCodes", exception.ParamName);
    }

    [Fact]
    public void Null_code_sources_are_rejected()
    {
        var options = new ResponseCodeExceptionOptions();

        Assert.Equal("responseCodesType", Assert.Throws<ArgumentNullException>(() => options.AddResponseCodes((Type)null!)).ParamName);
        Assert.Equal("responseCodes", Assert.Throws<ArgumentNullException>(() => options.AddResponseCodes((IEnumerable<string>)null!)).ParamName);
    }

    [Theory]
    [InlineData(399)]
    [InlineData(600)]
    public void Status_outside_the_error_range_is_rejected(int statusCode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResponseCodeExceptionOptions().Map<OrderLimitException>(statusCode));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(599)]
    public void Status_at_the_ends_of_the_error_range_is_accepted(int statusCode)
    {
        var options = new ResponseCodeExceptionOptions().Map<OrderLimitException>(statusCode);

        Assert.Equal(statusCode, options.StatusCodesByExceptionType[typeof(OrderLimitException)]);
    }

    private static ErrorResponseHarness CreateHarness(Action<ResponseCodeExceptionOptions> configure)
    {
        var options = new ResponseCodeExceptionOptions();
        configure(options);
        var harness = new ErrorResponseHarness();
        harness.ExceptionResolvers.Add(new ResponseCodeExceptionResolver(Microsoft.Extensions.Options.Options.Create(options)));
        return harness;
    }
}

internal static class SampleResponseCodes
{
    public const string OrderLocked = "order_locked";
    public const string OrderMissing = "order_missing";
    public const int OrderLimit = 5;
    public static readonly string OrderArchived = "order_archived";
    private const string OrderHidden = "order_hidden";
}

internal sealed class OrderLimitException(string message) : Exception(message);
