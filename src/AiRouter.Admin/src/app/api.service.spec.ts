import { HttpClient } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from './api.service';
import { ConfigurationDocument, ProviderDefinition, RouteDefinition } from './models';

function provider(id = 'provider a'): ProviderDefinition {
  return {
    id,
    name: 'Provider',
    type: 'openai-compatible',
    baseUrl: 'https://example.test/v1/',
    apiKey: null,
    enabled: true,
    priority: 100,
    timeout: null,
    models: null,
    defaultModel: null,
    discoverModels: true,
    extraHeaders: null,
    chatEndpoint: null,
    responsesEndpoint: null,
    modelsEndpoint: null,
    supportsNativeResponses: true
  };
}

function route(id = 'route a'): RouteDefinition {
  return { id, strategy: 0, enabled: true, targets: [] };
}

function createHttp(response: unknown = {}) {
  const http = {
    get: vi.fn().mockReturnValue(of(response)),
    post: vi.fn().mockReturnValue(of(response)),
    put: vi.fn().mockReturnValue(of(response)),
    delete: vi.fn().mockReturnValue(of(response))
  };
  return { api: new ApiService(http as unknown as HttpClient), http };
}

describe('ApiService', () => {
  it('maps every provider and route management operation to the expected endpoint', () => {
    const { api, http } = createHttp();
    const currentProvider = provider();
    const currentRoute = route();

    api.listProviders();
    api.addProvider(currentProvider);
    api.updateProvider(currentProvider);
    api.deleteProvider(currentProvider.id);
    api.setProviderEnabled(currentProvider.id, true);
    api.setProviderEnabled(currentProvider.id, false);
    api.testProvider(currentProvider.id);
    api.providerModels(currentProvider.id);
    api.listRoutes();
    api.addRoute(currentRoute);
    api.updateRoute(currentRoute);
    api.deleteRoute(currentRoute.id);

    expect(http.get).toHaveBeenCalledWith('/providers');
    expect(http.post).toHaveBeenCalledWith('/providers', currentProvider);
    expect(http.put).toHaveBeenCalledWith('/providers/provider%20a', currentProvider);
    expect(http.delete).toHaveBeenCalledWith('/providers/provider%20a');
    expect(http.post).toHaveBeenCalledWith('/providers/provider%20a/enable', {});
    expect(http.post).toHaveBeenCalledWith('/providers/provider%20a/disable', {});
    expect(http.post).toHaveBeenCalledWith('/providers/provider%20a/test', {});
    expect(http.get).toHaveBeenCalledWith('/providers/provider%20a/models');
    expect(http.get).toHaveBeenCalledWith('/routes');
    expect(http.post).toHaveBeenCalledWith('/routes', currentRoute);
    expect(http.put).toHaveBeenCalledWith('/routes/route%20a', currentRoute);
    expect(http.delete).toHaveBeenCalledWith('/routes/route%20a');
  });

  it('maps configuration import and export requests', () => {
    const { api, http } = createHttp();
    const document: ConfigurationDocument = { schemaVersion: 1, providers: [], routes: [] };

    api.exportConfiguration(false);
    api.exportConfiguration(true);
    api.importConfiguration(document, 'merge');
    api.importConfiguration(document, 'replace');

    expect(http.get).toHaveBeenCalledWith('/config/export?includeSecrets=false');
    expect(http.get).toHaveBeenCalledWith('/config/export?includeSecrets=true');
    expect(http.post).toHaveBeenCalledWith('/config/import?mode=merge', document);
    expect(http.post).toHaveBeenCalledWith('/config/import?mode=replace', document);
  });

  it.each([
    [0, 'Healthy'],
    [1, 'Degraded'],
    [2, 'CoolingDown'],
    [3, 'Disabled'],
    [99, 'Unknown'],
    ['Healthy', 'Healthy'],
    ['', 'Unknown']
  ])('normalizes provider health status %s to %s', async (status, expected) => {
    const { api, http } = createHttp({ status, consecutiveFailures: 1 });

    const health = await firstValueFrom(api.providerHealth('provider a'));

    expect(http.get).toHaveBeenCalledWith('/providers/provider%20a/health');
    expect(health).toEqual({ status: expected, consecutiveFailures: 1 });
  });
});
