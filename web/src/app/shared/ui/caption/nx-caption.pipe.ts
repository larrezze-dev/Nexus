import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from './caption.util';

/**
 * Shows an `I18nText` caption in the active UI language (fallback chain in `resolveCaption`).
 *
 * Impure on purpose: it reads the `activeLang` signal inside `transform`, and Angular's change
 * detection re-evaluates impure pipes, so templates follow language switches. Usage:
 * `{{ row.captionI18n | nxCaption: row.code }}`.
 */
@Pipe({ name: 'nxCaption', pure: false })
export class NxCaptionPipe implements PipeTransform {
  private readonly i18n = inject(NxI18nService);

  transform(caption: I18NText | null | undefined, fallback = ''): string {
    return resolveCaption(caption, this.i18n.activeLang(), fallback);
  }
}
