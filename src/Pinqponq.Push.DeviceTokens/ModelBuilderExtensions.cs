using Microsoft.EntityFrameworkCore;

namespace Pinqponq.Push.DeviceTokens;

/// <summary>Adds the device token table to an application's model.</summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="DeviceToken"/> with a unique index on the token and an index on the user and device.
    /// Call it from <c>OnModelCreating</c> and add a migration. To keep an existing table, pass its name in
    /// <paramref name="configure"/>.
    /// </summary>
    public static ModelBuilder ApplyPinqponqDeviceTokens(
        this ModelBuilder modelBuilder,
        Action<DeviceTokenModelOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var options = new DeviceTokenModelOptions();
        configure?.Invoke(options);

        modelBuilder.Entity<DeviceToken>(entity =>
        {
            entity.ToTable(options.TableName, options.Schema);
            entity.HasKey(deviceToken => deviceToken.Id);
            entity.Property(deviceToken => deviceToken.UserId).IsRequired();
            entity.Property(deviceToken => deviceToken.Token).IsRequired();
            entity.Property(deviceToken => deviceToken.Platform).IsRequired();
            entity.HasIndex(deviceToken => deviceToken.Token).IsUnique();
            entity.HasIndex(deviceToken => new { deviceToken.UserId, deviceToken.DeviceIdentifier });
        });

        return modelBuilder;
    }
}
