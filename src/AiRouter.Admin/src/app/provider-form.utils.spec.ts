import { describe, expect, it } from 'vitest';
import { blankProviderEditor, editorFromProvider, providerFromEditor, secondsToTimeSpan, timeSpanToSeconds } from './provider-form.utils';
import { ProviderDefinition } from './models';

describe('provider form utilities', () => {
  it('sends a blank API key as null so server updates preserve the existing secret', () => {
    const editor = blankProviderEditor();
    editor.id = 'primary';
    editor.name = 'Primary';
    editor.baseUrl = 'https://example.test/v1/';
    editor.apiKey = '   ';

    expect(providerFromEditor(editor).apiKey).toBeNull();
  });

  it('normalizes models, headers and timeout', () => {
    const editor = blankProviderEditor();
    editor.id = 'primary';
    editor.name = 'Primary';
    editor.baseUrl = 'https://example.test/v1/';
    editor.timeoutSeconds = 125;
    editor.modelsText = 'model-a\nmodel-b, model-c';
    editor.extraHeadersText = '{"X-Tenant": 42}';

    const provider = providerFromEditor(editor);
    expect(provider.timeout).toBe('00:02:05');
    expect(provider.models).toEqual(['model-a', 'model-b', 'model-c']);
    expect(provider.extraHeaders).toEqual({ 'X-Tenant': '42' });
  });

  it('round trips a provider into an editor without exposing its key', () => {
    const provider: ProviderDefinition = {
      id: 'primary',
      name: 'Primary',
      type: 'openai-compatible',
      baseUrl: 'https://example.test/v1/',
      apiKey: null,
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
      supportsNativeResponses: true
    };

    const editor = editorFromProvider(provider);
    expect(editor.apiKey).toBe('');
    expect(editor.timeoutSeconds).toBe(3723);
    expect(editor.modelsText).toBe('model-a');
  });

  it('handles null and day-based time spans', () => {
    expect(secondsToTimeSpan(null)).toBeNull();
    expect(secondsToTimeSpan(0)).toBeNull();
    expect(timeSpanToSeconds(null)).toBe(120);
    expect(timeSpanToSeconds('1.01:00:00')).toBe(90000);
    expect(timeSpanToSeconds('not-a-timespan')).toBe(120);
  });

  it('rejects non-object extra headers', () => {
    const editor = blankProviderEditor();
    editor.id = 'primary';
    editor.name = 'Primary';
    editor.baseUrl = 'https://example.test/v1/';
    editor.extraHeadersText = '[]';

    expect(() => providerFromEditor(editor)).toThrow('Extra headers must be a JSON object.');
  });
});
