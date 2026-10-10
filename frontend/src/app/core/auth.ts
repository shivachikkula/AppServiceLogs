import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import {
  AccountInfo,
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  PublicClientApplication,
} from '@azure/msal-browser';
import { from, switchMap } from 'rxjs';

/** Sign-in settings served by the API at /api/auth-config (public values only). */
export interface AuthConfig {
  enabled: boolean;
  clientId: string | null;
  authority: string | null;
  knownAuthorities: string[];
  scopes: string[];
}

export interface AuthContext {
  config: AuthConfig;
  /** Null when B2C is disabled (API running with a Development user). */
  msal: PublicClientApplication | null;
}

export const AUTH_CONTEXT = new InjectionToken<AuthContext>('AUTH_CONTEXT');

/**
 * Loads the sign-in settings and, when Azure AD B2C is enabled, completes any pending redirect sign-in before the
 * Angular app starts, so routes and HTTP calls see the signed-in account immediately.
 */
export async function initializeAuth(): Promise<AuthContext> {
  const response = await fetch('/api/auth-config');
  if (!response.ok) {
    throw new Error(`Could not load sign-in settings from the API (${response.status}).`);
  }
  const config = (await response.json()) as AuthConfig;
  if (!config.enabled || !config.clientId) {
    return { config, msal: null };
  }

  const msal = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority ?? undefined,
      knownAuthorities: config.knownAuthorities,
      redirectUri: window.location.origin + '/',
      postLogoutRedirectUri: window.location.origin + '/',
    },
    cache: { cacheLocation: BrowserCacheLocation.LocalStorage },
  });
  await msal.initialize();

  const result = await msal.handleRedirectPromise();
  if (result?.account) {
    msal.setActiveAccount(result.account);
    // Return to the page the user originally asked for (carried through the redirect in "state").
    if (result.state?.startsWith('/')) {
      window.history.replaceState(null, '', result.state);
    }
  } else if (!msal.getActiveAccount() && msal.getAllAccounts().length > 0) {
    msal.setActiveAccount(msal.getAllAccounts()[0]);
  }

  return { config, msal };
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly context = inject(AUTH_CONTEXT);
  private readonly accountState = signal<AccountInfo | null>(this.context.msal?.getActiveAccount() ?? null);

  readonly enabled = this.context.config.enabled;
  readonly account = this.accountState.asReadonly();
  readonly isSignedIn = computed(() => !this.enabled || this.accountState() !== null);

  login(returnUrl = window.location.pathname + window.location.search): Promise<void> {
    return this.context.msal?.loginRedirect({ scopes: this.context.config.scopes, state: returnUrl }) ?? Promise.resolve();
  }

  logout(): Promise<void> {
    const msal = this.context.msal;
    if (!msal) {
      return Promise.resolve();
    }
    this.accountState.set(null);
    return msal.logoutRedirect({ account: msal.getActiveAccount() });
  }

  /** Returns an access token for the API, or null when sign-in is disabled. Redirects to sign in if required. */
  async getAccessToken(): Promise<string | null> {
    const msal = this.context.msal;
    if (!msal) {
      return null;
    }
    const account = msal.getActiveAccount();
    if (!account) {
      await this.login();
      throw new Error('Redirecting to sign in.');
    }
    try {
      const result = await msal.acquireTokenSilent({ scopes: this.context.config.scopes, account });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await msal.acquireTokenRedirect({
          scopes: this.context.config.scopes,
          account,
          state: window.location.pathname + window.location.search,
        });
      }
      throw error;
    }
  }
}

/** Sends the user to the B2C sign-in page when they are not signed in. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isSignedIn()) {
    return true;
  }
  void auth.login(state.url);
  return false;
};

/** Adds the B2C access token to calls to the API (except the public sign-in settings). */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') || request.url.startsWith('/api/auth-config')) {
    return next(request);
  }
  const auth = inject(AuthService);
  return from(auth.getAccessToken()).pipe(
    switchMap((token) => next(token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request)),
  );
};
