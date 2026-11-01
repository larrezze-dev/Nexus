using Nexus.Platform.Domain.Roles;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

public sealed class RoleTests
{
    [Fact]
    public void Create_sets_a_non_system_active_role_with_no_permissions_yet()
    {
        var result = Role.Create(
            Guid.CreateVersion7(),
            code: "MOBILE_SCANNER",
            captionI18n: new Dictionary<string, string> { ["de"] = "Mobiler Scanner", ["en"] = "Mobile scanner" },
            description: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSystem.ShouldBeFalse();
        result.Value.IsActive.ShouldBeTrue();
        result.Value.DomainEvents.Count.ShouldBe(1);
        var raised = result.Value.DomainEvents[0].ShouldBeOfType<RoleUpdated>();
        raised.PermissionCodes.ShouldBeEmpty();
        raised.Topic.ShouldBe("nexus.platform.role.updated");
    }

    // ACC-R9 (a system role cannot be deactivated) is exercised end to end in
    // Integration/RoleApiTests.cs against the real seeded SYSTEM_ADMIN role: Role.Create's
    // only public factory always produces isSystem = false (the 11 seeded system roles are
    // inserted directly by db/seed/common, never through this aggregate), so there is no way
    // to construct an isSystem = true Role from this assembly to unit-test the guard in isolation.

    [Fact]
    public void Deactivate_a_non_system_role_succeeds_and_raises_RoleUpdated()
    {
        var role = Role.Create(Guid.CreateVersion7(), "MOBILE_SCANNER", new Dictionary<string, string> { ["de"] = "x" }, null).Value;

        var result = role.Deactivate(currentPermissionCodes: ["ORG_STRUCTURE:READ"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        role.IsActive.ShouldBeFalse();
        var raised = role.DomainEvents[^1].ShouldBeOfType<RoleUpdated>();
        raised.IsActive.ShouldBeFalse();
        raised.PermissionCodes.ShouldBe(["ORG_STRUCTURE:READ"]);
    }

    [Fact]
    public void Deactivate_an_already_inactive_role_is_idempotent_and_raises_nothing()
    {
        var role = Role.Create(Guid.CreateVersion7(), "MOBILE_SCANNER", new Dictionary<string, string> { ["de"] = "x" }, null).Value;
        role.Deactivate(currentPermissionCodes: []);
        role.ClearDomainEvents();

        var result = role.Deactivate(currentPermissionCodes: []);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        role.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_with_no_actual_change_raises_nothing()
    {
        var captionI18n = new Dictionary<string, string> { ["de"] = "x" };
        var role = Role.Create(Guid.CreateVersion7(), "MOBILE_SCANNER", captionI18n, "desc").Value;
        role.ClearDomainEvents();

        var changed = role.Update(new Dictionary<string, string> { ["de"] = "x" }, "desc", currentPermissionCodes: []);

        changed.ShouldBeFalse();
        role.DomainEvents.ShouldBeEmpty();
    }
}
