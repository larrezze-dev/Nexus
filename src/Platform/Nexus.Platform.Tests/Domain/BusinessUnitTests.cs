using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.Parties;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

public sealed class BusinessUnitTests
{
    [Fact(DisplayName = "ORG-R1: A BusinessUnit's party must be an internal ORGANIZATION")]
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

        var result = BusinessUnit.Create(
            Guid.CreateVersion7(),
            party,
            legalEntityId: Guid.CreateVersion7(),
            code: "BU01",
            captionI18n: new Dictionary<string, string> { ["de"] = "Test" },
            costCenterCode: null);

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

        var result = BusinessUnit.Create(
            Guid.CreateVersion7(),
            party,
            legalEntityId: Guid.CreateVersion7(),
            code: "BU01",
            captionI18n: new Dictionary<string, string> { ["de"] = "Test" },
            costCenterCode: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PartyId.ShouldBe(party.Id);
    }
}
