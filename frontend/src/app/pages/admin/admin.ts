import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin-api.service';
import { ApplicationContextService } from '../../core/application-context.service';
import { AdminApplication, AdminGrant, ApplicationCheck, AuditEntry, DiagnosticCheck } from '../../core/models';
import { describeError } from '../../core/telemetry-api.service';

type Tab = 'access' | 'applications' | 'audit' | 'diagnostics';

const AUDIT_PAGE = 100;

@Component({
  selector: 'app-admin',
  imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './admin.html',
})
export class Admin {
  private readonly api = inject(AdminApiService);
  private readonly appContext = inject(ApplicationContextService);

  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'access', label: 'Access' },
    { id: 'applications', label: 'Applications' },
    { id: 'audit', label: 'Audit log' },
    { id: 'diagnostics', label: 'Diagnostics' },
  ];
  protected readonly tab = signal<Tab>('access');
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  // Access
  protected readonly grants = signal<AdminGrant[]>([]);
  protected readonly grantsLoading = signal(false);
  protected readonly grantFilter = signal('');
  protected readonly newEmail = signal('');
  protected readonly newAppKey = signal('');
  protected readonly newAppName = signal('');
  protected readonly saving = signal(false);
  protected readonly filteredGrants = computed(() => {
    const term = this.grantFilter().trim().toLowerCase();
    return term
      ? this.grants().filter((g) => `${g.email} ${g.applicationName} ${g.appKey}`.toLowerCase().includes(term))
      : this.grants();
  });
  /** Name of the application when the typed app key already exists (then no name is needed). */
  protected readonly existingAppName = computed(
    () => this.applications().find((a) => a.appKey === this.newAppKey().trim())?.applicationName ?? null,
  );

  // Applications
  protected readonly applications = signal<AdminApplication[]>([]);
  protected readonly editingKey = signal<string | null>(null);
  protected readonly editName = signal('');
  protected readonly checks = signal<Record<string, ApplicationCheck | 'running'>>({});

  // Audit
  protected readonly audit = signal<AuditEntry[]>([]);
  protected readonly auditLoading = signal(false);
  protected readonly auditHasMore = signal(false);
  protected readonly auditEmail = signal('');
  protected readonly auditAppKey = signal('');
  protected readonly auditFailedOnly = signal(false);
  protected readonly expandedAudit = signal<number | null>(null);

  // Diagnostics
  protected readonly diagnostics = signal<DiagnosticCheck[] | null>(null);
  protected readonly diagnosticsLoading = signal(false);

  constructor() {
    this.loadGrants();
    this.loadApplications();
  }

  protected select(tab: Tab): void {
    this.tab.set(tab);
    this.error.set(null);
    this.notice.set(null);
    if (tab === 'audit' && this.audit().length === 0) {
      this.loadAudit(true);
    }
    if (tab === 'diagnostics' && this.diagnostics() === null) {
      this.runDiagnostics();
    }
  }

  // ---- Access ----

  protected loadGrants(): void {
    this.grantsLoading.set(true);
    this.api.getGrants().subscribe({
      next: (grants) => {
        this.grants.set(grants);
        this.grantsLoading.set(false);
      },
      error: (err) => this.fail(err, () => this.grantsLoading.set(false)),
    });
  }

  protected addGrant(): void {
    const email = this.newEmail().trim();
    const appKey = this.newAppKey().trim();
    if (!email || !appKey || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api.createGrant(email, this.existingAppName() ?? this.newAppName().trim(), appKey).subscribe({
      next: (grant) => {
        this.saving.set(false);
        this.notice.set(`${grant.email} can now view ${grant.applicationName}.`);
        this.newEmail.set('');
        this.loadGrants();
        this.loadApplications();
        this.refreshOwnApplications(grant.email);
      },
      error: (err) => this.fail(err, () => this.saving.set(false)),
    });
  }

  protected removeGrant(grant: AdminGrant): void {
    if (!confirm(`Remove ${grant.email}'s access to ${grant.applicationName}?`)) {
      return;
    }
    this.api.deleteGrant(grant.id).subscribe({
      next: () => {
        this.notice.set(`Removed ${grant.email}'s access to ${grant.applicationName}.`);
        this.grants.update((grants) => grants.filter((g) => g.id !== grant.id));
        this.loadApplications();
        this.refreshOwnApplications(grant.email);
      },
      error: (err) => this.fail(err),
    });
  }

  // ---- Applications ----

  protected loadApplications(): void {
    this.api.getApplications().subscribe({
      next: (apps) => this.applications.set(apps),
      error: (err) => this.fail(err),
    });
  }

  protected startRename(app: AdminApplication): void {
    this.editingKey.set(app.appKey);
    this.editName.set(app.applicationName);
  }

  protected saveRename(app: AdminApplication): void {
    const name = this.editName().trim();
    if (!name || name === app.applicationName) {
      this.editingKey.set(null);
      return;
    }
    this.api.renameApplication(app.appKey, name).subscribe({
      next: () => {
        this.editingKey.set(null);
        this.notice.set(`Renamed ${app.applicationName} to ${name}.`);
        this.loadApplications();
        this.loadGrants();
        this.appContext.load();
      },
      error: (err) => this.fail(err),
    });
  }

  protected check(appKey: string): void {
    this.checks.update((c) => ({ ...c, [appKey]: 'running' }));
    this.api.checkApplication(appKey).subscribe({
      next: (result) => this.checks.update((c) => ({ ...c, [appKey]: result })),
      error: (err) =>
        this.checks.update((c) => ({ ...c, [appKey]: { ok: false, applicationId: null, recentItems: null, message: describeError(err) } })),
    });
  }

  protected checkResult(appKey: string): ApplicationCheck | null {
    const result = this.checks()[appKey];
    return result && result !== 'running' ? result : null;
  }

  protected isChecking(appKey: string): boolean {
    return this.checks()[appKey] === 'running';
  }

  // ---- Audit ----

  protected loadAudit(reset: boolean): void {
    const before = reset ? undefined : this.audit().at(-1)?.id;
    this.auditLoading.set(true);
    this.api
      .getAudit({
        email: this.auditEmail().trim(),
        appKey: this.auditAppKey(),
        failedOnly: this.auditFailedOnly(),
        beforeId: before,
        take: AUDIT_PAGE,
      })
      .subscribe({
        next: (entries) => {
          this.audit.update((current) => (reset ? entries : [...current, ...entries]));
          this.auditHasMore.set(entries.length === AUDIT_PAGE);
          this.auditLoading.set(false);
        },
        error: (err) => this.fail(err, () => this.auditLoading.set(false)),
      });
  }

  protected toggleAudit(id: number): void {
    this.expandedAudit.update((current) => (current === id ? null : id));
  }

  protected actionLabel(action: string): string {
    return (
      {
        query: 'Query',
        'ai.generate': 'AI: write',
        'ai.fix': 'AI: fix',
        'admin.grant': 'Admin: grant',
        'admin.revoke': 'Admin: revoke',
        'admin.rename': 'Admin: rename',
      }[action] ?? action
    );
  }

  // ---- Diagnostics ----

  protected runDiagnostics(): void {
    this.diagnosticsLoading.set(true);
    this.api.getDiagnostics().subscribe({
      next: (checks) => {
        this.diagnostics.set(checks);
        this.diagnosticsLoading.set(false);
      },
      error: (err) => this.fail(err, () => this.diagnosticsLoading.set(false)),
    });
  }

  private refreshOwnApplications(email: string): void {
    if (email.toLowerCase() === this.appContext.user()?.email) {
      this.appContext.load();
    }
  }

  private fail(err: unknown, after?: () => void): void {
    this.error.set(describeError(err));
    this.notice.set(null);
    after?.();
  }
}
