/**
 * One entry of the side navigation, built dynamically from `Route.data`
 * (`titleKey`, `icon`, `permission`) — see `conventions-frontend.md §4` /
 * ficha 0.5 track F point 4. Never hardcode a business route here.
 */
export interface NxNavItem {
  readonly titleKey: string;
  readonly path: string;
  readonly icon?: string;
  readonly permission?: string;
  readonly children?: readonly NxNavItem[];
}
