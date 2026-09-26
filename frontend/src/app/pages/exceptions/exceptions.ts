import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription, forkJoin } from 'rxjs';
import { ApplicationContextService } from '../../core/application-context.service';
import { prettyJson, severityClass, severityLabel } from '../../core/format';
import { ExceptionDetail, ExceptionEntry, ExceptionGroup, TIME_RANGES } from '../../core/models';
import { TelemetryApiService, describeError } from '../../core/telemetry-api.service';

@Component({
  selector: 'app-exceptions',
  imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './exceptions.html',
})
export class Exceptions {
  private readonly api = inject(TelemetryApiService);
  protected readonly appContext = inject(ApplicationContextService);
  private readonly router = inject(Router);

  protected readonly ranges = TIME_RANGES;

  protected rangeMinutes = signal(1440);
  protected search = signal('');
  protected roleName = signal('');
  protected problemId = signal<string | null>(null);
  protected autoRefresh = signal(false);

  protected readonly groups = signal<ExceptionGroup[]>([]);
  protected readonly exceptions = signal<ExceptionEntry[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly lastRefresh = signal<Date | null>(null);

  protected readonly selected = signal<ExceptionEntry | null>(null);
  protected readonly detail = signal<ExceptionDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);

  protected readonly totals = computed(() => {
    const groups = this.groups();
    return {
      count: groups.reduce((sum, g) => sum + g.count, 0),
      problems: groups.length,
      operations: groups.reduce((sum, g) => sum + g.affectedOperations, 0),
    };
  });
  protected readonly maxGroupCount = computed(() => Math.max(1, ...this.groups().map((g) => g.count)));

  protected readonly severityLabel = severityLabel;
  protected readonly severityClass = severityClass;
  protected readonly prettyJson = prettyJson;

  private loadSub: Subscription | undefined;
  private detailSub: Subscription | undefined;
  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      clearInterval(this.timer);
      this.loadSub?.unsubscribe();
      this.detailSub?.unsubscribe();
    });

    // Reload whenever a different application is selected in the header.
    effect(() => {
      const appKey = this.appContext.selectedKey();
      untracked(() => {
        this.close();
        this.problemId.set(null);
        if (appKey) {
          this.load();
        } else {
          this.groups.set([]);
          this.exceptions.set([]);
        }
      });
    });
  }

  protected load(): void {
    const appKey = this.appContext.selectedKey();
    if (!appKey) {
      return;
    }
    this.loadSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);

    const filter = {
      appKey,
      rangeMinutes: this.rangeMinutes(),
      search: this.search().trim(),
      roleName: this.roleName().trim(),
      problemId: this.problemId() ?? undefined,
      take: 300,
    };

    this.loadSub = forkJoin({
      groups: this.api.getExceptionSummary(filter),
      exceptions: this.api.getExceptions(filter),
    }).subscribe({
      next: ({ groups, exceptions }) => {
        this.groups.set(groups);
        this.exceptions.set(exceptions);
        this.lastRefresh.set(new Date());
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
      },
    });
  }

  protected selectGroup(problemId: string): void {
    this.problemId.update((current) => (current === problemId ? null : problemId));
    this.load();
  }

  protected toggleAutoRefresh(enabled: boolean): void {
    this.autoRefresh.set(enabled);
    clearInterval(this.timer);
    if (enabled) {
      this.timer = setInterval(() => this.load(), 30_000);
    }
  }

  protected open(entry: ExceptionEntry): void {
    const appKey = this.appContext.selectedKey();
    if (!appKey) {
      return;
    }
    this.selected.set(entry);
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(true);
    this.detailSub?.unsubscribe();
    this.detailSub = this.api.getException(appKey, entry.itemId, this.rangeMinutes() + 60).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: (err) => {
        this.detailError.set(describeError(err));
        this.detailLoading.set(false);
      },
    });
  }

  protected close(): void {
    this.selected.set(null);
    this.detail.set(null);
  }

  protected viewOperation(operationId: string): void {
    this.router.navigate(['/live'], { queryParams: { operationId } });
  }

  protected copy(text: string): void {
    navigator.clipboard?.writeText(text);
  }
}
