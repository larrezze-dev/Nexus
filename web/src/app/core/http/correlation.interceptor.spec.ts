import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { CORRELATION_ID_HEADER, correlationInterceptor } from './correlation.interceptor';

describe('correlationInterceptor', () => {
  it('adds a X-Correlation-Id header with a UUID to every outgoing request', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);

    http.get('/v1/asset-types').subscribe();

    const req = controller.expectOne('/v1/asset-types');
    const correlationId = req.request.headers.get(CORRELATION_ID_HEADER);
    expect(correlationId).toBeTruthy();
    expect(correlationId).toMatch(/^[0-9a-f-]{36}$/i);

    req.flush({});
    controller.verify();
  });

  it('generates a different id for each request', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);

    http.get('/v1/a').subscribe();
    http.get('/v1/b').subscribe();

    const [first, second] = controller.match(() => true);
    const firstId = first.request.headers.get(CORRELATION_ID_HEADER);
    const secondId = second.request.headers.get(CORRELATION_ID_HEADER);
    expect(firstId).not.toEqual(secondId);

    first.flush({});
    second.flush({});
    controller.verify();
  });
});
