import { HttpClient } from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { firstValueFrom, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from './admin-session.service';
import { ApiService } from './api.service';
import { AppComponent } from './app.component';
import { CacheProbeResult, RouterTelemetryRecord, RouterTelemetrySummary } from './models';

const summary: RouterTelemetrySummary = {
  requestCount: 2,
  successCount: 2,
  errorCount: 0,
  averageLatencyMs: 12,
  inputTokens: 100,
  outputTokens: 20,
  cachedInputTokens: 50,
  cacheRatio: 0.5,
  cacheCoverageCount: 2,
  cacheCoveragePercentage: 100,
  totalCost: 0.01,
  providers: [],
  routes: []
};

const recent: RouterTelemetryRecord[] = [];
const probe: CacheProbeResult = {
  repeats: 3,
  attempts: [],
  targetChanged: false,
  cacheRatio: 0.5,
  diagnostics: [],
  recommendation: null
};

class FakeSession {
  value = '';
  get hasKey(): boolean { return this.value.length > 0; }
  set(value: string): void { this.value = value.trim(); }
  clear(): void { this.value = ''; }
}

function createApi() {
  return {
    listProviders: vi.fn(() => of([])),
    listRoutes: vi.fn(() => of([])),
    providerHealth: vi.fn(() => of({ status: 'Healthy', consecutiveFailures: 0 })),
    telemetrySummary: vi.fn(() => of(summary)),
    telemetryRecent: vi.fn(() => of(recent)),
    probeCache: vi.fn(() => of(probe))
  };
}

describe('cache observability', () => {
  let api: ReturnType<typeof createApi>;
  let app: AppComponent;

  beforeEach(() => {
    vi.useFakeTimers();
    api = createApi();
    const injector = Injector.create({ providers: [
      { provide: ApiService, useValue: api },
      { provide: AdminSessionService, useValue: new FakeSession() }
    ] });
    app = runInInjectionContext(injector, () => new AppComponent());
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('maps telemetry and probe API calls to their endpoints', async () => {
    const http = {
      get: vi.fn((url: string) => of(url === '/telemetry/summary' ? summary : recent)),
      post: vi.fn(() => of(probe))
    };
    const service = new ApiService(http as unknown as HttpClient);

    expect(await firstValueFrom(service.telemetrySummary())).toBe(summary);
    expect(await firstValueFrom(service.telemetryRecent())).toBe(recent);
    expect(await firstValueFrom(service.probeCache('coding', { messages: [] }))).toBe(probe);
    expect(await firstValueFrom(service.probeCache('coding', {}, 5))).toBe(probe);

    expect(http.get).toHaveBeenCalledWith('/telemetry/summary');
    expect(http.get).toHaveBeenCalledWith('/telemetry/recent');
    expect(http.post).toHaveBeenCalledWith('/probe/cache', { model: 'coding', request: { messages: [] }, repeats: 3 });
    expect(http.post).toHaveBeenCalledWith('/probe/cache', { model: 'coding', request: {}, repeats: 5 });
  });

  it('opens cache tab and loads telemetry', async () => {
    await app.openCache();
    expect(app.activeTab).toBe('cache');
    expect(app.telemetrySummary).toBe(summary);
    expect(app.telemetryRecent).toBe(recent);
  });

  it('reports telemetry failures unless refresh is silent', async () => {
    api.telemetrySummary.mockReturnValue(throwError(() => new Error('telemetry failed')));

    await app.refreshTelemetry(false);
    expect(app.toast).toBe('telemetry failed');

    app.toast = '';
    await app.refreshTelemetry(true);
    expect(app.toast).toBe('');
  });

  it('validates cache probe model and JSON before sending', async () => {
    app.probeModel = '   ';
    await app.runCacheProbe();
    expect(app.toast).toContain('Choose a route');
    expect(api.probeCache).not.toHaveBeenCalled();

    app.probeModel = 'coding';
    app.probeRequestText = '{bad';
    await app.runCacheProbe();
    expect(app.toast).toContain('valid JSON');
    expect(api.probeCache).not.toHaveBeenCalled();
  });

  it('runs cache probe, trims model, coerces repeats and silently refreshes telemetry', async () => {
    app.probeModel = ' coding ';
    app.probeRepeats = 4;
    app.probeRequestText = '{"messages":[]}';
    const refresh = vi.spyOn(app, 'refreshTelemetry').mockResolvedValue();

    await app.runCacheProbe();

    expect(api.probeCache).toHaveBeenCalledWith('coding', { messages: [] }, 4);
    expect(app.probeResult).toBe(probe);
    expect(refresh).toHaveBeenCalledWith(true);
    expect(app.probeBusy).toBe(false);
  });

  it('reports cache probe failures and always clears busy state', async () => {
    app.probeModel = 'coding';
    app.probeRequestText = '{}';
    api.probeCache.mockReturnValue(throwError(() => new Error('probe failed')));

    await app.runCacheProbe();

    expect(app.toast).toBe('probe failed');
    expect(app.probeBusy).toBe(false);
  });

  it('formats strategy, ratio and cost display values', () => {
    expect(app.routeStrategyName(0)).toBe('Fallback');
    expect(app.routeStrategyName(1)).toBe('Round robin');
    expect(app.routeStrategyName(2)).toBe('Sticky');
    expect(app.ratio(null)).toBe('Unknown');
    expect(app.ratio(undefined)).toBe('Unknown');
    expect(app.ratio(0.125)).toBe('12.5%');
    expect(app.cost(null)).toBe('Unknown');
    expect(app.cost(undefined)).toBe('Unknown');
    expect(app.cost(0.1234567)).toBe('$0.123457');
  });

  it('chooses the initial probe model from routes, then providers, then empty', async () => {
    api.listRoutes.mockReturnValueOnce(of([{ id: 'route-x', strategy: 0, enabled: true, targets: [] }]));
    await app.refreshAll();
    expect(app.probeModel).toBe('route-x');

    app.probeModel = '';
    api.listRoutes.mockReturnValueOnce(of([]));
    api.listProviders.mockReturnValueOnce(of([{ id: 'provider-x', name: 'P', type: 'x', baseUrl: 'x', apiKey: null, enabled: true, priority: 1, timeout: null, models: null, defaultModel: null, discoverModels: false, extraHeaders: null, chatEndpoint: null, responsesEndpoint: null, modelsEndpoint: null, supportsNativeResponses: false }]));
    await app.refreshAll();
    expect(app.probeModel).toBe('provider-x');

    app.probeModel = '';
    api.listRoutes.mockReturnValueOnce(of([]));
    api.listProviders.mockReturnValueOnce(of([]));
    await app.refreshAll();
    expect(app.probeModel).toBe('');
  });
});
