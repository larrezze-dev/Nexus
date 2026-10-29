using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Parties;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

public sealed class LegalEntityTests
{
    [Fact(DisplayName = "ORG-R1: A LegalEntity's party must be an internal ORGANIZATION")]
    public void Create_with_a_non_internal_party_fails_ORG_R1()
    {
        var party = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Person,
            legalName: "Jane Doe",
            displayName: null,
            taxId: null,
            erpCompanyCode: null,
            isInternal: false).Value;

        var result = LegalEntity.Create(
            Guid.CreateVersion7(),
            party,
            groupId: Guid.CreateVersion7(),
            code: "CH01",
            currencyCode: "CHF",
            countryCode: "CH");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("ORG-R1");
    }

    [Fact]
    public void Create_with_an_internal_organization_party_succeeds()
    {
        var party = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Organization,
            legalName: "Nüssli AG",
            displayName: null,
            taxId: null,
            erpCompanyCode: null,
            isInternal: true).Value;

        var result = LegalEntity.Create(
            Guid.CreateVersion7(),
            party,
            groupId: Guid.CreateVersion7(),
            code: "CH01",
            currencyCode: "CHF",
            countryCode: "CH");

        result.IsSuccess.ShouldBeTrue();
        result.Value.PartyId.ShouldBe(party.Id);
    }
}
