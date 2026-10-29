using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.OpenApi;

/// <summary>
/// ADR-0014: numbers of response-only types are described as the JSON numbers the service
/// writes, not as <c>[integer, string]</c> (which only reflects that requests may quote them).
/// </summary>
public sealed class ResponseNumberOpenApiSchemaTransformerTests
{
    [Fact]
    public async Task Number_in_a_response_only_type_is_a_plain_integer()
    {
        var document = await GenerateAsync();

        var minorUnits = document.Component(nameof(AmountResponse)).Property("minorUnits");

        minorUnits.Type.ShouldBe(JsonSchemaType.Integer);
        minorUnits.Pattern.ShouldBeNull();
    }

    [Fact]
    public async Task Nullable_number_in_a_response_only_type_keeps_null()
    {
        var document = await GenerateAsync();

        var precision = document.Component(nameof(AmountResponse)).Property("precision");

        precision.Type.ShouldBe(JsonSchemaType.Integer | JsonSchemaType.Null);
        precision.Pattern.ShouldBeNull();
    }

    [Fact]
    public async Task Number_written_as_string_is_left_alone()
    {
        var document = await GenerateAsync();

        var quoted = document.Component(nameof(AmountResponse)).Property("quoted");

        quoted.Type!.Value.HasFlag(JsonSchemaType.String).ShouldBeTrue();
        quoted.Pattern.ShouldNotBeNull();
    }

    [Fact]
    public async Task Number_in_a_request_type_still_accepts_a_quoted_number()
    {
        var document = await GenerateAsync();

        var quantity = document.Component(nameof(OrderRequest)).Property("quantity");

        quantity.Type!.Value.HasFlag(JsonSchemaType.String).ShouldBeTrue();
        quantity.Type!.Value.HasFlag(JsonSchemaType.Integer).ShouldBeTrue();
        quantity.Pattern.ShouldNotBeNull();
    }

    [Fact]
    public async Task Number_in_a_type_nested_in_a_request_still_accepts_a_quoted_number()
    {
        var document = await GenerateAsync();

        var lineQuantity = document.Component(nameof(OrderLine)).Property("quantity");

        lineQuantity.Type!.Value.HasFlag(JsonSchemaType.String).ShouldBeTrue();
    }

    [Fact]
    public async Task Query_parameter_number_is_left_alone()
    {
        var document = await GenerateAsync();

        var page = document.Operation("/amounts", HttpMethod.Get).ParameterSchema("page");

        page.Type!.Value.HasFlag(JsonSchemaType.String).ShouldBeTrue();
    }

    private static Task<OpenApiDocument> GenerateAsync() =>
        OpenApiTestHost.GenerateAsync(
            _ => { },
            app =>
            {
                app.MapGet("/amounts", (int page = 1) => TypedResults.Ok(new AmountResponse(2, null, 5)));
                app.MapPost("/orders", (OrderRequest request) => TypedResults.Ok(new AmountResponse(2, 4, 5)));
            });

    internal sealed record AmountResponse(
        short MinorUnits,
        int? Precision,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
        int Quoted);

    internal sealed record OrderRequest(int Quantity, IReadOnlyList<OrderLine> Lines);

    internal sealed record OrderLine(int Quantity);
}
