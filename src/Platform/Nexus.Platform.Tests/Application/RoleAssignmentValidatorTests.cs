using Nexus.BuildingBlocks.Application;
using Nexus.Platform.Application.RoleAssignments.CreateRoleAssignment;
using Nexus.Platform.Application.RoleAssignments.GetRoleAssignments;
using Nexus.Platform.Domain.RoleAssignments;
using Shouldly;

namespace Nexus.Platform.Tests.Application;

/// <summary>
/// <c>scopeType</c> is validated with <c>MustBeOneOf(RoleAssignment.ScopeTypes)</c> (ADR-0014), so
/// both the create command and the list filter accept exactly the contract's <c>enum</c>.
/// Tested on the validators directly: over HTTP, <c>POST /v1/role-assignments</c>'s permission
/// filter (<c>x-scope: body.scopeEntityId</c>) runs first and fails closed with 403 on an
/// unknown scope type before validation is reached.
/// </summary>
public sealed class RoleAssignmentValidatorTests
{
    public static TheoryData<string> ContractScopeTypes => new(RoleAssignment.ScopeTypes);

    [Theory]
    [MemberData(nameof(ContractScopeTypes))]
    public void Create_accepts_every_contract_scope_type(string scopeType) =>
        new CreateRoleAssignmentValidator().Validate(Create(scopeType)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("COUNTRY")]
    [InlineData("legal_entity")]
    [InlineData("")]
    public void Create_rejects_a_scope_type_outside_the_contract_enum(string scopeType)
    {
        var result = new CreateRoleAssignmentValidator().Validate(Create(scopeType));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CreateRoleAssignmentCommand.ScopeType));
    }

    [Fact]
    public void Create_rejects_a_missing_scope_type()
    {
        var result = new CreateRoleAssignmentValidator().Validate(Create(null!));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(CreateRoleAssignmentCommand.ScopeType));
    }

    [Theory]
    [MemberData(nameof(ContractScopeTypes))]
    public void List_accepts_every_contract_scope_type(string scopeType) =>
        new GetRoleAssignmentsValidator().Validate(List(scopeType)).IsValid.ShouldBeTrue();

    [Fact]
    public void List_accepts_no_scope_type_filter() =>
        new GetRoleAssignmentsValidator().Validate(List(null)).IsValid.ShouldBeTrue();

    [Fact]
    public void List_rejects_a_scope_type_outside_the_contract_enum()
    {
        var result = new GetRoleAssignmentsValidator().Validate(List("COUNTRY"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetRoleAssignmentsQuery.ScopeType));
    }

    private static CreateRoleAssignmentCommand Create(string scopeType) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        scopeType,
        Guid.CreateVersion7(),
        RoundedSystemTimeProvider.Instance.GetUtcNow(),
        null);

    private static GetRoleAssignmentsQuery List(string? scopeType) => new(1, 50, null, null, null, scopeType, null);
}
