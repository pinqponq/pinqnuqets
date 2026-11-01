using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class SchemaIdNamingTests
{
    private static readonly Regex OpenApiSchemaIdPattern = new("^[a-zA-Z0-9.\\-_]+$");

    [Fact]
    public async Task Default_naming_uses_the_short_type_name()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("OrderModel", out _));
    }

    [Fact]
    public async Task Full_name_naming_joins_the_namespace_and_declaring_types_with_underscores()
    {
        await using var host = await OpenApiHost.Start(options => options.SchemaIdNaming = SchemaIdNaming.FullName);

        using var document = await host.GetDocument();

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("Pinqponq_OpenApi_Tests_Nested_OrderModel", out _));
        Assert.False(schemas.TryGetProperty("OrderModel", out _));
    }

    [Fact]
    public void Full_name_id_of_a_generic_type_names_its_type_arguments_and_is_a_valid_schema_id()
    {
        var id = CreateFullNameId(typeof(Page<Nested.OrderModel, Page<int, string>>));

        Assert.Equal(
            "Pinqponq_OpenApi_Tests_PageOfPinqponq_OpenApi_Tests_Nested_OrderModelAndPinqponq_OpenApi_Tests_PageOfSystem_Int32AndSystem_String",
            id);
        Assert.Matches(OpenApiSchemaIdPattern, id);
    }

    [Fact]
    public void Full_name_id_of_an_array_type_argument_differs_from_the_id_of_its_element_type()
    {
        var arrayArgumentId = CreateFullNameId(typeof(Page<Nested.OrderModel[], int>));
        var elementArgumentId = CreateFullNameId(typeof(Page<Nested.OrderModel, int>));

        Assert.Equal(
            "Pinqponq_OpenApi_Tests_PageOfArrayOfPinqponq_OpenApi_Tests_Nested_OrderModelAndSystem_Int32",
            arrayArgumentId);
        Assert.NotEqual(elementArgumentId, arrayArgumentId);
    }

    [Fact]
    public void Full_name_id_of_a_type_nested_in_a_generic_type_drops_the_generic_arity()
    {
        var id = CreateFullNameId(typeof(Page<int, int>.Item));

        Assert.Equal("Pinqponq_OpenApi_Tests_Page_ItemOfSystem_Int32AndSystem_Int32", id);
    }

    [Fact]
    public void Full_name_id_of_a_type_in_the_global_namespace_is_its_name()
    {
        Assert.Equal("GlobalNamespaceModel", CreateFullNameId(typeof(GlobalNamespaceModel)));
    }

    [Fact]
    public void Full_name_id_removes_characters_that_are_not_allowed_in_a_schema_id()
    {
        var anonymousType = new { Name = "value" }.GetType();

        var id = CreateFullNameId(anonymousType);

        Assert.NotNull(id);
        Assert.Matches(OpenApiSchemaIdPattern, id);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<Nested.OrderModel>))]
    [InlineData(typeof(Dictionary<string, Nested.OrderModel>))]
    public void Full_name_id_keeps_types_inline_that_the_default_keeps_inline(Type type)
    {
        Assert.Null(CreateFullNameId(type));
    }

    private static string? CreateFullNameId(Type type) =>
        SchemaIdFactory.CreateFullNameId(JsonSerializerOptions.Default.GetTypeInfo(type));
}

internal sealed record Page<TItem, TCursor>(TItem First, TCursor Cursor)
{
    internal sealed record Item(TItem Value);
}
