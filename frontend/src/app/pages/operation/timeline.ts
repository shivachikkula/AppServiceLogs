import { OperationItem } from '../../core/models';

export interface TimelineRow {
  item: OperationItem;
  depth: number;
  /** Bar start and width as a percentage of the whole operation (spans), or the marker position (events). */
  offsetPct: number;
  widthPct: number;
  startMs: number;
  isSpan: boolean;
  failed: boolean;
}

export interface Timeline {
  rows: TimelineRow[];
  totalMs: number;
  start: number;
  root: OperationItem | null;
  failedCount: number;
  roles: string[];
}

const SPAN_KINDS = new Set(['request', 'dependency']);

/**
 * Orders an operation's telemetry as a tree: requests and dependencies nest under the span whose id matches their
 * operation_ParentId; traces, exceptions and events sit under the span they were logged in. Siblings are in time order.
 */
export function buildTimeline(items: OperationItem[]): Timeline {
  const empty: Timeline = { rows: [], totalMs: 0, start: 0, root: null, failedCount: 0, roles: [] };
  if (items.length === 0) {
    return empty;
  }

  const time = (i: OperationItem) => new Date(i.timestamp).getTime();
  const start = Math.min(...items.map(time));
  const end = Math.max(...items.map((i) => time(i) + (SPAN_KINDS.has(i.kind) ? (i.durationMs ?? 0) : 0)));
  const totalMs = Math.max(1, end - start);

  const spansById = new Map(items.filter((i) => SPAN_KINDS.has(i.kind) && i.id).map((i) => [i.id!, i]));
  const children = new Map<string, OperationItem[]>();
  const roots: OperationItem[] = [];
  for (const item of items) {
    const parent = item.parentId && spansById.has(item.parentId) && item.parentId !== item.id ? item.parentId : null;
    if (parent) {
      const list = children.get(parent) ?? [];
      list.push(item);
      children.set(parent, list);
    } else {
      roots.push(item);
    }
  }

  const byTime = (a: OperationItem, b: OperationItem) =>
    time(a) - time(b) || (SPAN_KINDS.has(b.kind) ? 1 : 0) - (SPAN_KINDS.has(a.kind) ? 1 : 0);
  const rows: TimelineRow[] = [];
  const visited = new Set<string>();
  const visit = (item: OperationItem, depth: number) => {
    if (visited.has(item.itemId)) {
      return; // guards against parent/child cycles in malformed telemetry
    }
    visited.add(item.itemId);
    const isSpan = SPAN_KINDS.has(item.kind);
    const startMs = time(item) - start;
    rows.push({
      item,
      depth,
      startMs,
      isSpan,
      offsetPct: (100 * startMs) / totalMs,
      widthPct: isSpan ? (100 * (item.durationMs ?? 0)) / totalMs : 0,
      failed: item.kind === 'exception' || item.success === false,
    });
    for (const child of [...(children.get(item.id ?? '') ?? [])].sort(byTime)) {
      visit(child, depth + 1);
    }
  };
  for (const root of [...roots].sort(byTime)) {
    visit(root, 0);
  }

  return {
    rows,
    totalMs,
    start,
    root: rows.find((r) => r.item.kind === 'request')?.item ?? rows[0]?.item ?? null,
    failedCount: rows.filter((r) => r.failed).length,
    roles: [...new Set(items.map((i) => i.roleName).filter((r): r is string => !!r))],
  };
}

export function formatDuration(ms: number | null): string {
  if (ms === null || ms === undefined) {
    return '';
  }
  if (ms < 1) {
    return '<1 ms';
  }
  return ms >= 1000 ? `${(ms / 1000).toFixed(ms >= 10_000 ? 1 : 2)} s` : `${Math.round(ms)} ms`;
}
