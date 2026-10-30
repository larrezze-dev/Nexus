using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Web.OpenApi;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.OpenApi;

/// <summary>
/// ADR-0014: the FluentValidation rules of an endpoint declared with <c>.ValidatedBy&lt;T&gt;()</c>
/// show up in the generated OpenAPI document as the equivalent schema constraints - one test per
/// rule mapping, plus the cases that must deliberately <i>not</i> be projected (conditional and
/// opaque rules, regex options) and the misconfigurations that must fail loudly.
/// </summary>
public sealed class FluentValidationOpenApiOperationTransformerTests
{
    [Fact]
    public async Task Query_parameters_get_minimum_and_maximum_from_GreaterThanOrEqualTo_and_InclusiveBetween()
    {
        var document = await GenerateListDocumentAsync();

        var operation = document.Operation("/items", HttpMethod.Get);
        var page = operation.ParameterSchema("page");
        page.Minimum.ShouldBe("1");
        page.Maximum.ShouldBeNull();

        var pageSize = operation.ParameterSchema("pageSize");
        pageSize.Minimum.ShouldBe("1");
        pageSize.Maximum.ShouldBe("200");
    }

    [Fact]
    public async Task Query_parameter_gets_enum_from_MustBeOneOf()
    {
        var document = await GenerateListDocumentAsync();

        var kind = document.Operation("/items", HttpMethod.Get).ParameterSchema("kind");

        EnumValues(kind).ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task Exclusive_bounds_come_from_GreaterThan_LessThan_and_ExclusiveBetween()
    {
        var document = await GenerateListDocumentAsync();

        var operation = document.Operation("/items", HttpMethod.Get);
        var score = operation.ParameterSchema("score");
        score.ExclusiveMinimum.ShouldBe("0");
        score.ExclusiveMaximum.ShouldBe("10");
        score.Minimum.ShouldBeNull();
        score.Maximum.ShouldBeNull();

        var ratio = operation.ParameterSchema("ratio");
        ratio.ExclusiveMinimum.ShouldBe("0.5");
        ratio.ExclusiveMaximum.ShouldBe("2.5");
    }

    [Fact]
    public async Task Conditional_and_opaque_rules_are_not_projected()
    {
        var document = await GenerateListDocumentAsync();

        var operation = document.Operation("/items", HttpMethod.Get);

        // When(...) on the rule: the constraint does not always apply.
        operation.ParameterSchema("conditional").Minimum.ShouldBeNull();

        // Must(...) is an opaque delegate.
        (operation.ParameterSchema("opaque").Enum ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Body_properties_get_pattern_from_Matches()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        body.Property("code").Pattern.ShouldBe("^[A-Z0-9]{1,10}$");
    }

    [Fact]
    public async Task Matches_with_regex_options_is_not_projected()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        body.Property("caseInsensitive").Pattern.ShouldBeNull();
    }

    [Fact]
    public async Task Body_property_gets_enum_from_MustBeOneOf()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        EnumValues(body.Property("status")).ShouldBe(["DRAFT", "ACTIVE"]);
    }

