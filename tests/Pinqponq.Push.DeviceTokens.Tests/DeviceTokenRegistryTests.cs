using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Push.DeviceTokens.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DeviceTokenRegistryTests(DeviceTokenDatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private DeviceTokenRegistry<TestDbContext> CreateRegistry(
        TestDbContext context,
        Action<DeviceTokenOptions>? configure = null,
        IDeviceTokenRegisteredHandler[]? handlers = null,
        TimeProvider? timeProvider = null)
    {
        var options = new DeviceTokenOptions();
        configure?.Invoke(options);
        return new DeviceTokenRegistry<TestDbContext>(
            context, Options.Create(options), handlers ?? [], timeProvider ?? TimeProvider.System);
    }

    private async Task<List<DeviceToken>> Rows(Func<DeviceToken, bool> filter)
    {
        await using var context = database.CreateContext();
        return (await context.Set<DeviceToken>().AsNoTracking().ToListAsync()).Where(filter).ToList();
    }

    [Fact]
    public async Task New_token_creates_a_row()
    {
        var user = Unique("user");
        var token = Unique("token");
        var now = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        await using var context = database.CreateContext();

        var registration = await CreateRegistry(context, timeProvider: new FixedTimeProvider(now))
            .Register(user, token, "ANDROID", "device-1");

        Assert.True(registration.IsNewRow);
        Assert.Null(registration.PreviousUserId);
        Assert.Null(registration.PreviousToken);
        Assert.False(registration.IsTokenRotation);
        var row = Assert.Single(await Rows(r => r.Token == token));
        Assert.Equal(user, row.UserId);
        Assert.Equal("android", row.Platform);
        Assert.Equal("device-1", row.DeviceIdentifier);
        Assert.Equal(now, row.CreatedAt);
        Assert.Equal(now, row.UpdatedAt);
    }

    [Fact]
    public async Task Same_token_for_another_user_moves_the_row()
    {
        var first = Unique("user");
        var second = Unique("user");
        var token = Unique("token");
        await using var context = database.CreateContext();
        await CreateRegistry(context).Register(first, token, "ios");

        await using var secondContext = database.CreateContext();
        var registration = await CreateRegistry(secondContext).Register(second, token, "ios");

        Assert.False(registration.IsNewRow);
        Assert.Equal(first, registration.PreviousUserId);
        Assert.Equal(second, Assert.Single(await Rows(r => r.Token == token)).UserId);
    }

    [Fact]
    public async Task Same_token_for_the_same_user_updates_the_row_once()
    {
        var user = Unique("user");
        var token = Unique("token");
        var created = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        var updated = created.AddHours(1);
        await using var context = database.CreateContext();
        await CreateRegistry(context, timeProvider: new FixedTimeProvider(created)).Register(user, token, "ios");

        await using var secondContext = database.CreateContext();
        await CreateRegistry(secondContext, timeProvider: new FixedTimeProvider(updated)).Register(user, token, "web");

        var row = Assert.Single(await Rows(r => r.Token == token));
        Assert.Equal("web", row.Platform);
        Assert.Equal(created, row.CreatedAt);
        Assert.Equal(updated, row.UpdatedAt);
    }

    [Fact]
    public async Task Rotated_token_on_a_known_device_replaces_the_row_token()
    {
        var user = Unique("user");
        var oldToken = Unique("old");
        var newToken = Unique("new");
        var device = Unique("device");
        await using var context = database.CreateContext();
        await CreateRegistry(context).Register(user, oldToken, "android", device);

        await using var secondContext = database.CreateContext();
        var registration = await CreateRegistry(secondContext).Register(user, newToken, "android", device);

        Assert.True(registration.IsTokenRotation);
        Assert.Equal(oldToken, registration.PreviousToken);
        Assert.Empty(await Rows(r => r.Token == oldToken));
        var row = Assert.Single(await Rows(r => r.DeviceIdentifier == device));
        Assert.Equal(newToken, row.Token);
    }

    [Fact]
    public async Task Rotated_token_that_sits_on_another_row_removes_that_row()
    {
        var user = Unique("user");
        var device = Unique("device");
        var otherDevice = Unique("device");
        var deviceToken = Unique("device-token");
        var incoming = Unique("incoming");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(user, deviceToken, "android", device);
        await registry.Register(user, incoming, "android", otherDevice);

        await using var secondContext = database.CreateContext();
        await CreateRegistry(secondContext).Register(user, incoming, "android", device);

        var rows = await Rows(r => r.UserId == user);
        var row = Assert.Single(rows);
        Assert.Equal(incoming, row.Token);
        Assert.Equal(device, row.DeviceIdentifier);
    }

    [Fact]
    public async Task Unknown_device_identifier_adds_a_second_row_for_the_user()
    {
        var user = Unique("user");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(user, Unique("phone"), "android", Unique("device"));

        await registry.Register(user, Unique("tablet"), "android", Unique("device"));

        Assert.Equal(2, (await Rows(r => r.UserId == user)).Count);
    }

    [Fact]
    public async Task Device_identifier_of_another_user_does_not_take_over_their_row()
    {
        var owner = Unique("owner");
        var other = Unique("other");
        var device = Unique("device");
        var ownerToken = Unique("owner-token");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(owner, ownerToken, "android", device);

        await registry.Register(other, Unique("other-token"), "android", device);

        var ownerRow = Assert.Single(await Rows(r => r.UserId == owner));
        Assert.Equal(ownerToken, ownerRow.Token);
        Assert.Single(await Rows(r => r.UserId == other));
    }

    [Fact]
    public async Task Row_without_identifier_adopts_the_identifier_it_is_sent_later()
    {
        var user = Unique("user");
        var token = Unique("token");
        var device = Unique("device");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(user, token, "ios");

        await registry.Register(user, token, "ios", device);
        await using var secondContext = database.CreateContext();
        await CreateRegistry(secondContext).Register(user, token, "ios");

        Assert.Equal(device, Assert.Single(await Rows(r => r.Token == token)).DeviceIdentifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Blank_device_identifier_is_stored_as_null(string? identifier)
    {
        var token = Unique("token");
        await using var context = database.CreateContext();

        await CreateRegistry(context).Register(Unique("user"), token, "ios", identifier);

        Assert.Null(Assert.Single(await Rows(r => r.Token == token)).DeviceIdentifier);
    }

    [Fact]
    public async Task Token_at_the_maximum_length_is_accepted_and_one_over_is_rejected()
    {
        var user = Unique("user");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context, o => o.MaxTokenLength = 40);
        var atLimit = (Guid.NewGuid().ToString("N") + "12345678")[..40];
        var tooLong = new string('b', 41);

        await registry.Register(user, atLimit, "ios");
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => registry.Register(user, tooLong, "ios"));

        Assert.Equal("token", exception.ParamName);
        Assert.Empty(await Rows(r => r.Token == tooLong));
        await registry.RemoveTokens([atLimit]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_token_is_rejected(string token)
    {
        await using var context = database.CreateContext();

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateRegistry(context).Register(Unique("user"), token, "ios"));
    }

    [Fact]
    public async Task Device_identifier_over_the_maximum_length_is_rejected()
    {
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context, o => o.MaxDeviceIdentifierLength = 10);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => registry.Register(Unique("user"), Unique("token"), "ios", new string('d', 11)));

        Assert.Equal("deviceIdentifier", exception.ParamName);
    }

    [Fact]
    public async Task Platform_outside_the_option_list_is_rejected_and_a_custom_list_is_used()
    {
        var token = Unique("token");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context, o => o.Platforms = ["Huawei"]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => registry.Register(Unique("user"), token, "ios"));
        await registry.Register(Unique("user"), token, "huawei");

        Assert.Equal("platform", exception.ParamName);
        Assert.Equal("Huawei", Assert.Single(await Rows(r => r.Token == token)).Platform);
    }

    [Fact]
    public async Task Blank_user_id_is_rejected()
    {
        await using var context = database.CreateContext();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateRegistry(context).Register(" ", Unique("token"), "ios"));
    }

    [Fact]
    public async Task Handlers_run_in_order_after_the_row_is_saved()
    {
        var user = Unique("user");
        var token = Unique("token");
        var calls = new List<string>();
        var first = new RecordingHandler("first", calls, async registration =>
            calls.Add($"saved:{(await Rows(r => r.Token == registration.DeviceToken.Token)).Count}"));
        var second = new RecordingHandler("second", calls);
        await using var context = database.CreateContext();

        var registration = await CreateRegistry(context, handlers: [first, second]).Register(user, token, "ios");

        Assert.Equal(["saved:1", "first", "second"], calls);
        Assert.Same(registration, first.Received);
    }

    [Fact]
    public async Task Handler_exception_reaches_the_caller_and_the_row_stays_saved()
    {
        var token = Unique("token");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context, handlers: [new ThrowingHandler()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.Register(Unique("user"), token, "ios"));

        Assert.Single(await Rows(r => r.Token == token));
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_new_token_end_with_one_row()
    {
        var token = Unique("token");
        var users = Enumerable.Range(0, 8).Select(_ => Unique("user")).ToList();

        await Task.WhenAll(users.Select(async user =>
        {
            await using var context = database.CreateContext();
            await CreateRegistry(context).Register(user, token, "android");
        }));

        Assert.Single(await Rows(r => r.Token == token));
    }

    [Fact]
    public async Task Unregister_removes_only_the_users_own_row()
    {
        var owner = Unique("owner");
        var token = Unique("token");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(owner, token, "ios");

        Assert.False(await registry.Unregister(Unique("other"), token));
        Assert.Single(await Rows(r => r.Token == token));
        Assert.True(await registry.Unregister(owner, token));
        Assert.False(await registry.Unregister(owner, token));
        Assert.Empty(await Rows(r => r.Token == token));
    }

    [Fact]
    public async Task GetTokens_returns_the_users_tokens_in_registration_order()
    {
        var user = Unique("user");
        var tokens = new[] { Unique("a"), Unique("b"), Unique("c") };
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        foreach (var token in tokens)
        {
            await registry.Register(user, token, "android");
        }

        await registry.Register(Unique("other"), Unique("d"), "android");

        Assert.Equal(tokens, await registry.GetTokens(user));
        Assert.Empty(await registry.GetTokens(Unique("nobody")));
    }

    [Fact]
    public async Task RemoveTokens_removes_the_listed_tokens_of_any_user_and_returns_the_count()
    {
        var dead1 = Unique("dead");
        var dead2 = Unique("dead");
        var alive = Unique("alive");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(Unique("u1"), dead1, "ios");
        await registry.Register(Unique("u2"), dead2, "ios");
        await registry.Register(Unique("u3"), alive, "ios");

        var removed = await registry.RemoveTokens([dead1, dead2, Unique("unknown")]);

        Assert.Equal(2, removed);
        Assert.Empty(await Rows(r => r.Token == dead1 || r.Token == dead2));
        Assert.Single(await Rows(r => r.Token == alive));
        Assert.Equal(0, await registry.RemoveTokens([]));
    }

    [Fact]
    public async Task Row_found_by_token_takes_a_new_identifier_over_its_old_one()
    {
        var user = Unique("user");
        var token = Unique("token");
        var newDevice = Unique("new-device");
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);
        await registry.Register(user, token, "ios", Unique("old-device"));

        await registry.Register(user, token, "ios", newDevice);

        Assert.Equal(newDevice, Assert.Single(await Rows(r => r.Token == token)).DeviceIdentifier);
    }

    [Fact]
    public async Task Token_of_exactly_one_character_is_accepted_with_a_limit_of_one()
    {
        var user = Unique("user");
        var token = "x";
        await using var context = database.CreateContext();

        await CreateRegistry(context, o => o.MaxTokenLength = 1).Register(user, token, "ios");

        Assert.Equal([token], await CreateRegistry(context).GetTokens(user));
        await CreateRegistry(context).RemoveTokens([token]);
    }

    [Fact]
    public async Task Rejections_name_the_problem()
    {
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context, o => o.MaxTokenLength = 5);

        var platform = await Assert.ThrowsAsync<ArgumentException>(() => registry.Register("u", "t", "toaster"));
        var length = await Assert.ThrowsAsync<ArgumentException>(() => registry.Register("u", "toolong", "ios"));

        Assert.Contains("'toaster'", platform.Message);
        Assert.Contains("android, ios, web", platform.Message);
        Assert.Contains("5", length.Message);
    }

    [Fact]
    public async Task Arguments_are_checked_on_every_method()
    {
        await using var context = database.CreateContext();
        var registry = CreateRegistry(context);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.Unregister(" ", "t"));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.Unregister("u", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.GetTokens(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => registry.RemoveTokens(null!));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler(
        string name,
        List<string> calls,
        Func<DeviceTokenRegistration, Task>? before = null) : IDeviceTokenRegisteredHandler
    {
        public DeviceTokenRegistration? Received { get; private set; }

        public async Task Handle(DeviceTokenRegistration registration, CancellationToken cancellationToken = default)
        {
            if (before is not null)
            {
                await before(registration);
            }

            Received = registration;
            calls.Add(name);
        }
    }

    private sealed class ThrowingHandler : IDeviceTokenRegisteredHandler
    {
        public Task Handle(DeviceTokenRegistration registration, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("handler failed");
    }
}
