using Nexus.Platform.Domain.Parties;
using Shouldly;

namespace Nexus.Platform.Tests.Domain;

public sealed class PartyTests
{
    [Fact]
    public void Create_an_internal_organization_party_succeeds()
    {
        var result = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Organization,
            legalName: "Nüssli Group",
            displayName: "Nüssli",
            taxId: "CHE-123.456.789",
            erpCompanyCode: "1000",
            isInternal: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PartyType.ShouldBe(PartyType.Organization);
        result.Value.IsInternal.ShouldBeTrue();
    }

    [Fact(DisplayName = "ORG-R2: party_type and is_internal never change after creation")]
    public void Update_does_not_change_party_type_or_is_internal()
    {
        var party = Party.Create(
            Guid.CreateVersion7(),
            PartyType.Organization,
            legalName: "Nüssli Group",
            displayName: "Nüssli",
            taxId: "CHE-123.456.789",
            erpCompanyCode: "1000",
            isInternal: true).Value;

        party.Update(
            legalName: "Nüssli Group AG",
            displayName: "Nüssli AG",
            taxId: "CHE-987.654.321",
            erpCompanyCode: "2000");

        party.PartyType.ShouldBe(PartyType.Organization);
        party.IsInternal.ShouldBeTrue();
    }
}
