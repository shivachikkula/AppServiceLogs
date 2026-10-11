import { OperationItem } from '../../core/models';
import { buildTimeline, formatDuration } from './timeline';

function item(partial: Partial<OperationItem> & Pick<OperationItem, 'itemId' | 'kind' | 'timestamp'>): OperationItem {
  return {
    id: null,
    parentId: null,
    name: partial.itemId,
    detail: null,
    target: null,
    dependencyType: null,
    resultCode: null,
    success: null,
    durationMs: null,
    severityLevel: null,
    roleName: null,
    ...partial,
  };
}

const t0 = Date.parse('2026-10-10T18:00:00Z');
const at = (ms: number) => new Date(t0 + ms).toISOString();

describe('buildTimeline', () => {
  it('nests spans by parent id and orders siblings by time', () => {
    const timeline = buildTimeline([
      item({ itemId: 'dep2', kind: 'dependency', timestamp: at(500), id: 'd2', parentId: 'r1', durationMs: 100 }),
      item({ itemId: 'req', kind: 'request', timestamp: at(0), id: 'r1', durationMs: 1000, roleName: 'api' }),
      item({ itemId: 'dep1', kind: 'dependency', timestamp: at(100), id: 'd1', parentId: 'r1', durationMs: 200 }),
      item({ itemId: 'inner', kind: 'request', timestamp: at(150), id: 'r2', parentId: 'd1', durationMs: 100, roleName: 'payments' }),
      item({ itemId: 'trace', kind: 'trace', timestamp: at(160), parentId: 'r2' }),
    ]);

    expect(timeline.rows.map((r) => `${r.depth}:${r.item.itemId}`)).toEqual(['0:req', '1:dep1', '2:inner', '3:trace', '1:dep2']);
    expect(timeline.totalMs).toBe(1000);
    expect(timeline.root?.itemId).toBe('req');
    expect(timeline.roles).toEqual(['api', 'payments']);
  });

  it('positions bars as a percentage of the whole operation', () => {
    const { rows } = buildTimeline([
      item({ itemId: 'req', kind: 'request', timestamp: at(0), id: 'r1', durationMs: 2000 }),
      item({ itemId: 'dep', kind: 'dependency', timestamp: at(500), id: 'd1', parentId: 'r1', durationMs: 1000 }),
    ]);

    expect(rows[1].offsetPct).toBe(25);
    expect(rows[1].widthPct).toBe(50);
  });

  it('counts failed spans and exceptions', () => {
    const timeline = buildTimeline([
      item({ itemId: 'req', kind: 'request', timestamp: at(0), id: 'r1', durationMs: 100, success: false }),
      item({ itemId: 'ok', kind: 'dependency', timestamp: at(10), id: 'd1', parentId: 'r1', durationMs: 10, success: true }),
      item({ itemId: 'ex', kind: 'exception', timestamp: at(50), parentId: 'r1' }),
    ]);

    expect(timeline.failedCount).toBe(2);
    expect(timeline.rows.filter((r) => r.failed).map((r) => r.item.itemId)).toEqual(['req', 'ex']);
  });

  it('treats items whose parent is missing as roots, and survives parent cycles', () => {
    const timeline = buildTimeline([
      item({ itemId: 'a', kind: 'dependency', timestamp: at(0), id: 'x', parentId: 'y', durationMs: 5 }),
      item({ itemId: 'b', kind: 'dependency', timestamp: at(1), id: 'y', parentId: 'x', durationMs: 5 }),
      item({ itemId: 'orphan', kind: 'trace', timestamp: at(2), parentId: 'gone' }),
    ]);

    // a and b point at each other, so neither is a root; only the orphan is visited.
    expect(timeline.rows.map((r) => r.item.itemId)).toEqual(['orphan']);
  });

  it('returns an empty timeline for no items', () => {
    expect(buildTimeline([]).rows).toEqual([]);
  });
});

describe('formatDuration', () => {
  it.each([
    [null, ''],
    [0.4, '<1 ms'],
    [42.4, '42 ms'],
    [1840, '1.84 s'],
    [12_345, '12.3 s'],
  ])('%s → %s', (input, expected) => {
    expect(formatDuration(input)).toBe(expected);
  });
});
