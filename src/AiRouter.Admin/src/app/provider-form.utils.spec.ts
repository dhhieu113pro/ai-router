import { describe, expect, it } from 'vitest';
import { blankProviderEditor, editorFromProvider, providerFromEditor, secondsToTimeSpan, timeSpanToSeconds } from './provider-form.utils';
import { ProviderDefinition } from './models';

function provider(overrides: Partial<ProviderDefinition> = {}): ProviderDefinition {
  return {
    id: 'primary',
    name: 'Primary',
    type: 'openai-compatible',
    baseUrl: 'https://example.test/v1/',
    apiKey: 'secret',
    enabled: true,
    priority: 10,
    timeout: '01:02:03',
    models: ['model-a'],
    defaultModel: 'model-a',
    discoverModels: false,
    extraHeaders: null,
    chatEndpoint: null,
    responsesEndpoint: null,
    modelsEndpoint: null,
    supportsNativeResponses: true,
    ...overrides
  };
}

describe('provider form utilities', () => {
  it('creates the expected provider defaults', () => {
    expect(blankProviderEditor()).toEqual({
      id: '', name: '', type: 'openai-compatible', baseUrl: '', apiKey: '', enabled: true, priority: 100,
      timeoutSeconds: 120, modelsText: '', defaultModel: '', discoverModels: true, extraHeadersText: '',
      chatEndpoint: '', responsesEndpoint: '', modelsEndpoint: '', supportsNativeResponses: true
    });
  });

  it('sends blank optional values as null so edits preserve the existing secret', () => {
    const editor = blankProviderEditor();
    editor.id = ' primary ';
    editor.name = ' Primary ';
    editor.type = ' openai-compatible ';
    editor.baseUrl = ' https://example.test/v1/ ';
    editor.apiKey = '   ';
    editor.timeoutSeconds = null;

    const result = providerFromEditor(editor);
    expect(result.id).toBe('primary');
    expect(result.name).toBe('Primary');
    expect(result.type).toBe('openai-compatible');
    expect(result.baseUrl).toBe('https://example.test/v1/');
    expect(result.apiKey).toBeNull();
    expect(result.timeout).toBeNull();
    expect(result.models).toBeNull();
    expect(result.defaultModel).toBeNull();
    expect(result.extraHeaders).toBeNull();
    expect(result.chatEndpoint).toBeNull();
    expect(result.responsesEndpoint).toBeNull();
    expect(result.modelsEndpoint).toBeNull();
  });

  it('normalizes populated models, headers, endpoints, secret and timeout', () => {
    const editor = blankProviderEditor();
    editor.id = 'primary';
    editor.name = 'Primary';
    editor.baseUrl = 'https://example.test/v1/';
    editor.apiKey = ' secret ';
    editor.timeoutSeconds = 125.4;
    editor.modelsText = 'model-a\nmodel-b, model-c';
    editor.defaultModel = ' model-a ';
    editor.extraHeadersText = '{"X-Tenant":42}';
    editor.chatEndpoint = ' chat ';
    editor.responsesEndpoint = ' responses ';
    editor.modelsEndpoint = ' models ';

    const result = providerFromEditor(editor);
    expect(result.apiKey).toBe('secret');
    expect(result.timeout).toBe('00:02:05');
    expect(result.models).toEqual(['model-a', 'model-b', 'model-c']);
    expect(result.defaultModel).toBe('model-a');
    expect(result.extraHeaders).toEqual({ 'X-Tenant': '42' });
    expect(result.chatEndpoint).toBe('chat');
    expect(result.responsesEndpoint).toBe('responses');
    expect(result.modelsEndpoint).toBe('models');
  });

  it('round trips populated provider fields without exposing its API key', () => {
    const editor = editorFromProvider(provider({
      extraHeaders: { 'X-Test': 'yes' },
      chatEndpoint: '/chat',
      responsesEndpoint: '/responses',
      modelsEndpoint: '/models'
    }));

    expect(editor.apiKey).toBe('');
    expect(editor.timeoutSeconds).toBe(3723);
    expect(editor.modelsText).toBe('model-a');
    expect(editor.defaultModel).toBe('model-a');
    expect(editor.extraHeadersText).toContain('X-Test');
    expect(editor.chatEndpoint).toBe('/chat');
    expect(editor.responsesEndpoint).toBe('/responses');
    expect(editor.modelsEndpoint).toBe('/models');
  });

  it('round trips nullable provider fields to empty editor values', () => {
    const editor = editorFromProvider(provider({
      timeout: null,
      models: null,
      defaultModel: null,
      extraHeaders: null,
      chatEndpoint: null,
      responsesEndpoint: null,
      modelsEndpoint: null
    }));

    expect(editor.timeoutSeconds).toBe(120);
    expect(editor.modelsText).toBe('');
    expect(editor.defaultModel).toBe('');
    expect(editor.extraHeadersText).toBe('');
    expect(editor.chatEndpoint).toBe('');
    expect(editor.responsesEndpoint).toBe('');
    expect(editor.modelsEndpoint).toBe('');
  });

  it('handles invalid and valid second values', () => {
    expect(secondsToTimeSpan(null)).toBeNull();
    expect(secondsToTimeSpan(Number.NaN)).toBeNull();
    expect(secondsToTimeSpan(0)).toBeNull();
    expect(secondsToTimeSpan(-1)).toBeNull();
    expect(secondsToTimeSpan(3661)).toBe('01:01:01');
  });

  it('handles default, invalid, day-based and fractional time spans', () => {
    expect(timeSpanToSeconds(null)).toBe(120);
    expect(timeSpanToSeconds('')).toBe(120);
    expect(timeSpanToSeconds('not-a-timespan')).toBe(120);
    expect(timeSpanToSeconds('01:02:03.500')).toBe(3723);
    expect(timeSpanToSeconds('1.01:00:00')).toBe(90000);
  });

  it.each(['null', '[]', '"text"'])('rejects non-object extra headers: %s', value => {
    const editor = blankProviderEditor();
    editor.id = 'primary';
    editor.name = 'Primary';
    editor.baseUrl = 'https://example.test/v1/';
    editor.extraHeadersText = value;

    expect(() => providerFromEditor(editor)).toThrow('Extra headers must be a JSON object.');
  });
});
