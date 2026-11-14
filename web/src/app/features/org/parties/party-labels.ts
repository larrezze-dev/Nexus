import type { PartyListItem } from '../../../api/platform/models/party-list-item';

/** i18n key of a party type («Organization», «Person»); never the API literal on screen. */
export function partyTypeKey(type: PartyListItem['partyType'] | null | undefined): string {
  return type === 'PERSON' ? 'org.parties.type.PERSON' : 'org.parties.type.ORGANIZATION';
}

/** i18n key of the scope of a party («Internal» / «External»); never `true`/`false` on screen. */
export function partyScopeKey(isInternal: boolean | null | undefined): string {
  return isInternal ? 'org.parties.scope.internal' : 'org.parties.scope.external';
}
