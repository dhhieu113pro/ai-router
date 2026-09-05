import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { ConfigurationDocument, ImportResult, ProviderDefinition, ProviderHealth, RouteDefinition } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private readonly http: HttpClient) {}

  listProviders() {
    return this.http.get<ProviderDefinition[]>('/providers');
  }

  addProvider(provider: ProviderDefinition) {
    return this.http.post<ProviderDefinition>('/providers', provider);
  }

  updateProvider(provider: ProviderDefinition) {
    return this.http.put<ProviderDefinition>(`/providers/${encodeURIComponent(provider.id)}`, provider);
  }

  deleteProvider(id: string) {
    return this.http.delete<void>(`/providers/${encodeURIComponent(id)}`);
  }

  setProviderEnabled(id: string, enabled: boolean) {
    return this.http.post<ProviderDefinition>(`/providers/${encodeURIComponent(id)}/${enabled ? 'enable' : 'disable'}`, {});
  }

  testProvider(id: string) {
    return this.http.post<{ success: boolean; error?: string | null }>(`/providers/${encodeURIComponent(id)}/test`, {});
  }

  providerModels(id: string) {
    return this.http.get<string[]>(`/providers/${encodeURIComponent(id)}/models`);
  }

  providerHealth(id: string) {
    return this.http.get<ProviderHealth>(`/providers/${encodeURIComponent(id)}/health`);
  }

  listRoutes() {
    return this.http.get<RouteDefinition[]>('/routes');
  }

  addRoute(route: RouteDefinition) {
    return this.http.post<RouteDefinition>('/routes', route);
  }

  updateRoute(route: RouteDefinition) {
    return this.http.put<RouteDefinition>(`/routes/${encodeURIComponent(route.id)}`, route);
  }

  deleteRoute(id: string) {
    return this.http.delete<void>(`/routes/${encodeURIComponent(id)}`);
  }

  exportConfiguration(includeSecrets: boolean) {
    return this.http.get<ConfigurationDocument>(`/config/export?includeSecrets=${includeSecrets}`);
  }

  importConfiguration(document: ConfigurationDocument, mode: 'merge' | 'replace') {
    return this.http.post<ImportResult>(`/config/import?mode=${mode}`, document);
  }
}
