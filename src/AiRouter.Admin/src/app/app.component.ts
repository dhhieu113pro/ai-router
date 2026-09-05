import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { AdminSessionService } from './admin-session.service';
import {
  ConfigurationDocument,
  ProviderDefinition,
  ProviderEditor,
  ProviderHealth,
  RouteDefinition,
  RouteTarget
} from './models';
import { blankProviderEditor, editorFromProvider, providerFromEditor } from './provider-form.utils';

type Tab = 'providers' | 'routes' | 'backup';
type ImportMode = 'merge' | 'replace';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly session = inject(AdminSessionService);

  activeTab: Tab = 'providers';
  locked = true;
  adminKey = '';
  unlockError = '';
  busy = false;
  providers: ProviderDefinition[] = [];
  routes: RouteDefinition[] = [];
  health: Record<string, ProviderHealth> = {};
  discoveredModels: Record<string, string[]> = {};
  toast = '';

  providerEditor: ProviderEditor | null = null;
  editingProvider = false;
  routeEditor: RouteDefinition | null = null;
  editingRoute = false;

  includeSecrets = false;
  importMode: ImportMode = 'merge';
  importDocument: ConfigurationDocument | null = null;
  importFileName = '';

  async ngOnInit(): Promise<void> {
    this.applySavedTheme();
    if (this.session.hasKey) {
      this.adminKey = this.session.value;
      await this.unlock(true);
    }
  }

  async unlock(silent = false): Promise<void> {
    this.unlockError = '';
    this.session.set(this.adminKey);
    if (!this.session.hasKey) {
      this.unlockError = 'Enter the admin key configured in AIROUTER_ADMIN_KEY.';
      return;
    }

    try {
      this.busy = true;
      await firstValueFrom(this.api.listProviders());
      this.locked = false;
      await this.refreshAll();
    } catch (error) {
      this.session.clear();
      this.locked = true;
      if (!silent) {
        this.unlockError = this.errorMessage(error, 'Unable to unlock management. Check AIROUTER_ADMIN_KEY.');
      }
    } finally {
      this.busy = false;
    }
  }

  logout(): void {
    this.session.clear();
    this.adminKey = '';
    this.providers = [];
    this.routes = [];
    this.locked = true;
  }

  async refreshAll(): Promise<void> {
    try {
      const [providers, routes] = await Promise.all([
        firstValueFrom(this.api.listProviders()),
        firstValueFrom(this.api.listRoutes())
      ]);
      this.providers = providers.sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name));
      this.routes = routes.sort((a, b) => a.id.localeCompare(b.id));
      await this.refreshHealth();
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to refresh AI Router.'));
    }
  }

  openAddProvider(): void {
    this.editingProvider = false;
    this.providerEditor = blankProviderEditor();
  }

  openEditProvider(provider: ProviderDefinition): void {
    this.editingProvider = true;
    this.providerEditor = editorFromProvider(provider);
  }

  closeProvider(): void {
    this.providerEditor = null;
  }

  async saveProvider(): Promise<void> {
    if (!this.providerEditor) return;

    try {
      const payload = providerFromEditor(this.providerEditor);
      if (!payload.id || !payload.name || !payload.baseUrl) {
        this.notify('Provider id, name and base URL are required.');
        return;
      }

      this.busy = true;
      if (this.editingProvider) {
        await firstValueFrom(this.api.updateProvider(payload));
      } else {
        await firstValueFrom(this.api.addProvider(payload));
      }
      this.providerEditor = null;
      await this.refreshAll();
      this.notify(`Provider ${payload.id} saved.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to save provider.'));
    } finally {
      this.busy = false;
    }
  }

  async deleteProvider(provider: ProviderDefinition): Promise<void> {
    if (!confirm(`Delete provider "${provider.name}"?`)) return;
    try {
      await firstValueFrom(this.api.deleteProvider(provider.id));
      await this.refreshAll();
      this.notify(`Provider ${provider.id} deleted.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to delete provider.'));
    }
  }

  async toggleProvider(provider: ProviderDefinition): Promise<void> {
    try {
      await firstValueFrom(this.api.setProviderEnabled(provider.id, !provider.enabled));
      await this.refreshAll();
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to update provider.'));
    }
  }

  async testProvider(provider: ProviderDefinition): Promise<void> {
    try {
      const result = await firstValueFrom(this.api.testProvider(provider.id));
      await this.refreshProviderHealth(provider.id);
      this.notify(result.success ? `${provider.name} is reachable.` : `${provider.name} test failed.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Provider test failed.'));
    }
  }

  async discoverProviderModels(provider: ProviderDefinition): Promise<void> {
    try {
      const models = await firstValueFrom(this.api.providerModels(provider.id));
      this.discoveredModels[provider.id] = models;
      this.notify(`Discovered ${models.length} model${models.length === 1 ? '' : 's'} for ${provider.name}.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to discover models.'));
    }
  }

  openAddRoute(): void {
    this.editingRoute = false;
    this.routeEditor = {
      id: '',
      strategy: 0,
      enabled: true,
      targets: [this.newTarget()]
    };
  }

  openEditRoute(route: RouteDefinition): void {
    this.editingRoute = true;
    this.routeEditor = {
      id: route.id,
      strategy: route.strategy,
      enabled: route.enabled,
      targets: route.targets.map(target => ({ ...target }))
    };
  }

  closeRoute(): void {
    this.routeEditor = null;
  }

  addTarget(): void {
    this.routeEditor?.targets.push(this.newTarget());
  }

  removeTarget(index: number): void {
    if (!this.routeEditor) return;
    this.routeEditor.targets.splice(index, 1);
  }

  async saveRoute(): Promise<void> {
    if (!this.routeEditor) return;
    if (!this.routeEditor.id.trim() || this.routeEditor.targets.length === 0) {
      this.notify('Route id and at least one target are required.');
      return;
    }

    try {
      const payload: RouteDefinition = {
        ...this.routeEditor,
        id: this.routeEditor.id.trim(),
        targets: this.routeEditor.targets.map(target => ({
          ...target,
          providerId: target.providerId.trim(),
          model: target.model.trim(),
          priority: Number(target.priority)
        }))
      };
      this.busy = true;
      if (this.editingRoute) {
        await firstValueFrom(this.api.updateRoute(payload));
      } else {
        await firstValueFrom(this.api.addRoute(payload));
      }
      this.routeEditor = null;
      await this.refreshAll();
      this.notify(`Route ${payload.id} saved.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to save route.'));
    } finally {
      this.busy = false;
    }
  }

  async deleteRoute(route: RouteDefinition): Promise<void> {
    if (!confirm(`Delete route "${route.id}"?`)) return;
    try {
      await firstValueFrom(this.api.deleteRoute(route.id));
      await this.refreshAll();
      this.notify(`Route ${route.id} deleted.`);
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to delete route.'));
    }
  }

  modelsFor(providerId: string): string[] {
    const provider = this.providers.find(item => item.id === providerId);
    return this.discoveredModels[providerId] ?? provider?.models ?? [];
  }

  async exportConfiguration(): Promise<void> {
    try {
      const configuration = await firstValueFrom(this.api.exportConfiguration(this.includeSecrets));
      const blob = new Blob([JSON.stringify(configuration, null, 2)], { type: 'application/json' });
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = `ai-router-config-${new Date().toISOString().slice(0, 10)}.json`;
      link.click();
      URL.revokeObjectURL(link.href);
      this.notify(this.includeSecrets ? 'Configuration exported with secrets.' : 'Configuration exported with secrets redacted.');
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to export configuration.'));
    }
  }

  async selectImportFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.importDocument = null;
    this.importFileName = '';
    if (!file) return;

    try {
      const parsed = JSON.parse(await file.text()) as ConfigurationDocument;
      if (parsed.schemaVersion !== 1 || !Array.isArray(parsed.providers) || !Array.isArray(parsed.routes)) {
        throw new Error('Expected an AI Router schemaVersion 1 configuration file.');
      }
      this.importDocument = parsed;
      this.importFileName = file.name;
    } catch (error) {
      input.value = '';
      this.notify(this.errorMessage(error, 'Unable to read configuration file.'));
    }
  }

  async importConfiguration(): Promise<void> {
    if (!this.importDocument) return;
    if (this.importMode === 'replace' && !confirm('Replace mode deletes providers and routes that are not in this file. Continue?')) return;

    try {
      this.busy = true;
      const result = await firstValueFrom(this.api.importConfiguration(this.importDocument, this.importMode));
      await this.refreshAll();
      this.importDocument = null;
      this.importFileName = '';
      this.notify(
        `Import complete: ${result.providersUpserted} providers, ${result.routesUpserted} routes` +
        (result.providersDeleted || result.routesDeleted
          ? `; deleted ${result.providersDeleted} providers and ${result.routesDeleted} routes.`
          : '.')
      );
    } catch (error) {
      this.notify(this.errorMessage(error, 'Unable to import configuration.'));
    } finally {
      this.busy = false;
    }
  }

  toggleTheme(): void {
    const root = document.documentElement;
    const current = root.dataset['theme'] ?? 'system';
    const next = current === 'dark' ? 'light' : current === 'light' ? 'system' : 'dark';
    if (next === 'system') {
      delete root.dataset['theme'];
      localStorage.removeItem('ai-router-theme');
    } else {
      root.dataset['theme'] = next;
      localStorage.setItem('ai-router-theme', next);
    }
    this.notify(`Theme: ${next}.`);
  }

  private async refreshHealth(): Promise<void> {
    await Promise.all(this.providers.map(provider => this.refreshProviderHealth(provider.id)));
  }

  private async refreshProviderHealth(id: string): Promise<void> {
    try {
      this.health[id] = await firstValueFrom(this.api.providerHealth(id));
    } catch {
      this.health[id] = { status: 'Unknown', consecutiveFailures: 0 };
    }
  }

  private newTarget(): RouteTarget {
    const provider = this.providers[0];
    return {
      providerId: provider?.id ?? '',
      model: provider?.defaultModel ?? provider?.models?.[0] ?? '',
      priority: 100,
      enabled: true
    };
  }

  private notify(message: string): void {
    this.toast = message;
    window.setTimeout(() => {
      if (this.toast === message) this.toast = '';
    }, 3500);
  }

  private errorMessage(error: unknown, fallback: string): string {
    const candidate = error as { error?: { error?: { message?: string } }; message?: string };
    return candidate?.error?.error?.message ?? candidate?.message ?? fallback;
  }

  private applySavedTheme(): void {
    const saved = localStorage.getItem('ai-router-theme');
    if (saved === 'dark' || saved === 'light') {
      document.documentElement.dataset['theme'] = saved;
    }
  }
}