    [Fact]
    public async Task Nullable_body_property_keeps_null_in_the_enum_because_MustBeOneOf_lets_null_through()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        var json = await body.Property("optionalStatus").SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, TestContext.Current.CancellationToken);
        var schema = JsonNode.Parse(json)!.AsObject();
        schema["enum"]!.AsArray().Select(node => node?.ToJsonString()).ShouldBe(["\"DRAFT\"", "\"ACTIVE\"", null]);
    }

    [Fact]
    public async Task Dictionary_property_gets_required_keys_from_MustContainNonBlankKeys()
    {
        var document = await GenerateCreateDocumentAsync();

        var caption = document.Component(nameof(CreateItemRequest)).Property("captionI18n");

        caption.Required.ShouldNotBeNull();
        caption.Required.ShouldContain("de");
        var de = caption.Property("de");
        de.Type.ShouldBe(JsonSchemaType.String);
        de.MinLength.ShouldBe(1);
    }

    [Fact]
    public async Task String_lengths_come_from_Length_MaximumLength_and_NotEmpty()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        var name = body.Property("name");
        name.MinLength.ShouldBe(2);
        name.MaxLength.ShouldBe(50);

        var note = body.Property("note");
        note.MinLength.ShouldBe(1);
        note.MaxLength.ShouldBe(200);
    }

    [Fact]
    public async Task Top_level_required_is_never_inferred_from_the_validator()
    {
        var document = await GenerateCreateDocumentAsync();

        var body = document.Component(nameof(CreateItemRequest));

        // `status` is validated (NotNull + MustBeOneOf) but the DTO defaults it, so it is not
        // a required JSON property - the projection must not add it.
        (body.Required ?? new HashSet<string>()).ShouldNotContain("status");
    }

    [Fact]
    public async Task Path_parameter_is_matched_by_name()
    {
        var document = await OpenApiTestHost.GenerateAsync(
            services => services.AddValidator<RenameItemCommand, RenameItemValidator>(),
            app => app.MapPut("/items/{code}", (string code, RenameItemRequest request) => Results.Ok())
                .ValidatedBy<RenameItemCommand>());

        var operation = document.Operation("/items/{code}", HttpMethod.Put);

        operation.ParameterSchema("code").Pattern.ShouldBe("^[A-Z]{3}$");
        document.Component(nameof(RenameItemRequest)).Property("name").MaxLength.ShouldBe(40);
    }

    [Fact]
    public async Task Endpoint_without_ValidatedBy_is_left_untouched()
    {
        var document = await OpenApiTestHost.GenerateAsync(
            services => services.AddValidator<CreateItemRequest, CreateItemRequestValidator>(),
            app => app.MapPost("/items", (CreateItemRequest request) => Results.Ok()));

        document.Component(nameof(CreateItemRequest)).Property("code").Pattern.ShouldBeNull();
    }

    [Fact]
    public async Task ValidatedBy_without_a_registered_validator_fails_document_generation()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => OpenApiTestHost.GenerateAsync(
            _ => { },
            app => app.MapPost("/items", (CreateItemRequest request) => Results.Ok())
                .ValidatedBy<CreateItemCommand>()));

        exception.Message.ShouldContain("IValidator<CreateItemCommand>");
    }

    [Fact]
    public async Task Request_body_shared_by_endpoints_with_different_validators_fails_document_generation()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => OpenApiTestHost.GenerateAsync(
            services => services
                .AddValidator<CreateItemCommand, CreateItemValidator>()
                .AddValidator<CreateItemRequest, CreateItemRequestValidator>(),
            app =>
            {
                app.MapPost("/items", (CreateItemRequest request) => Results.Ok()).ValidatedBy<CreateItemCommand>();
                app.MapPost("/items/import", (CreateItemRequest request) => Results.Ok()).ValidatedBy<CreateItemRequest>();
            }));

        exception.Message.ShouldContain("shares its request body schema");
    }

    private static Task<OpenApiDocument> GenerateListDocumentAsync() =>
        OpenApiTestHost.GenerateAsync(
            services => services.AddValidator<ListItemsQuery, ListItemsValidator>(),
            app => app.MapGet(
                    "/items",
                    (int page = 1, int pageSize = 50, string? kind = null, int? score = null, decimal? ratio = null,
                        int? conditional = null, string? opaque = null) => Results.Ok())
                .ValidatedBy<ListItemsQuery>());

    private static Task<OpenApiDocument> GenerateCreateDocumentAsync() =>
        OpenApiTestHost.GenerateAsync(
            services => services.AddValidator<CreateItemCommand, CreateItemValidator>(),
            app => app.MapPost("/items", (CreateItemRequest request) => Results.Ok())
                .ValidatedBy<CreateItemCommand>());

    private static IEnumerable<string?> EnumValues(OpenApiSchema schema) =>
        schema.Enum.ShouldNotBeNull().Select(node => node?.GetValue<string>());

    internal sealed record ListItemsQuery(
        int Page, int PageSize, string? Kind, int? Score, decimal? Ratio, int? Conditional, string? Opaque);

    internal sealed class ListItemsValidator : AbstractValidator<ListItemsQuery>
    {
        public ListItemsValidator()
        {
            RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
            RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
            RuleFor(q => q.Kind).MustBeOneOf(["A", "B"]);
            RuleFor(q => q.Score).GreaterThan(0).LessThan(10);
            RuleFor(q => q.Ratio).ExclusiveBetween(0.5m, 2.5m);
            RuleFor(q => q.Conditional).GreaterThanOrEqualTo(5).When(q => q.Page > 1);
            RuleFor(q => q.Opaque).Must(value => value is null or "X" or "Y");
        }
    }

    internal sealed record CreateItemRequest
    {
        public required string Code { get; init; }

        public string? CaseInsensitive { get; init; }

        public string Status { get; init; } = "DRAFT";

        public string? OptionalStatus { get; init; }

        public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

        public required string Name { get; init; }

        public string? Note { get; init; }
    }

    internal sealed record CreateItemCommand(
        string Code,
        string? CaseInsensitive,
        string Status,
        string? OptionalStatus,
        IReadOnlyDictionary<string, string> CaptionI18n,
        string Name,
        string? Note);

    internal sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
    {
        public CreateItemValidator()
        {
            RuleFor(c => c.Code).Matches("^[A-Z0-9]{1,10}$");
            RuleFor(c => c.CaseInsensitive).Matches("^[a-z]+$", RegexOptions.IgnoreCase);
            RuleFor(c => c.Status).NotNull().MustBeOneOf(["DRAFT", "ACTIVE"]);
            RuleFor(c => c.OptionalStatus).MustBeOneOf(["DRAFT", "ACTIVE"]);
            RuleFor(c => c.CaptionI18n).NotNull().MustContainNonBlankKeys("de");
            RuleFor(c => c.Name).Length(2, 50);
            RuleFor(c => c.Note).NotEmpty().MaximumLength(200);
        }
    }

    internal sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
    {
        public CreateItemRequestValidator()
        {
            RuleFor(r => r.Code).Matches("^[0-9]+$");
        }
    }

    internal sealed record RenameItemRequest(string Name);

    internal sealed record RenameItemCommand(string Code, string Name);

    internal sealed class RenameItemValidator : AbstractValidator<RenameItemCommand>
    {
        public RenameItemValidator()
        {
            RuleFor(c => c.Code).Matches("^[A-Z]{3}$");
            RuleFor(c => c.Name).MaximumLength(40);
        }
    }
}
