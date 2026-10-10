import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
// Order matters: Monaco's editor features (suggest, hover, find, folding — but none of its other languages) must
// register before the Kusto contribution starts Monaco's services.
import 'monaco-editor/esm/vs/editor/edcore.main.js';
import * as monaco from 'monaco-editor/esm/vs/editor/editor.api.js';
import { getKustoWorker } from '@kusto/monaco-kusto';
import type { EngineSchema } from '@kusto/monaco-kusto';
import { KqlSchema } from '../../core/models';

/** Monaco + Kusto language service: IntelliSense, syntax highlighting, diagnostics and hovers for KQL. */
@Component({
  selector: 'app-kusto-editor',
  template: '<div #host class="kusto-editor"></div>',
})
export class KustoEditor implements AfterViewInit {
  readonly value = model('');
  readonly schema = input<KqlSchema | null>(null);
  /** Ctrl/Cmd+Enter or Shift+Enter, as in the Azure portal. */
  readonly runRequested = output<void>();

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private editor: monaco.editor.IStandaloneCodeEditor | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.editor?.getModel()?.dispose();
      this.editor?.dispose();
    });

    // Push text changes made outside the editor (sample queries, AI suggestions) into Monaco.
    effect(() => {
      const value = this.value();
      if (this.editor && this.editor.getValue() !== value) {
        this.editor.setValue(value);
      }
    });

    // Give the language service the application's tables and columns for completions and validation.
    effect(() => {
      const schema = this.schema();
      if (this.editor && schema) {
        void applySchema(this.editor, schema);
      }
    });
  }

  ngAfterViewInit(): void {
    configureMonaco();
    const dark = window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
    this.editor = monaco.editor.create(this.host().nativeElement, {
      value: this.value(),
      language: 'kusto',
      theme: dark ? 'kusto-dark' : 'kusto-light',
      automaticLayout: true,
      minimap: { enabled: false },
      fontSize: 13,
      lineNumbers: 'on',
      scrollBeyondLastLine: false,
      wordWrap: 'on',
      fixedOverflowWidgets: true,
      suggest: { showWords: false },
    });

    this.editor.onDidChangeModelContent(() => this.value.set(this.editor!.getValue()));
    this.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => this.runRequested.emit());
    this.editor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.Enter, () => this.runRequested.emit());

    const schema = this.schema();
    if (schema) {
      void applySchema(this.editor, schema);
    }
  }

  focus(): void {
    this.editor?.focus();
  }

  /** Inserts text at the cursor (used by the table and column explorer). */
  insert(text: string): void {
    const editor = this.editor;
    if (!editor) {
      return;
    }
    const selection = editor.getSelection();
    if (selection) {
      editor.executeEdits('explorer', [{ range: selection, text, forceMoveMarkers: true }]);
    }
    editor.focus();
  }
}

let configured = false;

/** Registers Monaco's web workers and its stylesheet once per page load. */
function configureMonaco(): void {
  if (configured) {
    return;
  }
  configured = true;

  (globalThis as { MonacoEnvironment?: monaco.Environment }).MonacoEnvironment = {
    getWorker: (_workerId: string, label: string) =>
      label === 'kusto'
        ? new Worker(new URL('./kusto.worker', import.meta.url), { type: 'module' })
        : new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
  };

  // Monaco's CSS is built as a separate, non-injected stylesheet (angular.json) so only this page loads it.
  if (!document.querySelector('link[data-monaco-css]')) {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'monaco-editor.css';
    link.dataset['monacoCss'] = '';
    document.head.appendChild(link);
  }
}

async function applySchema(editor: monaco.editor.IStandaloneCodeEditor, schema: KqlSchema): Promise<void> {
  const model = editor.getModel();
  if (!model) {
    return;
  }
  const database = {
    name: 'ApplicationInsights',
    tables: schema.tables.map((t) => ({
      name: t.name,
      entityType: 'Table' as const,
      docstring: t.description ?? undefined,
      columns: t.columns.map((c) => ({ name: c.name, type: c.type })),
    })),
    functions: [],
    graphs: [],
    entityGroups: [],
    majorVersion: 1,
    minorVersion: 0,
  };
  const engineSchema: EngineSchema = {
    clusterType: 'Engine',
    cluster: { connectionString: 'https://api.applicationinsights.io', databases: [database] },
    database,
  };
  const workerAccessor = await getKustoWorker();
  const worker = await workerAccessor(model.uri);
  await worker.setSchema(engineSchema);
}
