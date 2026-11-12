/**
 * One entry of the side navigation, built dynamically from `Route.data`
 * (`titleKey`, `icon`, `permission`, `navGroup`) — see `conventions-frontend.md §4` /
 * ficha 0.5 track F point 4. Never hardcode a business route here.
 */
export interface NxNavItem {
  readonly titleKey: string;
  readonly path: string;
  readonly icon?: string;
  readonly permission?: string;
  /** Section group this item belongs to (`NX_NAV_GROUPS` key); `undefined` = ungrouped. */
  readonly navGroup?: NxNavGroupId;
  readonly children?: readonly NxNavItem[];
}

export type NxNavGroupId = 'org' | 'people' | 'access';

/** The navigation items of one section group, in display order. */
export interface NxNavGroup {
  readonly navGroup: NxNavGroupId;
  readonly titleKey: string;
  readonly items: readonly NxNavItem[];
}

/** Section groups in display order, with the i18n key of their heading. */
export const NX_NAV_GROUPS: readonly { readonly id: NxNavGroupId; readonly titleKey: string }[] = [
  { id: 'org', titleKey: 'nav.groups.org' },
  { id: 'people', titleKey: 'nav.groups.people' },
  { id: 'access', titleKey: 'nav.groups.access' },
];

export function navGroupTitleKey(id: string | undefined): string | undefined {
  return NX_NAV_GROUPS.find((group) => group.id === id)?.titleKey;
}
