import { InjectionToken } from '@angular/core';

/** Current instant in epoch milliseconds. Inject instead of calling `Date.now()` so tests can pin "now". */
export const NX_NOW = new InjectionToken<() => number>('NX_NOW', { providedIn: 'root', factory: () => () => Date.now() });
