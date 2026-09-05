import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
  bootstrapApplication: vi.fn(),
  provideHttpClient: vi.fn(() => 'http-provider'),
  withInterceptors: vi.fn(() => 'interceptors')
}));

vi.mock('@angular/platform-browser', () => ({ bootstrapApplication: mocks.bootstrapApplication }));
vi.mock('@angular/common/http', async importOriginal => {
  const actual = await importOriginal<typeof import('@angular/common/http')>();
  return { ...actual, provideHttpClient: mocks.provideHttpClient, withInterceptors: mocks.withInterceptors };
});

describe('admin bootstrap', () => {
  beforeEach(() => {
    vi.resetModules();
    mocks.bootstrapApplication.mockReset();
    mocks.provideHttpClient.mockClear();
    mocks.withInterceptors.mockClear();
  });

  it('bootstraps the admin application with the admin-key interceptor', async () => {
    mocks.bootstrapApplication.mockResolvedValue({});
    await import('./main');
    await Promise.resolve();

    expect(mocks.withInterceptors).toHaveBeenCalledOnce();
    expect(mocks.provideHttpClient).toHaveBeenCalledWith('interceptors');
    expect(mocks.bootstrapApplication).toHaveBeenCalledOnce();
  });

  it('logs bootstrap failures', async () => {
    const error = new Error('bootstrap failed');
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    mocks.bootstrapApplication.mockRejectedValue(error);

    await import('./main');
    await Promise.resolve();

    expect(consoleError).toHaveBeenCalledWith(error);
  });
});
