import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

export const ACCEPT_LANGUAGE_HEADER = 'Accept-Language';

/**
 * Stamps every outgoing request with the currently active UI language, so services that
 * localize their own output (e.g. validation messages) match what the user sees
 * (ficha 0.5, track F point 2).
 */
export const acceptLanguageInterceptor: HttpInterceptorFn = (req, next) => {
  const transloco = inject(TranslocoService);
  return next(
    req.clone({
      setHeaders: { [ACCEPT_LANGUAGE_HEADER]: transloco.getActiveLang() },
    }),
  );
};
