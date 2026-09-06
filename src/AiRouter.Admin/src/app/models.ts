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
  inputPricePerMillion?: number | null;
  cachedInputPricePerMillion?: number | null;
  outputPricePerMillion?: number | null;
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
  strategy: 0 | 1 | 2;
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

export interface ProviderUsage {
  inputTokens: number | null;
  outputTokens: number | null;
  totalTokens: number | null;
  cachedInputTokens: number | null;
  cacheWriteTokens: number | null;
  reportedCost: number | null;
}

export interface RouterTelemetryGroup {
  key: string;
  requestCount: number;
  successCount: number;
  errorCount: number;
  averageLatencyMs: number;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  cacheRatio: number | null;
  cacheCoverageCount: number;
  cacheCoveragePercentage: number;
  totalCost: number;
}

export interface RouterTelemetrySummary extends Omit<RouterTelemetryGroup, 'key'> {
  providers: RouterTelemetryGroup[];
  routes: RouterTelemetryGroup[];
}

export interface RouterTelemetryRecord {
  timestamp: string;
  routeId: string;
  providerId: string | null;
  model: string | null;
  strategy: 0 | 1 | 2;
  pinned: boolean;
  sticky: boolean;
  fallbackOccurred: boolean;
  affinityClassification: string;
  attemptCount: number;
  latency: string;
  usage: ProviderUsage | null;
  cost: number | null;
  costSource: string | null;
  success: boolean;
  statusCode: number;
  failureKind: number;
}

export interface CacheProbeAttempt {
  index: number;
  success: boolean;
  statusCode: number;
  providerId: string | null;
  model: string | null;
  latencyMs: number;
  usage: ProviderUsage | null;
  cost: number | null;
  costSource: string | null;
  affinity: string;
  fallbackOccurred: boolean;
  attemptCount: number;
}

export interface CacheProbeResult {
  repeats: number;
  attempts: CacheProbeAttempt[];
  targetChanged: boolean;
  cacheRatio: number | null;
  diagnostics: string[];
  recommendation: string | null;
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
