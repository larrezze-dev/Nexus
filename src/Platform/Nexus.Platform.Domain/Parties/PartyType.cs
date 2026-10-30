namespace Nexus.Platform.Domain.Parties;

/// <summary>
/// <c>org.party.party_type</c>. Stored and published as the upper-case names
/// (<c>ORGANIZATION</c>, <c>PERSON</c>; see <see cref="PartyTypeNames"/>), never as numbers.
/// Fixed at creation (ORG-R2).
/// </summary>
public enum PartyType
{
    Organization,
    Person,
}

/// <summary>Wire/storage spelling of <see cref="PartyType"/> (DDL CHECK, Avro, OpenAPI enum).</summary>
public static class PartyTypeNames
{
    public const string Organization = "ORGANIZATION";

    public const string Person = "PERSON";

    public static string ToName(this PartyType partyType) => partyType switch
    {
        PartyType.Organization => Organization,
        PartyType.Person => Person,
        _ => throw new ArgumentOutOfRangeException(nameof(partyType), partyType, "Unknown party type."),
    };

    public static PartyType Parse(string name) => name switch
    {
        Organization => PartyType.Organization,
        Person => PartyType.Person,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown party type."),
    };
}
