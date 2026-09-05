import { beforeEach, describe, expect, it } from 'vitest';
import { AdminSessionService } from './admin-session.service';

describe('AdminSessionService', () => {
  beforeEach(() => sessionStorage.clear());

  it('starts empty when there is no saved key', () => {
    const session = new AdminSessionService();

    expect(session.value).toBe('');
    expect(session.hasKey).toBe(false);
  });

  it('loads, trims and persists an admin key for the browser session', () => {
    sessionStorage.setItem('ai-router-admin-key', 'saved');
    const session = new AdminSessionService();

    expect(session.value).toBe('saved');
    expect(session.hasKey).toBe(true);

    session.set('  changed  ');
    expect(session.value).toBe('changed');
    expect(sessionStorage.getItem('ai-router-admin-key')).toBe('changed');
  });

  it('removes empty keys and clear delegates to the same behavior', () => {
    const session = new AdminSessionService();
    session.set('key');
    session.set('   ');

    expect(session.hasKey).toBe(false);
    expect(sessionStorage.getItem('ai-router-admin-key')).toBeNull();

    session.set('again');
    session.clear();
    expect(session.value).toBe('');
    expect(sessionStorage.getItem('ai-router-admin-key')).toBeNull();
  });
});
