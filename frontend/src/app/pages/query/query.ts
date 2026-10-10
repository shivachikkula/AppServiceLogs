import { DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subscription, catchError, of } from 'rxjs';
import { ApplicationContextService } from '../../core/application-context.service';
import { KqlResult, KqlSample, KqlSchema, TIME_RANGES } from '../../core/models';
import { TelemetryApiService, describeError } from '../../core/telemetry-api.service';
import { DEFAULT_QUERY, KQL_SAMPLES } from './kql-samples';
import { KustoEditor } from './kusto-editor';

const DISPLAY_ROWS = 500;

@Component({
  selector: 'app-query',
  imports: [FormsModule, DecimalPipe, KustoEditor],
  templateUrl: './query.html',
})
export class Query {
  private readonly api = inject(TelemetryApiService);
  protected readonly appContext = inject(ApplicationContextService);
  private readonly editor = viewChild(KustoEditor);

  protected readonly ranges = TIME_RANGES;
  protected readonly samples = KQL_SAMPLES;

  protected readonly query = signal(DEFAULT_QUERY);
  protected readonly rangeMinutes = signal(1440);

  // Schema explorer / IntelliSense
  protected readonly schema = signal<KqlSchema | null>(null);
  protected readonly schemaError = signal<string | null>(null);
  protected readonly explorerFilter = signal('');
  protected readonly expandedTable = signal<string | null>('requests');
  protected readonly filteredTables = computed(() => {
    const filter = this.explorerFilter().trim().toLowerCase();
    const tables = this.schema()?.tables ?? [];
    if (!filter) {
      return tables;
    }
    return tables
      .map((t) => ({
        ...t,
        columns: t.name.toLowerCase().includes(filter) ? t.columns : t.columns.filter((c) => c.name.toLowerCase().includes(filter)),
      }))
      .filter((t) => t.columns.length > 0 || t.name.toLowerCase().includes(filter));
  });

  // Results
  protected readonly running = signal(false);
  protected readonly result = signal<KqlResult | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly showAllRows = signal(false);
  protected readonly visibleRows = computed(() => {
    const rows = this.result()?.rows ?? [];
    return this.showAllRows() ? rows : rows.slice(0, DISPLAY_ROWS);
  });
  protected readonly displayRows = DISPLAY_ROWS;

  // AI assistant
  protected readonly aiEnabled = toSignal(
    this.api.getFeatures().pipe(catchError(() => of({ aiQueryEnabled: false }))),
    { initialValue: { aiQueryEnabled: false } },
  );
  protected readonly aiPrompt = signal('');
  /** Send the editor's query along, so the request modifies it instead of starting from scratch. */
  protected readonly aiUseCurrent = signal(false);
  protected readonly aiBusy = signal(false);
  protected readonly aiExplanation = signal<string | null>(null);
  protected readonly aiError = signal<string | null>(null);

  private runSub: Subscription | undefined;
  private schemaSub: Subscription | undefined;
  private aiSub: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.runSub?.unsubscribe();
      this.schemaSub?.unsubscribe();
      this.aiSub?.unsubscribe();
    });

    // New application: load its schema and the query last used for it.
    effect(() => {
      const appKey = this.appContext.selectedKey();
      untracked(() => {
        this.result.set(null);
        this.error.set(null);
        this.aiExplanation.set(null);
        this.schema.set(null);
        if (appKey) {
          this.query.set(loadSavedQuery(appKey) ?? DEFAULT_QUERY);
          this.loadSchema(appKey);
        }
      });
    });

    // Remember the query per application.
    effect(() => {
      const appKey = this.appContext.selectedKey();
      const query = this.query();
      if (appKey) {
        saveQuery(appKey, query);
      }
    });
  }

  protected run(): void {
    const appKey = this.appContext.selectedKey();
    const query = this.query().trim();
    if (!appKey || !query || this.running()) {
      return;
    }
    this.runSub?.unsubscribe();
    this.running.set(true);
    this.error.set(null);
    this.showAllRows.set(false);
    this.runSub = this.api.runQuery(appKey, query, this.rangeMinutes()).subscribe({
      next: (result) => {
        this.result.set(result);
        this.running.set(false);
      },
      error: (err) => {
        this.result.set(null);
        this.error.set(describeError(err));
        this.running.set(false);
      },
    });
  }

  protected cancel(): void {
    this.runSub?.unsubscribe();
    this.running.set(false);
  }

  protected useSample(sample: KqlSample | undefined): void {
    if (sample) {
      this.query.set(sample.query);
      this.editor()?.focus();
    }
  }

  protected insert(text: string): void {
    this.editor()?.insert(text);
  }

  protected toggleTable(name: string): void {
    this.expandedTable.update((current) => (current === name ? null : name));
  }

  /** Writes a new query from the description, or fixes the current one when it failed. */
  protected askAi(fixError = false): void {
    const appKey = this.appContext.selectedKey();
    const prompt = this.aiPrompt().trim();
    if (!appKey || this.aiBusy() || (!prompt && !fixError)) {
      return;
    }
    this.aiSub?.unsubscribe();
    this.aiBusy.set(true);
    this.aiError.set(null);
    this.aiExplanation.set(null);
    const current = fixError || this.aiUseCurrent() ? this.query() : undefined;
    this.aiSub = this.api
      .generateQuery(appKey, fixError ? '' : prompt, fixError ? this.query() : current, fixError ? (this.error() ?? undefined) : undefined)
      .subscribe({
        next: (generated) => {
          this.query.set(generated.query);
          this.aiExplanation.set(generated.explanation);
          this.aiBusy.set(false);
          if (fixError) {
            this.error.set(null);
          }
          this.editor()?.focus();
        },
        error: (err) => {
          this.aiError.set(describeError(err));
          this.aiBusy.set(false);
        },
      });
  }

  protected formatCell(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }
    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }

  protected isComplex(value: unknown): boolean {
    return value !== null && typeof value === 'object';
  }

  protected exportCsv(): void {
    const result = this.result();
    if (!result) {
      return;
    }
    const escape = (value: unknown) => {
      const text = this.formatCell(value);
      return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    };
    const lines = [
      result.columns.map((c) => escape(c.name)).join(','),
      ...result.rows.map((row) => row.map(escape).join(',')),
    ];
    const blob = new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `query-${this.appContext.selectedKey() ?? 'results'}-${new Date().toISOString().slice(0, 19).replace(/:/g, '')}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  private loadSchema(appKey: string): void {
    this.schemaSub?.unsubscribe();
    this.schemaError.set(null);
    this.schemaSub = this.api.getQuerySchema(appKey).subscribe({
      next: (schema) => this.schema.set(schema),
      error: (err) => this.schemaError.set(describeError(err)),
    });
  }
}

function loadSavedQuery(appKey: string): string | null {
  try {
    return localStorage.getItem(`osse.query.${appKey}`);
  } catch {
    return null;
  }
}

function saveQuery(appKey: string, query: string): void {
  try {
    localStorage.setItem(`osse.query.${appKey}`, query);
  } catch {
    // Storage unavailable (private mode); the query simply isn't remembered.
  }
}
