import { HttpInterceptorFn } from '@angular/common/http';

export const CORRELATION_ID_HEADER = 'X-Correlation-Id';

/**
 * Stamps every outgoing request with a fresh `X-Correlation-Id`, so a failed request can be
 * traced across the gateway and the services it reaches (ficha 0.5, track F point 2).
 */
export const correlationInterceptor: HttpInterceptorFn = (req, next) => {
  const correlationId = crypto.randomUUID();
  // Intentional, discreet debugging aid (ficha 0.5 point 2): lets a developer correlate a
  // failed request with the id a service logs, without any UI for it.
  console.debug(`[nexus] ${CORRELATION_ID_HEADER}=${correlationId} ${req.method} ${req.url}`);
  return next(
    req.clone({
      setHeaders: { [CORRELATION_ID_HEADER]: correlationId },
    }),
  );
};
