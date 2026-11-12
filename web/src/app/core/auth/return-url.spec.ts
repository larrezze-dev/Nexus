import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { consumeReturnUrl, isSafeReturnUrl, postLoginRedirect, saveReturnUrl } from './return-url';

describe('return-url', () => {
  beforeEach(() => sessionStorage.clear());

  it.each(['/warehouses', '/legal-entities/123?tab=audit#x', '/org/tree'])('accepts %s', (url) => {
    expect(isSafeReturnUrl(url)).toBe(true);
  });

  it.each([
    'https://evil.example/x',
    '//evil.example',
    '/\\evil.example',
    'warehouses',
    'javascript:alert(1)',
    '/a\nb',
    '/',
    '/login',
    '/unauthorized?x=1',
    '',
    null,
    42,
  ])('rejects %s (open-redirect guard)', (url) => {
    expect(isSafeReturnUrl(url)).toBe(false);
  });

  it('round-trips a deep link exactly once', () => {
    saveReturnUrl('/warehouses?q=a');
    expect(consumeReturnUrl()).toBe('/warehouses?q=a');
    expect(consumeReturnUrl()).toBeNull();
  });

  it('does not store an unsafe url', () => {
    saveReturnUrl('//evil.example');
    expect(consumeReturnUrl()).toBeNull();
  });

  it('ignores a saved url older than ten minutes', () => {
    saveReturnUrl('/warehouses', 1_000);
    expect(consumeReturnUrl(1_000 + 10 * 60 * 1000 + 1)).toBeNull();
  });

  it('ignores a tampered storage value', () => {
    sessionStorage.setItem(
      'nx.auth.returnUrl',
      JSON.stringify({ url: 'https://evil.example', savedAt: Date.now() }),
    );
    expect(consumeReturnUrl()).toBeNull();
    sessionStorage.setItem('nx.auth.returnUrl', 'not json');
    expect(consumeReturnUrl()).toBeNull();
  });

  describe('postLoginRedirect', () => {
    beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

    it('goes back to the saved deep link, keeping its query string', () => {
      saveReturnUrl('/warehouses?q=a');
      const tree = TestBed.runInInjectionContext(() => postLoginRedirect());
      expect(TestBed.inject(Router).serializeUrl(tree)).toBe('/warehouses?q=a');
    });

    it('falls back to /account without a saved url', () => {
      const tree = TestBed.runInInjectionContext(() => postLoginRedirect());
      expect(TestBed.inject(Router).serializeUrl(tree)).toBe('/account');
    });
  });
});
