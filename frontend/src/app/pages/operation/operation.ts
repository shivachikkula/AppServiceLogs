import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApplicationContextService } from '../../core/application-context.service';
import { SEVERITY_LABELS } from '../../core/models';
import { TelemetryApiService, describeError } from '../../core/telemetry-api.service';
import { Timeline, TimelineRow, buildTimeline, formatDuration } from './timeline';

/** Waterfall of everything recorded for one operation: requests, dependencies, exceptions, traces and events. */
@Component({
  selector: 'app-operation',
  imports: [DatePipe, RouterLink],
  templateUrl: './operation.html',
})
export class Operation {
  private readonly api = inject(TelemetryApiService);
  protected readonly appContext = inject(ApplicationContextService);

  /** Route parameter and optional ?around= hint (bound by the router). */
  readonly operationId = input.required<string>();
  readonly around = input<string | undefined>();

  protected readonly timeline = signal<Timeline | null>(null);
  protected readonly truncated = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<TimelineRow | null>(null);
  protected readonly showTraces = signal(true);

  protected readonly visibleRows = computed(() => {
    const rows = this.timeline()?.rows ?? [];
    return this.showTraces() ? rows : rows.filter((r) => r.item.kind !== 'trace');
  });
  protected readonly axis = computed(() => {
    const total = this.timeline()?.totalMs ?? 0;
    return [0, 0.25, 0.5, 0.75, 1].map((f) => ({ pct: f * 100, label: f === 0 ? '0' : formatDuration(total * f) }));
  });

  protected readonly formatDuration = formatDuration;
  protected readonly severityLabels = SEVERITY_LABELS;

  private sub: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    effect(() => {
      const appKey = this.appContext.selectedKey();
      const operationId = this.operationId();
      const around = this.around();
      untracked(() => {
        if (appKey && operationId) {
          this.load(appKey, operationId, around);
        }
      });
    });
  }

  protected select(row: TimelineRow): void {
    this.selected.update((current) => (current?.item.itemId === row.item.itemId ? null : row));
  }

  protected kindLabel(kind: string): string {
    return { request: 'Request', dependency: 'Dependency', exception: 'Exception', trace: 'Trace', event: 'Event' }[kind] ?? kind;
  }

  private load(appKey: string, operationId: string, around?: string): void {
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.selected.set(null);
    this.sub = this.api.getOperation(appKey, operationId, around).subscribe({
      next: (result) => {
        this.timeline.set(buildTimeline(result.items));
        this.truncated.set(result.truncated);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
      },
    });
  }
}
