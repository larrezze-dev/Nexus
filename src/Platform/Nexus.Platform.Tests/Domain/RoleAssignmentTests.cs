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

    [Fact(DisplayName = "ACC-R3: revoking an active assignment with a future validTo pulls validTo in to now")]
    public void Revoke_an_active_assignment_with_a_future_valid_to_revokes_it()
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-1), now.AddYears(1), GrantedBy).Value;
        var revokedBy = Guid.CreateVersion7();

        var result = assignment.Revoke(revokedBy, now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        assignment.ValidTo.ShouldBe(now);
        assignment.RevokedBy.ShouldBe(revokedBy);
        assignment.DomainEvents[^1].ShouldBeOfType<RoleAssignmentChanged>().ChangeKind.ShouldBe("REVOKED");
    }

    [Fact(DisplayName = "ACC-R3: revoking an assignment whose validTo has already passed is a no-op")]
    public void Revoke_an_already_expired_assignment_is_idempotent()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredAt = now.AddDays(-1);
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-10), expiredAt, GrantedBy).Value;
        var eventCount = assignment.DomainEvents.Count;

        var result = assignment.Revoke(Guid.CreateVersion7(), now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        assignment.ValidTo.ShouldBe(expiredAt);
        assignment.RevokedBy.ShouldBeNull();
        assignment.DomainEvents.Count.ShouldBe(eventCount);
    }

    [Fact(DisplayName = "ACC-R3: IsValidAt - started, not revoked, not ended (validTo exclusive, validFrom inclusive)")]
    public void IsValidAt_covers_the_validity_window()
    {
        var now = DateTimeOffset.UtcNow;
        var open = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-1), null, GrantedBy).Value;
        var future = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(1), null, GrantedBy).Value;
        var bounded = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-2), now.AddDays(-1), GrantedBy).Value;
        var revoked = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-1), null, GrantedBy).Value;
        revoked.Revoke(Guid.CreateVersion7(), now).Value.ShouldBeTrue();

        open.IsValidAt(now).ShouldBeTrue();
        open.IsValidAt(now.AddDays(-1)).ShouldBeTrue();
        open.IsValidAt(now.AddDays(-1).AddTicks(-1)).ShouldBeFalse();
        future.IsValidAt(now).ShouldBeFalse();
        future.IsValidAt(now.AddDays(1)).ShouldBeTrue();
        bounded.IsValidAt(now.AddDays(-1).AddTicks(-1)).ShouldBeTrue();
        bounded.IsValidAt(now.AddDays(-1)).ShouldBeFalse();
        revoked.IsValidAt(now.AddSeconds(1)).ShouldBeFalse();
    }

    [Fact(DisplayName = "ACC-R3: CheckRevocable returns false (no-op) for revoked or expired rows, true for a revocable one, and changes nothing")]
    public void CheckRevocable_reports_no_op_and_revocable_without_changing_anything()
    {
        var now = DateTimeOffset.UtcNow;
        var live = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-1), null, GrantedBy).Value;
        var expired = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-2), now.AddDays(-1), GrantedBy).Value;
        var revoked = RoleAssignment.Create(Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, now.AddDays(-1), null, GrantedBy).Value;
        revoked.Revoke(Guid.CreateVersion7(), now);
        var eventsBefore = live.DomainEvents.Count;

        var other = Guid.CreateVersion7();
        var liveCheck = live.CheckRevocable(other, now);
        liveCheck.IsSuccess.ShouldBeTrue();
        liveCheck.Value.ShouldBeTrue();
        live.RevokedBy.ShouldBeNull();
        live.ValidTo.ShouldBeNull();
        live.DomainEvents.Count.ShouldBe(eventsBefore);

        expired.CheckRevocable(other, now).Value.ShouldBeFalse();
        revoked.CheckRevocable(other, now).Value.ShouldBeFalse();
    }

    [Fact(DisplayName = "ACC-R5 before ACC-R6: CheckRevocable reports the no-op first, then ACC-R5 over ACC-R6, then ACC-R6")]
    public void CheckRevocable_checks_no_op_then_ACC_R5_then_ACC_R6()
    {
        var now = DateTimeOffset.UtcNow;

        // Own, live, seeded GLOBAL: both rules apply, ACC-R5 wins.
        var seeded = CreateSeededGlobalAssignment();
        var own = seeded.CheckRevocable(UserId, now);
        own.IsFailure.ShouldBeTrue();
        own.Error!.Code.ShouldBe("ACC-R5");

        // Someone else: ACC-R6.
        var other = seeded.CheckRevocable(Guid.CreateVersion7(), now);
        other.IsFailure.ShouldBeTrue();
        other.Error!.Code.ShouldBe("ACC-R6");

        // Already ended: the no-op beats both, even for the assigned user.
        var ended = CreateSeededGlobalAssignment(validTo: now.AddDays(-1));
        var noOp = ended.CheckRevocable(UserId, now);
        noOp.IsSuccess.ShouldBeTrue();
        noOp.Value.ShouldBeFalse();

        // A SEED assignment that is not GLOBAL is revocable.
        var seededScoped = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeLegalEntity, LegalEntityId, ValidFrom, null, GrantedBy).Value;
        SetSource(seededScoped, RoleAssignment.SourceSeed);
        seededScoped.CheckRevocable(Guid.CreateVersion7(), now).Value.ShouldBeTrue();
    }

    /// <summary>
    /// A SEED/GLOBAL assignment as it would be loaded from the database (there is no public
    /// factory for that shape: only the seed SQL creates one, never the API). Built through
    /// reflection on the aggregate's private state, the same way EF Core materializes it.
    /// </summary>
    private static RoleAssignment CreateSeededGlobalAssignment(DateTimeOffset? validTo = null)
    {
        var assignment = RoleAssignment.Create(
            Id, UserId, RoleId, RoleAssignment.ScopeGlobal, null, ValidFrom.AddDays(-30), validTo, GrantedBy).Value;
        SetSource(assignment, RoleAssignment.SourceSeed);
        return assignment;
    }

    private static void SetSource(RoleAssignment assignment, string source) =>
        typeof(RoleAssignment)
            .GetProperty(nameof(RoleAssignment.Source), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(assignment, source);
}
