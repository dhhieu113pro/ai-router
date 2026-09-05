import { Injector, runInInjectionContext } from '@angular/core';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from './admin-session.service';
import { ApiService } from './api.service';
import { AppComponent } from './app.component';
import { ConfigurationDocument, ProviderDefinition, RouteDefinition } from './models';
import { blankProviderEditor } from './provider-form.utils';

function provider(overrides: Partial<ProviderDefinition> = {}): ProviderDefinition {
  return {
    id: 'primary',
    name: 'Primary',
    type: 'openai-compatible',
    baseUrl: 'https://example.test/v1/',
    apiKey: null,
    enabled: true,
    priority: 100,
    timeout: null,
    models: ['model-a'],
    defaultModel: 'model-a',
    discoverModels: true,
    extraHeaders: null,
    chatEndpoint: null,
    responsesEndpoint: null,
    modelsEndpoint: null,
    supportsNativeResponses: true,
    ...overrides
  };
}

function route(overrides: Partial<RouteDefinition> = {}): RouteDefinition {
  return {
    id: 'route-a',
    strategy: 0,
    enabled: true,
    targets: [{ providerId: 'primary', model: 'model-a', priority: 100, enabled: true }],
    ...overrides
  };
}

class FakeSession {
  value = '';
  get hasKey(): boolean { return this.value.length > 0; }
  set(value: string): void { this.value = value.trim(); }
  clear(): void { this.value = ''; }
}

function createApi() {
  return {
    listProviders: vi.fn(() => of<ProviderDefinition[]>([])),
    addProvider: vi.fn((value: ProviderDefinition) => of(value)),
    updateProvider: vi.fn((value: ProviderDefinition) => of(value)),
    deleteProvider: vi.fn(() => of(undefined)),
    setProviderEnabled: vi.fn((id: string, enabled: boolean) => of(provider({ id, enabled }))),
    testProvider: vi.fn(() => of({ success: true })),
    providerModels: vi.fn(() => of<string[]>([])),
    providerHealth: vi.fn(() => of({ status: 'Healthy', consecutiveFailures: 0 })),
    listRoutes: vi.fn(() => of<RouteDefinition[]>([])),
    addRoute: vi.fn((value: RouteDefinition) => of(value)),
    updateRoute: vi.fn((value: RouteDefinition) => of(value)),
    deleteRoute: vi.fn(() => of(undefined)),
    exportConfiguration: vi.fn(() => of<ConfigurationDocument>({ schemaVersion: 1, providers: [], routes: [] })),
    importConfiguration: vi.fn(() => of({ mode: 'merge', providersUpserted: 0, providersDeleted: 0, routesUpserted: 0, routesDeleted: 0 }))
  };
}

