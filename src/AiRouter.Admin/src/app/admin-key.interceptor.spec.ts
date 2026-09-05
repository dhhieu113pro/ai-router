import { HttpRequest } from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { adminKeyInterceptor } from './admin-key.interceptor';
import { AdminSessionService } from './admin-session.service';

async function intercept(hasKey: boolean, value: string) {
  const session = { hasKey, value };
  const injector = Injector.create({ providers: [{ provide: AdminSessionService, useValue: session }] });
  const next = vi.fn(request => of(request));
  const request = new HttpRequest('GET', '/providers');

  await firstValueFrom(runInInjectionContext(injector, () => adminKeyInterceptor(request, next)));
  return next.mock.calls[0][0] as HttpRequest<unknown>;
}

describe('adminKeyInterceptor', () => {
  it('leaves requests untouched while the admin session is locked', async () => {
    const request = await intercept(false, '');
    expect(request.headers.has('Authorization')).toBe(false);
  });

  it('adds the bearer admin key after unlock', async () => {
    const request = await intercept(true, 'secret');
    expect(request.headers.get('Authorization')).toBe('Bearer secret');
  });
});
