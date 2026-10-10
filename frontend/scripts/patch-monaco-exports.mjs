// @kusto/monaco-kusto imports 'monaco-editor/esm/vs/editor/editor.api' and '.../editor.worker' without the '.js'
// extension, which monaco-editor's "exports" map ("./*": "./*") does not resolve under Angular's esbuild-based
// builder. Add explicit entries for those two paths. Safe to run repeatedly.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
let packageJsonPath;
try {
  packageJsonPath = require.resolve('monaco-editor/package.json');
} catch {
  process.exit(0); // monaco-editor not installed (e.g. production-only install)
}
if (!existsSync(packageJsonPath)) {
  process.exit(0);
}

const pkg = JSON.parse(readFileSync(packageJsonPath, 'utf8'));
const exportsMap = pkg.exports ?? {};
const entries = {
  './esm/vs/editor/editor.api': './esm/vs/editor/editor.api.js',
  './esm/vs/editor/editor.worker': './esm/vs/editor/editor.worker.js',
};

let changed = false;
const patched = {};
for (const [key, value] of Object.entries(entries)) {
  if (!(key in exportsMap)) {
    patched[key] = value;
    changed = true;
  }
}
if (changed) {
  // Specific entries must precede the "./*" wildcard; rebuild the map with them first.
  pkg.exports = { ...patched, ...exportsMap };
  writeFileSync(packageJsonPath, JSON.stringify(pkg, null, 2) + '\n');
  console.log('Patched monaco-editor exports for @kusto/monaco-kusto.');
}
