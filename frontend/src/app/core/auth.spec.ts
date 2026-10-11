import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { firstValueFrom, isObservable, of, throwError } from 'rxjs';
import { AUTH_CONTEXT, AuthService, adminGuard, authInterceptor } from './auth';
import { TelemetryApiService } from './telemetry-api.service';

const disabledAuth = {
  config: { enabled: false, clientId: null, authority: null, knownAuthorities: [], scopes: [] },
  msal: null,
};

describe('AuthService without B2C (Development user)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [{ provide: AUTH_CONTEXT, useValue: disabledAuth }] }));

  it('is signed in and sends no token', async () => {
    const auth = TestBed.inject(AuthService);
    expect(auth.isSignedIn()).toBe(true);
    expect(await auth.getAccessToken()).toBeNull();
  });
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { getAccessToken: () => Promise.resolve('test-token') } },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API calls', async () => {
    http.get('/api/me').subscribe();
    await Promise.resolve(); // the token is fetched asynchronously
    await Promise.resolve();
    const request = backend.expectOne('/api/me');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-token');
    request.flush({});
  });

  it('does not send a token to the public sign-in settings or to other hosts', () => {
    http.get('/api/auth-config').subscribe();
    http.get('https://example.com/data').subscribe();
    expect(backend.expectOne('/api/auth-config').request.headers.has('Authorization')).toBe(false);
    expect(backend.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(false);
  });
});

describe('adminGuard', () => {
  async function run(getMe: () => unknown) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: TelemetryApiService, useValue: { getMe } }],
    });
    const result = TestBed.runInInjectionContext(() =>
      adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  it('lets admins in', async () => {
    expect(await run(() => of({ email: 'a@b.c', name: null, isAdmin: true }))).toBe(true);
  });

  it('sends everyone else to the live logs', async () => {
    const result = await run(() => of({ email: 'a@b.c', name: null, isAdmin: false }));
    expect(result instanceof UrlTree && TestBed.inject(Router).serializeUrl(result)).toBe('/live');
  });

  it('sends users to the live logs when the check fails', async () => {
    const result = await run(() => throwError(() => new Error('offline')));
    expect(result instanceof UrlTree && TestBed.inject(Router).serializeUrl(result)).toBe('/live');
  });
});
