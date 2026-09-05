export interface ProviderDefinition {
  id: string;
  name: string;
  type: string;
  baseUrl: string;
  apiKey: string | null;
  enabled: boolean;
  priority: number;
  timeout: string | null;
  models: string[] | null;
  defaultModel: string | null;
  discoverModels: boolean;
  extraHeaders: Record<string, string> | null;
  chatEndpoint: string | null;
  responsesEndpoint: string | null;
  modelsEndpoint: string | null;
  supportsNativeResponses: boolean;
}

export interface ProviderHealth {
  status: string;
  consecutiveFailures: number;
  cooldownUntil?: string | null;
  lastRequestAt?: string | null;
  lastSuccessAt?: string | null;
  lastFailureAt?: string | null;
  lastError?: string | null;
  lastLatency?: string | null;
}

export interface RouteTarget {
  providerId: string;
  model: string;
  priority: number;
  enabled: boolean;
}

export interface RouteDefinition {
  id: string;
  strategy: 0 | 1;
  targets: RouteTarget[];
  enabled: boolean;
}

export interface ConfigurationDocument {
  schemaVersion: number;
  providers: ProviderDefinition[];
  routes: RouteDefinition[];
}

export interface ImportResult {
  mode: string;
  providersUpserted: number;
  providersDeleted: number;
  routesUpserted: number;
  routesDeleted: number;
}

export interface ProviderEditor {
  id: string;
  name: string;
  type: string;
  baseUrl: string;
  apiKey: string;
  enabled: boolean;
  priority: number;
  timeoutSeconds: number | null;
  modelsText: string;
  defaultModel: string;
  discoverModels: boolean;
  extraHeadersText: string;
  chatEndpoint: string;
  responsesEndpoint: string;
  modelsEndpoint: string;
  supportsNativeResponses: boolean;
}
