using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

/// <summary><see cref="AppUser.ChangePreferredLanguage"/> (<c>PATCH /v1/me</c>).</summary>
public sealed class AppUserPreferredLanguageTests
{
    private static readonly Guid BusinessUnitId = Guid.CreateVersion7();

    [Fact(DisplayName = "EV-R4: changing the language raises one user.updated with the user's full state, nothing else changes")]
    public void Changing_the_language_raises_user_updated_with_full_state()
    {
        var user = NewUser();
        var versionBefore = user.AggregateVersion;

        var result = user.ChangePreferredLanguage("es");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        user.PreferredLanguage.ShouldBe("es");
        user.ExternalSubject.ShouldBe("oid-domain-test");
        user.Email.ShouldBe("domain@test.nexus.local");
        user.BusinessUnitId.ShouldBe(BusinessUnitId);
        user.IsActive.ShouldBeTrue();

        var updated = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserUpdated>();
        updated.ShouldBe(new UserUpdated(user.Id, user.PartyId, BusinessUnitId, "oid-domain-test", "es", true));
        updated.Topic.ShouldBe("nexus.org.user.updated");
        user.AggregateVersion.ShouldBe(versionBefore + 1);
    }

    [Fact(DisplayName = "UI-ME: setting the language the user already has is a no-op: no change, no event")]
    public void Setting_the_same_language_raises_nothing()
    {
        var user = NewUser();
        var versionBefore = user.AggregateVersion;

        var result = user.ChangePreferredLanguage("en");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        user.DomainEvents.ShouldBeEmpty();
        user.AggregateVersion.ShouldBe(versionBefore);
    }

    private static AppUser NewUser()
    {
        var party = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Person,
            legalName: "Domain Test",
            displayName: "Domain Test",
            taxId: null,
            erpCompanyCode: null,
            isInternal: true).Value;

        var user = AppUser.Create(Guid.CreateVersion7(), party, "oid-domain-test", "domain@test.nexus.local", "en", BusinessUnitId).Value;
        user.ClearDomainEvents();
        return user;
    }
}
