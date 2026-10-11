using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.Contracts.Tests;

public sealed class ResponseContractTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Base_response_serializes_the_fields_mobile_clients_decode()
    {
        var response = new BaseResponseModel
        {
            Status = false,
            StatusCode = 404,
            Message = "Not found.",
            ResponseCode = "order_not_found",
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));

        Assert.Equal(["status", "statusCode", "message", "responseCode"], json.RootElement.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal("Not found.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Paged_response_adds_paging_fields_after_the_envelope()
    {
        var response = new PagedResponseModel<List<int>> { Data = [1], Page = 1, Size = 10, RowCount = 1, PageCount = 1 };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));

        Assert.Equal(
            ["page", "size", "rowCount", "pageCount", "hasNextPage", "firstRowOnPage", "lastRowOnPage", "data", "status", "statusCode", "message", "responseCode"],
            json.RootElement.EnumerateObject().Select(field => field.Name).ToArray());
    }

    [Fact]
    public void Paged_request_defaults_to_the_first_page_of_ten_bound_from_page_number_and_page_size()
    {
        var request = new PagedRequestModel();

        Assert.Equal(1, request.Page);
        Assert.Equal(10, request.Size);
        Assert.Equal("PageNumber", typeof(PagedRequestModel).GetProperty(nameof(PagedRequestModel.Page))!.GetCustomAttribute<FromQueryAttribute>()!.Name);
        Assert.Equal("PageSize", typeof(PagedRequestModel).GetProperty(nameof(PagedRequestModel.Size))!.GetCustomAttribute<FromQueryAttribute>()!.Name);
    }

    [Fact]
    public void Request_error_feature_carries_its_event_and_detail()
    {
        var detail = new Dictionary<string, object?> { ["statusCode"] = 500 };

        IRequestErrorFeature errorFeature = new RequestErrorFeature("exception_Exception", detail);

        Assert.Equal("exception_Exception", errorFeature.EventName);
        Assert.Same(detail, errorFeature.Detail);
    }
}
