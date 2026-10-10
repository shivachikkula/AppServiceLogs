/// <reference lib="webworker" />
// Monaco's 'kusto' language worker (completions, diagnostics, hovers). It imports Monaco's editor worker itself and
// then installs its own message handler, so the generic editor worker must not be imported again after it.
import './worker-globals';
import '@kusto/monaco-kusto/release/esm/kusto.worker.js';
