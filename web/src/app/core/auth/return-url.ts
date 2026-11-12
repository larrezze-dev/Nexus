import { inject } from '@angular/core';
import { Router, type UrlTree } from '@angular/router';

const STORAGE_KEY = 'nx.auth.returnUrl';
/** A saved URL older than this is ignored (an abandoned sign-in must not hijack a later visit). */
const MAX_AGE_MS = 10 * 60 * 1000;
/** Routes that must never be a post-login destination. */
const EXCLUDED_PATHS = new Set(['/', '/login', '/unauthorized']);

/**
 * Only same-origin, app-internal paths may be a return target (open-redirect guard): a single
 * leading `/`, no protocol-relative `//host`, no backslashes (browsers read `/\host` as `//host`),
 * no control characters and no scheme.
 */
export function isSafeReturnUrl(url: unknown): url is string {
  if (typeof url !== 'string' || url.length === 0 || url.length > 2048) {
    return false;
  }
  if (!url.startsWith('/') || url.startsWith('//') || url.includes('\\')) {
    return false;
  }
  if (/[\u0000-\u001f\u007f]/.test(url)) {
    return false;
  }
  const path = url.split(/[?#]/, 1)[0];
  return !EXCLUDED_PATHS.has(path);
}

/** Remembers where an unauthenticated user was going, before the redirect to the IdP. */
export function saveReturnUrl(url: unknown, now: number = Date.now()): void {
  if (!isSafeReturnUrl(url)) {
    return;
  }
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ url, savedAt: now }));
  } catch {
    // Storage unavailable (private mode quota…): fall back to the default landing page.
  }
}

/** Returns the saved URL once (and forgets it); `null` if none, expired or unsafe. */
export function consumeReturnUrl(now: number = Date.now()): string | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    sessionStorage.removeItem(STORAGE_KEY);
    if (raw === null) {
      return null;
    }
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) {
      return null;
    }
    const { url, savedAt } = parsed as { url?: unknown; savedAt?: unknown };
    if (typeof savedAt !== 'number' || now - savedAt > MAX_AGE_MS || !isSafeReturnUrl(url)) {
      return null;
    }
    return url;
  } catch {
    return null;
  }
}

/** `redirectTo` of the shell's empty path: the saved deep link, else `/account`. */
export function postLoginRedirect(): UrlTree {
  const router = inject(Router);
  return router.parseUrl(consumeReturnUrl() ?? '/account');
}
