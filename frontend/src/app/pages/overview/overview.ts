import { DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApplicationContextService } from '../../core/application-context.service';
import { OverviewResponse } from '../../core/models';
import { TelemetryApiService, describeError } from '../../core/telemetry-api.service';
import { ChartSeries, TimeChart } from '../../shared/time-chart';

interface Kpi {
  label: string;
  value: string;
  /** e.g. "▲ 12% vs previous 24 hours"; null when there is nothing to compare. */
  change: string | null;
  /** 'bad' when the change is in the unwanted direction (more failures), 'good' when it improved. */
  tone: 'good' | 'bad' | 'neutral';
  hint?: string;
}

const RANGES = [
  { label: 'Last hour', minutes: 60 },
  { label: 'Last 6 hours', minutes: 360 },
  { label: 'Last 24 hours', minutes: 1440 },
  { label: 'Last 3 days', minutes: 4320 },
  { label: 'Last 7 days', minutes: 10080 },
  { label: 'Last 30 days', minutes: 43200 },
];

@Component({
  selector: 'app-overview',
  imports: [FormsModule, DecimalPipe, TimeChart],
  templateUrl: './overview.html',
})
export class Overview {
  private readonly api = inject(TelemetryApiService);
  protected readonly appContext = inject(ApplicationContextService);

  protected readonly ranges = RANGES;
  protected readonly rangeMinutes = signal(1440);
  protected readonly data = signal<OverviewResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly rangeLabel = computed(() => RANGES.find((r) => r.minutes === this.rangeMinutes())?.label.replace('Last ', '') ?? '');

  protected readonly kpis = computed<Kpi[]>(() => {
    const d = this.data();
    if (!d) {
      return [];
    }
    const k = d.kpis;
    const period = `vs previous ${this.rangeLabel()}`;
    const failureRate = k.requests ? (100 * k.failedRequests) / k.requests : 0;
    const previousRate = k.previousRequests ? (100 * k.previousFailedRequests) / k.previousRequests : null;
    return [
      { label: 'Requests', value: k.requests.toLocaleString(), ...percentChange(k.requests, k.previousRequests, period, 'neutral') },
      {
        label: 'Failure rate',
        value: `${failureRate.toFixed(2)}%`,
        hint: `${k.failedRequests.toLocaleString()} failed`,
        ...(previousRate === null
          ? { change: null, tone: 'neutral' as const }
          : pointChange(failureRate, previousRate, period)),
      },
      { label: 'P95 response time', value: formatMs(k.p95DurationMs), hint: `average ${formatMs(k.avgDurationMs)}`, change: null, tone: 'neutral' },
      { label: 'Exceptions', value: k.exceptions.toLocaleString(), ...percentChange(k.exceptions, k.previousExceptions, period, 'up-is-bad') },
      { label: 'Failed dependency calls', value: k.failedDependencies.toLocaleString(), change: null, tone: 'neutral' },
    ];
  });

  protected readonly timestamps = computed(() => this.data()?.series.map((p) => p.timestamp) ?? []);
  protected readonly requestSeries = computed<ChartSeries[]>(() => [
    { name: 'Requests', color: 'var(--series-1)', values: this.data()?.series.map((p) => p.requests) ?? [] },
    { name: 'Failed requests', color: 'var(--series-2)', values: this.data()?.series.map((p) => p.failedRequests) ?? [] },
  ]);
  protected readonly latencySeries = computed<ChartSeries[]>(() => [
    { name: 'P95 response time', color: 'var(--series-1)', values: this.data()?.series.map((p) => p.p95DurationMs) ?? [] },
  ]);
  protected readonly exceptionSeries = computed<ChartSeries[]>(() => [
    { name: 'Exceptions', color: 'var(--series-1)', values: this.data()?.series.map((p) => p.exceptions) ?? [] },
  ]);

  protected readonly formatCount = (v: number) => (v >= 10_000 ? `${(v / 1000).toFixed(0)}k` : Math.round(v).toLocaleString());
  protected readonly formatMs = (v: number) => formatMs(v);

  private sub: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    effect(() => {
      const appKey = this.appContext.selectedKey();
      const range = this.rangeMinutes();
      untracked(() => (appKey ? this.load(appKey, range) : this.data.set(null)));
    });
  }

  protected refresh(): void {
    const appKey = this.appContext.selectedKey();
    if (appKey) {
      this.load(appKey, this.rangeMinutes(), true);
    }
  }

  private load(appKey: string, range: number, fresh = false): void {
    this.sub?.unsubscribe();
    this.loading.set(true); // the previous render stays visible (dimmed) while reloading
    this.error.set(null);
    this.sub = this.api.getOverview(appKey, range, fresh).subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
      },
    });
  }
}

function formatMs(value: number | null): string {
  if (value === null || value === undefined) {
    return '—';
  }
  return value >= 1000 ? `${(value / 1000).toFixed(value >= 10_000 ? 0 : 1)} s` : `${Math.round(value)} ms`;
}

function percentChange(current: number, previous: number, period: string, direction: 'neutral' | 'up-is-bad'): Pick<Kpi, 'change' | 'tone'> {
  if (!previous) {
    return { change: null, tone: 'neutral' };
  }
  const pct = ((current - previous) / previous) * 100;
  if (Math.abs(pct) < 0.5) {
    return { change: `no change ${period}`, tone: 'neutral' };
  }
  const tone = direction === 'neutral' ? 'neutral' : pct > 0 ? 'bad' : 'good';
  return { change: `${pct > 0 ? '▲' : '▼'} ${Math.abs(pct).toFixed(0)}% ${period}`, tone };
}

function pointChange(current: number, previous: number, period: string): Pick<Kpi, 'change' | 'tone'> {
  const diff = current - previous;
  if (Math.abs(diff) < 0.01) {
    return { change: `no change ${period}`, tone: 'neutral' };
  }
  return { change: `${diff > 0 ? '▲' : '▼'} ${Math.abs(diff).toFixed(2)} pts ${period}`, tone: diff > 0 ? 'bad' : 'good' };
}
