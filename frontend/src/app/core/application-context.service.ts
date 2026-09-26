import { Injectable, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApplicationSummary, UserInfo } from './models';
import { TelemetryApiService, describeError } from './telemetry-api.service';

/** The signed-in user and the application currently selected in the header dropdown. */
@Injectable({ providedIn: 'root' })
export class ApplicationContextService {
  private readonly api = inject(TelemetryApiService);

  readonly user = signal<UserInfo | null>(null);
  readonly applications = signal<ApplicationSummary[]>([]);
  readonly loading = signal(false);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  readonly selectedKey = signal<string | null>(null);
  readonly selected = computed(() => this.applications().find((a) => a.appKey === this.selectedKey()) ?? null);

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({ user: this.api.getMe(), applications: this.api.getApplications() }).subscribe({
      next: ({ user, applications }) => {
        this.user.set(user);
        this.applications.set(applications);
        const remembered = readSelection(user.email);
        const initial = applications.find((a) => a.appKey === remembered) ?? applications[0] ?? null;
        this.selectedKey.set(initial?.appKey ?? null);
        this.loading.set(false);
        this.loaded.set(true);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
        this.loaded.set(true);
      },
    });
  }

  select(appKey: string): void {
    this.selectedKey.set(appKey);
    const email = this.user()?.email;
    if (email) {
      writeSelection(email, appKey);
    }
  }
}

// Remember the last application per user; storage can be unavailable (private mode), so failures are ignored.
function readSelection(email: string): string | null {
  try {
    return localStorage.getItem(`osse.selectedApp.${email}`);
  } catch {
    return null;
  }
}

function writeSelection(email: string, appKey: string): void {
  try {
    localStorage.setItem(`osse.selectedApp.${email}`, appKey);
  } catch {
    // ignore
  }
}
