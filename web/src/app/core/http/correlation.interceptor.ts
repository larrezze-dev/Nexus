import { HttpInterceptorFn } from '@angular/common/http';

export const CORRELATION_ID_HEADER = 'X-Correlation-Id';

/**
 * Stamps outgoing requests to our own API with a fresh `X-Correlation-Id`, so a failed request
 * can be traced across the gateway and the services it reaches (ficha 0.5, track F point 2).
 * Cross-origin requests (e.g. the OIDC provider's discovery/token/JWKS endpoints) are left
 * untouched: the custom header would fail their CORS preflight.
 */
function isOwnApiRequest(url: string): boolean {
  if (!/^[a-z][a-z0-9+.-]*:/i.test(url) && !url.startsWith('//')) {
    return true; // relative URL: goes through the dev proxy / gateway
  }
  try {
    return new URL(url, window.location.origin).origin === window.location.origin;
  } catch {
    return false;
  }
}

export const correlationInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isOwnApiRequest(req.url)) {
    return next(req);
  }
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
