import {
  Directive,
  EmbeddedViewRef,
  Input,
  TemplateRef,
  ViewContainerRef,
  effect,
  inject,
} from '@angular/core';
import { PermissionsStore } from './permissions.store';

/**
 * Structural directive: `*nxCan="'ASSET_TYPE:CREATE'"` shows its content
 * only when `PermissionsStore.has('ASSET_TYPE:CREATE')` is true.
 *
 * This is solely cosmetic (`conventions-frontend.md §6`: "Es solo
 * cosmética: el servidor decide"). It hides buttons/sections the user has
 * no permission for; it is NOT a security boundary — the API enforces the
 * real permission check on every request regardless of what this directive
 * shows or hides.
 *
 * In step 0.5, `PermissionsStore` is always empty, so `has()` returns
 * `true` for every permission and this directive lets everything through
 * (see `permissions.store.ts`). Step 0.6 hardens this.
 */
@Directive({
  selector: '[nxCan]',
})
export class NxCanDirective {
  private readonly templateRef = inject<TemplateRef<unknown>>(TemplateRef);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly permissionsStore = inject(PermissionsStore);

  private permission: string | null = null;
  private viewRef: EmbeddedViewRef<unknown> | null = null;

  constructor() {
    effect(() => {
      // Reading the signal here makes the effect re-run whenever
      // permissions change (relevant from 0.6 onward).
      this.permissionsStore.permissions();
      this.updateView();
    });
  }

  /**
   * `null`/`undefined` means "no permission required" (e.g. a nav item every authenticated
   * user may see): the directive then always shows its content, regardless of
   * `PermissionsStore`.
   */
  @Input({ required: true })
  set nxCan(permission: string | null | undefined) {
    this.permission = permission ?? null;
    this.updateView();
  }

  private updateView(): void {
    const canShow = this.permission === null || this.permissionsStore.has(this.permission);

    if (canShow && !this.viewRef) {
      this.viewRef = this.viewContainerRef.createEmbeddedView(this.templateRef);
      return;
    }

    if (!canShow && this.viewRef) {
      this.viewContainerRef.clear();
      this.viewRef = null;
    }
  }
}
