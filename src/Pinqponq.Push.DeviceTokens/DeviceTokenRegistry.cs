using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Pinqponq.Push.DeviceTokens;

internal sealed class DeviceTokenRegistry<TContext>(
    TContext context,
    IOptions<DeviceTokenOptions> options,
    IEnumerable<IDeviceTokenRegisteredHandler> handlers,
    TimeProvider timeProvider) : IDeviceTokenRegistry
    where TContext : DbContext
{
    public async Task<DeviceTokenRegistration> Register(
        string userId,
        string token,
        string platform,
        string? deviceIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var settings = options.Value;
        var storedPlatform = ResolvePlatform(settings, platform);
        ValidateLength(token, settings.MaxTokenLength, nameof(token));
        var storedDeviceIdentifier = string.IsNullOrWhiteSpace(deviceIdentifier) ? null : deviceIdentifier;
        if (storedDeviceIdentifier is not null)
        {
            ValidateLength(storedDeviceIdentifier, settings.MaxDeviceIdentifierLength, nameof(deviceIdentifier));
        }

        DeviceTokenRegistration registration;
        try
        {
            registration = await Save(userId, token, storedPlatform, storedDeviceIdentifier, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException first)
        {
            // Two requests registered the same new token at once: the unique index rejected the second.
            // The retry finds the stored row; a failure that is not that race fails again and the first error is thrown.
            context.ChangeTracker.Clear();
            try
            {
                registration = await Save(userId, token, storedPlatform, storedDeviceIdentifier, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                throw first;
            }
        }

        foreach (var handler in handlers)
        {
            await handler.Handle(registration, cancellationToken).ConfigureAwait(false);
        }

        return registration;
    }

    public async Task<bool> Unregister(string userId, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrEmpty(token);

        var removed = await context.Set<DeviceToken>()
            .Where(deviceToken => deviceToken.UserId == userId && deviceToken.Token == token)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return removed > 0;
    }

    public async Task<IReadOnlyList<string>> GetTokens(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await context.Set<DeviceToken>()
            .AsNoTracking()
            .Where(deviceToken => deviceToken.UserId == userId)
            .OrderBy(deviceToken => deviceToken.Id)
            .Select(deviceToken => deviceToken.Token)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RemoveTokens(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return 0;
        }

        var tokenList = tokens.ToList();
        return await context.Set<DeviceToken>()
            .Where(deviceToken => tokenList.Contains(deviceToken.Token))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DeviceTokenRegistration> Save(
        string userId,
        string token,
        string platform,
        string? deviceIdentifier,
        CancellationToken cancellationToken)
    {
        var deviceTokens = context.Set<DeviceToken>();
        var now = timeProvider.GetUtcNow();

        var byToken = await deviceTokens
            .FirstOrDefaultAsync(deviceToken => deviceToken.Token == token, cancellationToken)
            .ConfigureAwait(false);
        var byDevice = deviceIdentifier is null
            ? null
            : await deviceTokens
                .FirstOrDefaultAsync(
                    deviceToken => deviceToken.UserId == userId && deviceToken.DeviceIdentifier == deviceIdentifier,
                    cancellationToken)
                .ConfigureAwait(false);

        // The device row wins: keeping it turns a token rotation into an update of the device's row.
        var row = byDevice ?? byToken;

        if (byDevice is not null && byToken is not null && byDevice.Id != byToken.Id)
        {
            // The token sits on another row. A token addresses one device, so that row goes; EF Core runs the
            // delete before the update that takes the token over.
            deviceTokens.Remove(byToken);
        }

        DeviceTokenRegistration registration;
        if (row is null)
        {
            row = new DeviceToken
            {
                UserId = userId,
                Token = token,
                Platform = platform,
                DeviceIdentifier = deviceIdentifier,
                CreatedAt = now,
                UpdatedAt = now,
            };
            deviceTokens.Add(row);
            registration = new DeviceTokenRegistration(row, IsNewRow: true, PreviousUserId: null, PreviousToken: null);
        }
        else
        {
            registration = new DeviceTokenRegistration(row, IsNewRow: false, row.UserId, row.Token);
            row.UserId = userId;
            row.Token = token;
            row.Platform = platform;
            row.UpdatedAt = now;

            // A row registered before the client sent identifiers adopts the one it now sends.
            row.DeviceIdentifier = deviceIdentifier ?? row.DeviceIdentifier;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return registration;
    }

    private static string ResolvePlatform(DeviceTokenOptions settings, string platform)
    {
        var match = settings.Platforms.FirstOrDefault(
            allowed => string.Equals(allowed, platform, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException(
            $"Platform '{platform}' is not one of: {string.Join(", ", settings.Platforms)}.", nameof(platform));
    }

    private static void ValidateLength(string? value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value is longer than the {maxLength} characters allowed.", parameterName);
        }
    }
}
