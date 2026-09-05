import { ProviderDefinition, ProviderEditor } from './models';

export function blankProviderEditor(): ProviderEditor {
  return {
    id: '',
    name: '',
    type: 'openai-compatible',
    baseUrl: '',
    apiKey: '',
    enabled: true,
    priority: 100,
    timeoutSeconds: 120,
    modelsText: '',
    defaultModel: '',
    discoverModels: true,
    extraHeadersText: '',
    chatEndpoint: '',
    responsesEndpoint: '',
    modelsEndpoint: '',
    supportsNativeResponses: true
  };
}

export function editorFromProvider(provider: ProviderDefinition): ProviderEditor {
  return {
    id: provider.id,
    name: provider.name,
    type: provider.type,
    baseUrl: provider.baseUrl,
    apiKey: '',
    enabled: provider.enabled,
    priority: provider.priority,
    timeoutSeconds: timeSpanToSeconds(provider.timeout),
    modelsText: (provider.models ?? []).join('\n'),
    defaultModel: provider.defaultModel ?? '',
    discoverModels: provider.discoverModels,
    extraHeadersText: provider.extraHeaders ? JSON.stringify(provider.extraHeaders, null, 2) : '',
    chatEndpoint: provider.chatEndpoint ?? '',
    responsesEndpoint: provider.responsesEndpoint ?? '',
    modelsEndpoint: provider.modelsEndpoint ?? '',
    supportsNativeResponses: provider.supportsNativeResponses
  };
}

export function providerFromEditor(editor: ProviderEditor): ProviderDefinition {
  return {
    id: editor.id.trim(),
    name: editor.name.trim(),
    type: editor.type.trim(),
    baseUrl: editor.baseUrl.trim(),
    apiKey: editor.apiKey.trim() || null,
    enabled: editor.enabled,
    priority: Number(editor.priority),
    timeout: secondsToTimeSpan(editor.timeoutSeconds),
    models: splitLines(editor.modelsText),
    defaultModel: editor.defaultModel.trim() || null,
    discoverModels: editor.discoverModels,
    extraHeaders: parseHeaders(editor.extraHeadersText),
    chatEndpoint: editor.chatEndpoint.trim() || null,
    responsesEndpoint: editor.responsesEndpoint.trim() || null,
    modelsEndpoint: editor.modelsEndpoint.trim() || null,
    supportsNativeResponses: editor.supportsNativeResponses
  };
}

export function secondsToTimeSpan(value: number | null): string | null {
  if (value === null || !Number.isFinite(value) || value <= 0) {
    return null;
  }

  const total = Math.round(value);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

export function timeSpanToSeconds(value: string | null): number | null {
  if (!value) {
    return 120;
  }

  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value);
  if (!match) {
    return 120;
  }

  return Number(match[1] ?? 0) * 86400 + Number(match[2]) * 3600 + Number(match[3]) * 60 + Number(match[4]);
}

function splitLines(value: string): string[] | null {
  const values = value.split(/[\n,]/).map(item => item.trim()).filter(Boolean);
  return values.length ? values : null;
}

function parseHeaders(value: string): Record<string, string> | null {
  if (!value.trim()) {
    return null;
  }

  const parsed: unknown = JSON.parse(value);
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
    throw new Error('Extra headers must be a JSON object.');
  }

  return Object.fromEntries(Object.entries(parsed).map(([key, entry]) => [key, String(entry)]));
}
