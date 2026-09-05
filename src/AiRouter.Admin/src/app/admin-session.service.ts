import { Injectable } from '@angular/core';

const storageKey = 'ai-router-admin-key';

@Injectable({ providedIn: 'root' })
export class AdminSessionService {
  private key = sessionStorage.getItem(storageKey) ?? '';

  get value(): string {
    return this.key;
  }

  get hasKey(): boolean {
    return this.key.length > 0;
  }

  set(value: string): void {
    this.key = value.trim();
    if (this.key) {
      sessionStorage.setItem(storageKey, this.key);
    } else {
      sessionStorage.removeItem(storageKey);
    }
  }

  clear(): void {
    this.set('');
  }
}
