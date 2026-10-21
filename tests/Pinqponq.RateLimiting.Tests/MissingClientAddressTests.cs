using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class MissingClientAddressTests
{
    [Fact]
    public async Task Requests_without_a_client_address_share_one_partition_and_the_warning_is_logged_once()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(new RateLimitPolicyOptions
        {
            PermitLimit = 2,
            Window = TimeSpan.FromMinutes(1),
            PartitionBy = RateLimitPartitionSource.ClientAddress,
        });

        var statusCodes = await host.SendMany(3, () => host.Send(RateLimitingHost.PolicyPath));
        var otherAddress = await host.SendMany(1, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statusCodes);
        Assert.Equal([HttpStatusCode.OK], otherAddress);
        var missingAddressWarnings = host.LoggerProvider.Records
            .Where(record => record.Level == LogLevel.Warning && record.Message.Contains(RateLimitPartitionKeyResolver.MissingClientAddressPartitionKey, StringComparison.Ordinal))
            .ToList();
        Assert.Single(missingAddressWarnings);
    }
}