describe('AppComponent', () => {
  let api: ReturnType<typeof createApi>;
  let session: FakeSession;
  let app: AppComponent;
  let confirmMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    sessionStorage.clear();
    delete document.documentElement.dataset['theme'];
    api = createApi();
    session = new FakeSession();
    const injector = Injector.create({
      providers: [
        { provide: ApiService, useValue: api },
        { provide: AdminSessionService, useValue: session }
      ]
    });
    app = runInInjectionContext(injector, () => new AppComponent());
    confirmMock = vi.fn(() => true);
    vi.stubGlobal('confirm', confirmMock);
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:config') });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('initializes theme without unlocking when no saved key exists', async () => {
    localStorage.setItem('ai-router-theme', 'dark');
    const unlock = vi.spyOn(app, 'unlock').mockResolvedValue();

    await app.ngOnInit();

    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(unlock).not.toHaveBeenCalled();
  });

  it('restores a saved admin key and silently unlocks on initialization', async () => {
    session.value = 'saved-key';
    const unlock = vi.spyOn(app, 'unlock').mockResolvedValue();

    await app.ngOnInit();

    expect(app.adminKey).toBe('saved-key');
    expect(unlock).toHaveBeenCalledWith(true);
  });

  it('requires a non-empty admin key', async () => {
    app.adminKey = '   ';
    await app.unlock();
    expect(app.unlockError).toContain('AIROUTER_ADMIN_KEY');
    expect(api.listProviders).not.toHaveBeenCalled();
  });

  it('unlocks and refreshes management data with a valid key', async () => {
    app.adminKey = ' key ';
    await app.unlock();
    expect(session.value).toBe('key');
    expect(app.locked).toBe(false);
    expect(app.busy).toBe(false);
    expect(api.listProviders).toHaveBeenCalledTimes(2);
    expect(api.listRoutes).toHaveBeenCalledOnce();
  });

  it('reports unlock errors unless the attempt is silent', async () => {
    const clear = vi.spyOn(session, 'clear');
    app.adminKey = 'bad';
    api.listProviders.mockReturnValue(throwError(() => ({ error: { error: { message: 'bad key' } } })));

    await app.unlock(false);
    expect(app.unlockError).toBe('bad key');
    expect(clear).toHaveBeenCalled();
    expect(app.locked).toBe(true);
    expect(app.busy).toBe(false);

    app.adminKey = 'bad-again';
    app.unlockError = 'old';
    await app.unlock(true);
    expect(app.unlockError).toBe('');
  });

  it('logs out and clears all management state', () => {
    session.value = 'key';
    app.adminKey = 'key';
    app.providers = [provider()];
    app.routes = [route()];
    app.locked = false;

    app.logout();

    expect(session.hasKey).toBe(false);
    expect(app.adminKey).toBe('');
    expect(app.providers).toEqual([]);
    expect(app.routes).toEqual([]);
    expect(app.locked).toBe(true);
  });

  it('refreshes, sorts and records provider health including unavailable providers', async () => {
    api.listProviders.mockReturnValue(of([
      provider({ id: 'z', name: 'Zulu', priority: 20 }),
      provider({ id: 'c', name: 'Charlie', priority: 10 }),
      provider({ id: 'a', name: 'Alpha', priority: 10 }),
      provider({ id: 'bad', name: 'Bad', priority: 30 })
    ]));
    api.listRoutes.mockReturnValue(of([route({ id: 'z' }), route({ id: 'a' })]));
    api.providerHealth.mockImplementation((id: string) => id === 'bad'
      ? throwError(() => new Error('offline'))
      : of({ status: 'Healthy', consecutiveFailures: 0 }));

    await app.refreshAll();

    expect(app.providers.map(item => item.id)).toEqual(['a', 'c', 'z', 'bad']);
    expect(app.routes.map(item => item.id)).toEqual(['a', 'z']);
    expect(app.health['a'].status).toBe('Healthy');
    expect(app.health['bad']).toEqual({ status: 'Unknown', consecutiveFailures: 0 });
  });

  it('reports refresh failures', async () => {
    api.listProviders.mockReturnValue(throwError(() => new Error('refresh failed')));
    await app.refreshAll();
    expect(app.toast).toBe('refresh failed');
  });

  it('opens, edits and closes provider editors', () => {
    app.openAddProvider();
    expect(app.editingProvider).toBe(false);
    expect(app.providerEditor?.type).toBe('openai-compatible');

    app.openEditProvider(provider({ id: 'edit-me' }));
    expect(app.editingProvider).toBe(true);
    expect(app.providerEditor?.id).toBe('edit-me');
    expect(app.providerEditor?.apiKey).toBe('');

    app.closeProvider();
    expect(app.providerEditor).toBeNull();
  });

  it('ignores save when no provider editor is open', async () => {
    await app.saveProvider();
    expect(api.addProvider).not.toHaveBeenCalled();
  });

  it('validates each required provider field', async () => {
    app.providerEditor = blankProviderEditor();
    await app.saveProvider();
    expect(app.toast).toContain('required');

    app.providerEditor.id = 'id';
    await app.saveProvider();
    expect(app.toast).toContain('required');

    app.providerEditor.name = 'name';
    await app.saveProvider();
    expect(app.toast).toContain('required');
  });

  it('adds a valid provider and refreshes the list', async () => {
    app.openAddProvider();
    Object.assign(app.providerEditor!, { id: 'new', name: 'New', baseUrl: 'https://new.test/v1/' });

    await app.saveProvider();

    expect(api.addProvider).toHaveBeenCalledWith(expect.objectContaining({ id: 'new' }));
    expect(app.providerEditor).toBeNull();
    expect(app.busy).toBe(false);
    expect(app.toast).toBe('Provider new saved.');
  });

  it('updates a valid provider', async () => {
    app.openEditProvider(provider({ id: 'existing' }));
    await app.saveProvider();
    expect(api.updateProvider).toHaveBeenCalledWith(expect.objectContaining({ id: 'existing', apiKey: null }));
  });

  it('reports provider save errors and resets busy state', async () => {
    app.openAddProvider();
    Object.assign(app.providerEditor!, { id: 'new', name: 'New', baseUrl: 'https://new.test/v1/' });
    api.addProvider.mockReturnValue(throwError(() => new Error('save failed')));

    await app.saveProvider();

    expect(app.toast).toBe('save failed');
    expect(app.busy).toBe(false);
  });

  it('cancels provider deletion when confirmation is declined', async () => {
    confirmMock.mockReturnValue(false);
    await app.deleteProvider(provider());
    expect(api.deleteProvider).not.toHaveBeenCalled();
  });

  it('deletes providers after confirmation and reports failures', async () => {
    await app.deleteProvider(provider({ id: 'gone' }));
    expect(api.deleteProvider).toHaveBeenCalledWith('gone');
    expect(app.toast).toBe('Provider gone deleted.');

    api.deleteProvider.mockReturnValue(throwError(() => new Error('delete failed')));
    await app.deleteProvider(provider({ id: 'broken' }));
    expect(app.toast).toBe('delete failed');
  });

  it('toggles providers and reports failures', async () => {
    await app.toggleProvider(provider({ id: 'toggle', enabled: true }));
    expect(api.setProviderEnabled).toHaveBeenCalledWith('toggle', false);

    api.setProviderEnabled.mockReturnValue(throwError(() => new Error('toggle failed')));
    await app.toggleProvider(provider({ id: 'toggle', enabled: false }));
    expect(app.toast).toBe('toggle failed');
  });

  it('tests provider connectivity for success, failure and request errors', async () => {
    const current = provider({ id: 'test', name: 'Test' });
    api.testProvider.mockReturnValueOnce(of({ success: true })).mockReturnValueOnce(of({ success: false }));

    await app.testProvider(current);
    expect(app.toast).toBe('Test is reachable.');
    await app.testProvider(current);
    expect(app.toast).toBe('Test test failed.');

    api.testProvider.mockReturnValue(throwError(() => new Error('request failed')));
    await app.testProvider(current);
    expect(app.toast).toBe('request failed');
  });

  it('discovers one, many and failed provider model requests', async () => {
    const current = provider({ id: 'models', name: 'Models' });
    api.providerModels.mockReturnValueOnce(of(['one'])).mockReturnValueOnce(of(['one', 'two']));

    await app.discoverProviderModels(current);
    expect(app.toast).toBe('Discovered 1 model for Models.');
    await app.discoverProviderModels(current);
    expect(app.toast).toBe('Discovered 2 models for Models.');

    api.providerModels.mockReturnValue(throwError(() => new Error('models failed')));
    await app.discoverProviderModels(current);
    expect(app.toast).toBe('models failed');
  });

  it('creates new route targets using provider defaults and fallbacks', () => {
    app.providers = [];
    expect((app as any).newTarget()).toEqual({ providerId: '', model: '', priority: 100, enabled: true });

    app.providers = [provider({ id: 'p', defaultModel: 'default', models: ['fallback'] })];
    expect((app as any).newTarget().model).toBe('default');

    app.providers = [provider({ id: 'p', defaultModel: null, models: ['fallback'] })];
    expect((app as any).newTarget().model).toBe('fallback');

    app.providers = [provider({ id: 'p', defaultModel: null, models: null })];
    expect((app as any).newTarget().model).toBe('');
  });

  it('opens, clones, mutates and closes route editors', () => {
    app.providers = [provider()];
    app.openAddRoute();
    expect(app.editingRoute).toBe(false);
    expect(app.routeEditor?.targets).toHaveLength(1);

    const original = route();
    app.openEditRoute(original);
    expect(app.editingRoute).toBe(true);
    app.routeEditor!.targets[0].model = 'changed';
    expect(original.targets[0].model).toBe('model-a');

    app.addTarget();
    expect(app.routeEditor!.targets).toHaveLength(2);
    app.removeTarget(0);
    expect(app.routeEditor!.targets).toHaveLength(1);
    app.closeRoute();
    expect(app.routeEditor).toBeNull();

    app.addTarget();
    app.removeTarget(0);
    expect(app.routeEditor).toBeNull();
  });

  it('ignores route save when no editor is open', async () => {
    await app.saveRoute();
    expect(api.addRoute).not.toHaveBeenCalled();
  });

  it('requires route id and at least one target', async () => {
    app.routeEditor = route({ id: '' });
    await app.saveRoute();
    expect(app.toast).toContain('at least one target');

    app.routeEditor = route({ id: 'valid', targets: [] });
    await app.saveRoute();
    expect(app.toast).toContain('at least one target');
  });

  it('adds and normalizes route values', async () => {
    app.routeEditor = route({ id: ' route ', targets: [{ providerId: ' primary ', model: ' model ', priority: 42 as any, enabled: true }] });
    app.editingRoute = false;

    await app.saveRoute();

    expect(api.addRoute).toHaveBeenCalledWith({
      id: 'route', strategy: 0, enabled: true,
      targets: [{ providerId: 'primary', model: 'model', priority: 42, enabled: true }]
    });
    expect(app.routeEditor).toBeNull();
    expect(app.toast).toBe('Route route saved.');
  });

  it('updates routes and reports route save errors', async () => {
    app.routeEditor = route({ id: 'edit' });
    app.editingRoute = true;
    await app.saveRoute();
    expect(api.updateRoute).toHaveBeenCalled();

    app.routeEditor = route({ id: 'bad' });
    api.updateRoute.mockReturnValue(throwError(() => new Error('route failed')));
    await app.saveRoute();
    expect(app.toast).toBe('route failed');
    expect(app.busy).toBe(false);
  });

  it('cancels, performs and reports route deletion', async () => {
    confirmMock.mockReturnValueOnce(false).mockReturnValue(true);
    await app.deleteRoute(route({ id: 'keep' }));
    expect(api.deleteRoute).not.toHaveBeenCalled();

    await app.deleteRoute(route({ id: 'gone' }));
    expect(api.deleteRoute).toHaveBeenCalledWith('gone');
    expect(app.toast).toBe('Route gone deleted.');

    api.deleteRoute.mockReturnValue(throwError(() => new Error('route delete failed')));
    await app.deleteRoute(route({ id: 'broken' }));
    expect(app.toast).toBe('route delete failed');
  });

  it('chooses discovered, configured and empty model lists', () => {
    app.providers = [provider({ id: 'p', models: ['configured'] }), provider({ id: 'none', models: null })];
    app.discoveredModels['p'] = ['discovered'];
    expect(app.modelsFor('p')).toEqual(['discovered']);
    delete app.discoveredModels['p'];
    expect(app.modelsFor('p')).toEqual(['configured']);
    expect(app.modelsFor('none')).toEqual([]);
    expect(app.modelsFor('missing')).toEqual([]);
  });

  it('exports redacted and secret configuration files', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    app.includeSecrets = false;
    await app.exportConfiguration();
    expect(api.exportConfiguration).toHaveBeenCalledWith(false);
    expect(app.toast).toContain('redacted');

    app.includeSecrets = true;
    await app.exportConfiguration();
    expect(api.exportConfiguration).toHaveBeenCalledWith(true);
    expect(app.toast).toContain('with secrets');
    expect(click).toHaveBeenCalledTimes(2);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(2);
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(2);
  });

  it('reports export failures', async () => {
    api.exportConfiguration.mockReturnValue(throwError(() => new Error('export failed')));
    await app.exportConfiguration();
    expect(app.toast).toBe('export failed');
  });

  it('ignores empty import file selection', async () => {
    await app.selectImportFile({ target: { files: undefined, value: 'x' } } as any);
    expect(app.importDocument).toBeNull();
    expect(app.importFileName).toBe('');
  });

  it('loads a valid configuration import file', async () => {
    const configuration = { schemaVersion: 1, providers: [], routes: [] };
    const file = { name: 'config.json', text: vi.fn().mockResolvedValue(JSON.stringify(configuration)) };
    await app.selectImportFile({ target: { files: [file], value: 'x' } } as any);
    expect(app.importDocument).toEqual(configuration);
    expect(app.importFileName).toBe('config.json');
  });

  it.each([
    [{ schemaVersion: 2, providers: [], routes: [] }, 'schema'],
    [{ schemaVersion: 1, providers: {}, routes: [] }, 'providers'],
    [{ schemaVersion: 1, providers: [], routes: {} }, 'routes']
  ])('rejects invalid configuration document %s', async (configuration) => {
    const target = {
      files: [{ name: 'bad.json', text: vi.fn().mockResolvedValue(JSON.stringify(configuration)) }],
      value: 'selected'
    };
    await app.selectImportFile({ target } as any);
    expect(app.importDocument).toBeNull();
    expect(target.value).toBe('');
    expect(app.toast).toContain('schemaVersion 1');
  });

  it('reports malformed configuration JSON', async () => {
    const target = { files: [{ name: 'bad.json', text: vi.fn().mockResolvedValue('{') }], value: 'selected' };
    await app.selectImportFile({ target } as any);
    expect(target.value).toBe('');
    expect(app.toast).not.toBe('');
  });

  it('ignores import when no document is selected', async () => {
    await app.importConfiguration();
    expect(api.importConfiguration).not.toHaveBeenCalled();
  });

  it('requires confirmation before replace imports', async () => {
    app.importDocument = { schemaVersion: 1, providers: [], routes: [] };
    app.importMode = 'replace';
    confirmMock.mockReturnValue(false);
    await app.importConfiguration();
    expect(api.importConfiguration).not.toHaveBeenCalled();
  });

  it('imports merge configuration without deletion summary', async () => {
    app.importDocument = { schemaVersion: 1, providers: [], routes: [] };
    app.importFileName = 'config.json';
    app.importMode = 'merge';
    api.importConfiguration.mockReturnValue(of({ mode: 'merge', providersUpserted: 2, providersDeleted: 0, routesUpserted: 1, routesDeleted: 0 }));

    await app.importConfiguration();

    expect(api.importConfiguration).toHaveBeenCalledWith(expect.anything(), 'merge');
    expect(app.importDocument).toBeNull();
    expect(app.importFileName).toBe('');
    expect(app.toast).toBe('Import complete: 2 providers, 1 routes.');
    expect(app.busy).toBe(false);
  });

  it('imports replace configuration with deletion summary', async () => {
    app.importDocument = { schemaVersion: 1, providers: [], routes: [] };
    app.importMode = 'replace';
    api.importConfiguration.mockReturnValue(of({ mode: 'replace', providersUpserted: 1, providersDeleted: 2, routesUpserted: 3, routesDeleted: 4 }));

    await app.importConfiguration();

    expect(confirmMock).toHaveBeenCalled();
    expect(app.toast).toContain('deleted 2 providers and 4 routes');
  });

  it('reports import failures and resets busy state', async () => {
    app.importDocument = { schemaVersion: 1, providers: [], routes: [] };
    api.importConfiguration.mockReturnValue(throwError(() => new Error('import failed')));
    await app.importConfiguration();
    expect(app.toast).toBe('import failed');
    expect(app.busy).toBe(false);
  });

  it('cycles dark, light and system themes', () => {
    document.documentElement.dataset['theme'] = 'dark';
    app.toggleTheme();
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(localStorage.getItem('ai-router-theme')).toBe('light');

    app.toggleTheme();
    expect(document.documentElement.dataset['theme']).toBeUndefined();
    expect(localStorage.getItem('ai-router-theme')).toBeNull();

    app.toggleTheme();
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(localStorage.getItem('ai-router-theme')).toBe('dark');
  });

  it('clears a toast only when it is still the same message', () => {
    (app as any).notify('first');
    vi.advanceTimersByTime(3500);
    expect(app.toast).toBe('');

    (app as any).notify('second');
    app.toast = 'newer';
    vi.advanceTimersByTime(3500);
    expect(app.toast).toBe('newer');
  });

  it('extracts nested, ordinary and fallback error messages', () => {
    expect((app as any).errorMessage({ error: { error: { message: 'nested' } } }, 'fallback')).toBe('nested');
    expect((app as any).errorMessage(new Error('ordinary'), 'fallback')).toBe('ordinary');
    expect((app as any).errorMessage({ error: {} }, 'fallback')).toBe('fallback');
    expect((app as any).errorMessage(null, 'fallback')).toBe('fallback');
  });

  it('applies only supported saved themes', () => {
    localStorage.setItem('ai-router-theme', 'light');
    (app as any).applySavedTheme();
    expect(document.documentElement.dataset['theme']).toBe('light');

    delete document.documentElement.dataset['theme'];
    localStorage.setItem('ai-router-theme', 'unexpected');
    (app as any).applySavedTheme();
    expect(document.documentElement.dataset['theme']).toBeUndefined();
  });
});
