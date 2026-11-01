using System.Reflection;
using Nexus.Platform.Domain.RoleAssignments;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

public sealed class RoleAssignmentTests
{
    private static readonly Guid Id = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private static readonly Guid GrantedBy = Guid.CreateVersion7();
    private static readonly Guid LegalEntityId = Guid.CreateVersion7();
    private static readonly DateTimeOffset ValidFrom = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "ACC-R2: scopeType PROJECT is rejected unconditionally")]
    public void Create_with_PROJECT_scope_fails_ACC_R2()
    {
        var result = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeProject, Guid.CreateVersion7(), ValidFrom, null, GrantedBy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R2");
    }

    [Fact(DisplayName = "ACC-R1: a non-GLOBAL scope requires scopeEntityId")]
    public void Create_non_global_without_scope_entity_id_fails_ACC_R1()
    {
        var result = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeLegalEntity, scopeEntityId: null, ValidFrom, null, GrantedBy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R1");
    }

    [Fact(DisplayName = "ACC-R1: GLOBAL scope must not carry a scopeEntityId")]
    public void Create_global_with_scope_entity_id_fails_ACC_R1()
    {
        var result = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, Guid.CreateVersion7(), ValidFrom, null, GrantedBy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R1");
    }

    [Fact(DisplayName = "ACC-R5: a user cannot grant a role assignment to themselves")]
    public void Create_self_grant_fails_ACC_R5()
    {
        var result = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom, null, grantedBy: UserId);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R5");
    }

    [Fact]
    public void Create_with_valid_legal_entity_scope_succeeds()
    {
        var result = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeLegalEntity, LegalEntityId, ValidFrom, null, GrantedBy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ScopeEntityId.ShouldBe(LegalEntityId);
        result.Value.DomainEvents.ShouldHaveSingleItem();
        var changed = result.Value.DomainEvents.Single().ShouldBeOfType<RoleAssignmentChanged>();
        changed.ChangeKind.ShouldBe("GRANTED");
        changed.Topic.ShouldBe("nexus.platform.role-assignment.changed");
    }

    [Fact(DisplayName = "ACC-R5: a user cannot revoke their own role assignment")]
    public void Revoke_by_the_assigned_user_fails_ACC_R5()
    {
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom, null, GrantedBy).Value;

        var result = assignment.Revoke(UserId, DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R5");
    }

    [Fact(DisplayName = "ACC-R6: a seeded GLOBAL role assignment cannot be revoked through the API")]
    public void Revoke_seeded_global_assignment_fails_ACC_R6()
    {
        var assignment = CreateSeededGlobalAssignment();

        var result = assignment.Revoke(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R6");
    }

    [Fact]
    public void Revoke_a_manual_assignment_by_a_different_user_succeeds()
    {
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom, null, GrantedBy).Value;
        var revokedBy = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var result = assignment.Revoke(revokedBy, now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        assignment.ValidTo.ShouldBe(now);
        assignment.RevokedBy.ShouldBe(revokedBy);
        assignment.DomainEvents.Count.ShouldBe(2);
        assignment.DomainEvents[1].ShouldBeOfType<RoleAssignmentChanged>().ChangeKind.ShouldBe("REVOKED");
    }

    [Fact]
    public void Revoke_an_already_revoked_assignment_is_idempotent()
    {
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom, null, GrantedBy).Value;
        var revokedBy = Guid.CreateVersion7();
        assignment.Revoke(revokedBy, DateTimeOffset.UtcNow);
        var eventCountAfterFirstRevoke = assignment.DomainEvents.Count;

        var result = assignment.Revoke(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        assignment.DomainEvents.Count.ShouldBe(eventCountAfterFirstRevoke);
    }

    /// <summary>
    /// A SEED/GLOBAL assignment as it would be loaded from the database (there is no public
    /// factory for that shape: only the seed SQL creates one, never the API). Built through
    /// reflection on the aggregate's private state, the same way EF Core materializes it.
    /// </summary>
    private static RoleAssignment CreateSeededGlobalAssignment()
    {
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom, null, GrantedBy).Value;

        typeof(RoleAssignment)
            .GetProperty(nameof(RoleAssignment.Source), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(assignment, RoleAssignment.SourceSeed);

        return assignment;
    }
}
