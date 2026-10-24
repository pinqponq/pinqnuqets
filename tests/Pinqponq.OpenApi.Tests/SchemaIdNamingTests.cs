using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class SchemaIdNamingTests
{
    [Fact]
    public async Task Default_naming_uses_the_short_type_name()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("OrderModel", out _));
    }

    [Fact]
    public async Task Full_name_naming_replaces_dots_and_plus_signs()
    {
        await using var host = await OpenApiHost.Start(options => options.SchemaIdNaming = SchemaIdNaming.FullName);

        using var document = await host.GetDocument();

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("Pinqponq_OpenApi_Tests_Nested_OrderModel", out _));
        Assert.False(schemas.TryGetProperty("OrderModel", out _));
    }

    [Fact]
    public void Full_name_id_falls_back_to_the_short_name_for_a_type_without_a_full_name()
    {
        var typeInfo = System.Text.Json.JsonSerializerOptions.Default.GetTypeInfo(typeof(List<string>));

        var id = SchemaIdFactory.CreateFullNameId(typeInfo);

        Assert.DoesNotContain('.', id);
        Assert.StartsWith("System_Collections_Generic_List", id, StringComparison.Ordinal);
    }
}
