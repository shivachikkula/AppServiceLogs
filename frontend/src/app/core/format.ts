import { SEVERITY_LABELS } from './models';

export function severityLabel(level: number): string {
  return SEVERITY_LABELS[level] ?? String(level);
}

export function severityClass(level: number): string {
  return ['sev-verbose', 'sev-info', 'sev-warning', 'sev-error', 'sev-critical'][level] ?? 'sev-info';
}

/** Pretty-prints a JSON string, falling back to the raw text. */
export function prettyJson(value: string | null): string {
  if (!value) {
    return '';
  }
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
