import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { AUTH_CONTEXT, initializeAuth } from './app/core/auth';

initializeAuth()
  .then((auth) =>
    bootstrapApplication(App, {
      ...appConfig,
      providers: [...appConfig.providers, { provide: AUTH_CONTEXT, useValue: auth }],
    }),
  )
  .catch((err) => {
    console.error(err);
    document.body.innerHTML = `<p style="font-family: system-ui; padding: 24px">Failed to start: ${String(err?.message ?? err)}</p>`;
  });
