namespace Pinqponq.Push.DeviceTokens;

/// <summary>Where <see cref="ModelBuilderExtensions.ApplyPinqponqDeviceTokens"/> puts the table.</summary>
public sealed class DeviceTokenModelOptions
{
    /// <summary>Table name. Defaults to <c>DeviceTokens</c>.</summary>
    public string TableName { get; set; } = "DeviceTokens";

    /// <summary>Table schema. <see langword="null"/> (the default) uses the context's default schema.</summary>
    public string? Schema { get; set; }
}
