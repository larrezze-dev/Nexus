using System.Reflection;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

/// <summary><see cref="AppUser.EnsureCanBeDeactivatedBy"/>: ORG-R6 is checked before ACC-R13.</summary>
public sealed class AppUserDeactivationTests
{
    [Fact(DisplayName = "ACC-R13: a user cannot deactivate themselves")]
    public void Deactivating_oneself_fails_ACC_R13()
    {
        var user = CreateUser();

        var result = user.EnsureCanBeDeactivatedBy(user.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ACC-R13");
    }

    [Fact]
    public void Deactivating_another_user_is_allowed()
    {
        CreateUser().EnsureCanBeDeactivatedBy(Guid.CreateVersion7()).IsSuccess.ShouldBeTrue();
    }

    [Fact(DisplayName = "ORG-R6 before ACC-R13: the SYSTEM user deactivating itself reports ORG-R6, and nobody deactivates SYSTEM")]
    public void System_user_is_never_deactivated_and_ORG_R6_wins_over_ACC_R13()
    {
        var system = CreateUser();
        SetId(system, AppUser.SystemUserId);
        system.IsSystem.ShouldBeTrue();

        var byItself = system.EnsureCanBeDeactivatedBy(AppUser.SystemUserId);
        var byOther = system.EnsureCanBeDeactivatedBy(Guid.CreateVersion7());

        byItself.IsFailure.ShouldBeTrue();
        byItself.Error!.Code.ShouldBe("ORG-R6");
        byOther.IsFailure.ShouldBeTrue();
        byOther.Error!.Code.ShouldBe("ORG-R6");
    }

    private static AppUser CreateUser()
    {
        var party = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Person,
            legalName: "Domain Test",
            displayName: "Domain Test",
            taxId: null,
            erpCompanyCode: null,
            isInternal: true).Value;

        return AppUser.Create(Guid.CreateVersion7(), party, "oid-domain-test", "domain@test.nexus.local", "en", null).Value;
    }

    /// <summary>The SYSTEM user is only ever seeded; this mimics how EF Core materializes it.</summary>
    private static void SetId(AppUser user, Guid id)
    {
        for (var type = user.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                field.SetValue(user, id);
                return;
            }
        }

        throw new InvalidOperationException("Id backing field not found.");
    }
}
