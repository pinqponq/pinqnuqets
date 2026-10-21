using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class RateLimitRejectionHandlerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(59_500, 60)]
    public void Retry_after_is_rounded_up_to_whole_seconds_and_never_below_one(int retryAfterMilliseconds, int expectedSeconds)
    {
        var retryAfterSeconds = RateLimitRejectionHandler.ToRetryAfterSeconds(TimeSpan.FromMilliseconds(retryAfterMilliseconds));

        Assert.Equal(expectedSeconds, retryAfterSeconds);
    }

    [Fact]
    public async Task Lease_with_retry_after_sets_the_header_and_passes_the_seconds_to_the_writer()
    {
        var rejectionWriter = new RecordingRejectionWriter();
        var httpContext = CreateHttpContext(rejectionWriter);

        await RateLimitRejectionHandler.OnRejected(
            new OnRejectedContext { HttpContext = httpContext, Lease = new RejectedLease(TimeSpan.FromSeconds(4.2)) },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("5", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.Equal(5, rejectionWriter.RetryAfterSeconds);
    }

    [Fact]
    public async Task Lease_without_retry_after_sets_no_header_and_still_writes_the_body()
    {
        var rejectionWriter = new RecordingRejectionWriter();
        var httpContext = CreateHttpContext(rejectionWriter);

        await RateLimitRejectionHandler.OnRejected(
            new OnRejectedContext { HttpContext = httpContext, Lease = new RejectedLease(retryAfter: null) },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("Retry-After"));
        Assert.True(rejectionWriter.IsCalled);
        Assert.Null(rejectionWriter.RetryAfterSeconds);
    }

    [Fact]
    public async Task Default_writer_describes_the_rejection_on_the_request_error_feature()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqErrorHandling();
        services.AddSingleton<IRateLimitRejectionWriter, ErrorResponseRejectionWriter>();
        using var serviceProvider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Response.Body = new MemoryStream();

        await RateLimitRejectionHandler.OnRejected(
            new OnRejectedContext { HttpContext = httpContext, Lease = new RejectedLease(TimeSpan.FromSeconds(1)) },
            CancellationToken.None);

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(RateLimitingEvents.RequestRateLimited, errorFeature.EventName);
        Assert.Equal(CommonErrorCodes.TooManyRequests, errorFeature.Detail["errorCode"]);
        Assert.Equal(StatusCodes.Status429TooManyRequests, errorFeature.Detail["statusCode"]);
    }

    private static DefaultHttpContext CreateHttpContext(IRateLimitRejectionWriter rejectionWriter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(rejectionWriter);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class RecordingRejectionWriter : IRateLimitRejectionWriter
    {
        public bool IsCalled { get; private set; }

        public int? RetryAfterSeconds { get; private set; }

        public Task Write(HttpContext context, int? retryAfterSeconds, CancellationToken cancellationToken)
        {
            IsCalled = true;
            RetryAfterSeconds = retryAfterSeconds;
            return Task.CompletedTask;
        }
    }

    private sealed class RejectedLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, [NotNullWhen(true)] out object? metadata)
        {
            var hasRetryAfter = retryAfter is not null && metadataName == MetadataName.RetryAfter.Name;
            metadata = hasRetryAfter ? retryAfter : null;
            return hasRetryAfter;
        }
    }
}
