import { HttpClient } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from './api.service';

function createApi(response: unknown) {
  const http = {
    get: vi.fn().mockReturnValue(of(response))
  } as unknown as HttpClient;
  return { api: new ApiService(http), http };
}

describe('ApiService provider health', () => {
  it.each([
    [0, 'Healthy'],
    [1, 'Degraded'],
    [2, 'CoolingDown'],
    [3, 'Disabled']
  ])('normalizes API status %s to %s', async (status, expected) => {
    const { api } = createApi({ status, consecutiveFailures: 0 });

    const health = await firstValueFrom(api.providerHealth('provider-a'));

    expect(health.status).toBe(expected);
  });

  it('keeps unknown health values readable', async () => {
    const { api } = createApi({ status: 99, consecutiveFailures: 1 });

    const health = await firstValueFrom(api.providerHealth('provider-a'));

    expect(health.status).toBe('Unknown');
  });
});
