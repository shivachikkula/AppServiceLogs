import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { prettyJson, severityClass, severityLabel } from '../../core/format';
import { ItemType, LogEntry, SEVERITY_LABELS, TIME_RANGES } from '../../core/models';
import { TelemetryApiService, describeError } from '../../core/telemetry-api.service';

const MAX_BUFFER = 2000;

@Component({
  selector: 'app-live-logs',
  imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './live-logs.html',
})
export class LiveLogs implements OnInit {
  private readonly api = inject(TelemetryApiService);

  /** Optional deep-link filter, e.g. /live?operationId=abc (bound from the query string). */
  readonly operationIdParam = input<string | undefined>(undefined, { alias: 'operationId' });

  protected readonly itemTypes: { value: ItemType; label: string }[] = [
    { value: 'trace', label: 'Traces' },
    { value: 'request', label: 'Requests' },
    { value: 'dependency', label: 'Dependencies' },
    { value: 'exception', label: 'Exceptions' },
    { value: 'customEvent', label: 'Events' },
  ];
  protected readonly severities = Object.entries(SEVERITY_LABELS).map(([value, label]) => ({ value: +value, label }));
  protected readonly lookbacks = TIME_RANGES.filter((r) => r.minutes <= 1440);
  protected readonly intervals = [5, 10, 30, 60];

  // Filters
  protected selectedTypes = signal<ItemType[]>(['trace', 'request', 'dependency', 'exception', 'customEvent']);
  protected minSeverity = signal(0);
  protected lookbackMinutes = signal(15);
  protected intervalSeconds = signal(10);
  protected search = signal('');
  protected roleName = signal('');
  protected operationId = signal('');

  // State
  protected readonly entries = signal<LogEntry[]>([]);
  protected readonly paused = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly lastPoll = signal<Date | null>(null);
  protected readonly newIds = signal<Set<string>>(new Set());
  protected readonly expandedId = signal<string | null>(null);

  protected readonly counts = computed(() => {
    const counts: Record<string, number> = {};
    for (const e of this.entries()) {
      counts[e.itemType] = (counts[e.itemType] ?? 0) + 1;
    }
    return counts;
  });

  protected readonly severityLabel = severityLabel;
  protected readonly severityClass = severityClass;
  protected readonly prettyJson = prettyJson;

  private cursor: string | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private request: Subscription | undefined;
  private generation = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  ngOnInit(): void {
    const operationId = this.operationIdParam();
    if (operationId) {
      this.operationId.set(operationId);
      this.lookbackMinutes.set(1440);
    }
    this.restart();
  }

  protected toggleType(type: ItemType, checked: boolean): void {
    this.selectedTypes.update((types) => (checked ? [...types, type] : types.filter((t) => t !== type)));
    this.restart();
  }

  protected togglePause(): void {
    this.paused.update((p) => !p);
    if (this.paused()) {
      clearTimeout(this.timer);
    } else {
      this.poll();
    }
  }

  protected clear(): void {
    this.entries.set([]);
    this.expandedId.set(null);
  }

  protected filterByOperation(operationId: string | null): void {
    if (operationId) {
      this.operationId.set(operationId);
      this.restart();
    }
  }

  protected toggleExpanded(id: string): void {
    this.expandedId.update((current) => (current === id ? null : id));
  }

  /** Discards the buffer and reloads using the current filters. */
  protected restart(): void {
    this.stop();
    this.generation++;
    this.cursor = null;
    this.entries.set([]);
    this.expandedId.set(null);
    this.error.set(null);
    this.poll();
  }

  private stop(): void {
    clearTimeout(this.timer);
    this.request?.unsubscribe();
  }

  private poll(): void {
    clearTimeout(this.timer);
    const generation = this.generation;
    const initial = this.cursor === null;
    this.loading.set(true);

    this.request = this.api
      .getLiveLogs({
        since: this.cursor,
        lookbackMinutes: this.lookbackMinutes(),
        types: this.selectedTypes(),
        minSeverity: this.minSeverity(),
        search: this.search().trim(),
        roleName: this.roleName().trim(),
        operationId: this.operationId().trim(),
        take: initial ? 500 : 200,
      })
      .subscribe({
        next: (response) => {
          if (generation !== this.generation) {
            return;
          }
          this.cursor = response.cursor ?? response.serverTime;
          this.merge(response.items, !initial);
          this.error.set(null);
          this.lastPoll.set(new Date());
        },
        error: (err) => {
          this.error.set(describeError(err));
          this.finish(generation);
        },
        complete: () => this.finish(generation),
      });
  }

  private finish(generation: number): void {
    if (generation !== this.generation) {
      return;
    }
    this.loading.set(false);
    if (!this.paused()) {
      this.timer = setTimeout(() => this.poll(), this.intervalSeconds() * 1000);
    }
  }

  private merge(items: LogEntry[], highlight: boolean): void {
    if (items.length === 0) {
      return;
    }
    const existing = new Set(this.entries().map((e) => e.itemId));
    const fresh = items.filter((i) => !existing.has(i.itemId));
    const merged = [...fresh, ...this.entries()]
      .sort((a, b) => b.timestamp.localeCompare(a.timestamp))
      .slice(0, MAX_BUFFER);
    this.entries.set(merged);

    if (highlight && fresh.length > 0) {
      const ids = new Set(fresh.map((f) => f.itemId));
      this.newIds.set(ids);
      setTimeout(() => {
        if (this.newIds() === ids) {
          this.newIds.set(new Set());
        }
      }, 3000);
    }
  }
}
