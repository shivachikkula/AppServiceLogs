import { TestBed } from '@angular/core/testing';
import { TimeChart, niceScale } from './time-chart';

describe('niceScale', () => {
  it.each([
    [0, 4, 1],
    [3, 3, 1],
    [41, 50, 10],
    [783, 800, 200],
    [1210, 1500, 500],
    [8420, 10_000, 2000],
  ])('%s → max %s, step %s', (value, max, step) => {
    expect(niceScale(value)).toEqual({ max, step });
  });
});

describe('TimeChart', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver.
    globalThis.ResizeObserver ??= class {
      observe = vi.fn();
      disconnect = vi.fn();
      unobserve = vi.fn();
    } as unknown as typeof ResizeObserver;
  });

  function render(kind: 'line' | 'bar', series: { name: string; values: (number | null)[] }[]) {
    const fixture = TestBed.createComponent(TimeChart);
    fixture.componentRef.setInput('title', 'Requests');
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('timestamps', ['2026-10-10T10:00:00Z', '2026-10-10T10:30:00Z', '2026-10-10T11:00:00Z']);
    fixture.componentRef.setInput('series', series.map((s, i) => ({ ...s, color: `var(--series-${i + 1})` })));
    fixture.detectChanges();
    return fixture;
  }

  it('draws one path per line series and a legend for two or more series', () => {
    const fixture = render('line', [
      { name: 'Requests', values: [10, 20, 30] },
      { name: 'Failed requests', values: [1, null, 3] },
    ]);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelectorAll('path[stroke]').length).toBe(2);
    // A null value breaks the line into separate segments.
    expect(el.querySelectorAll('path[stroke]')[1].getAttribute('d')?.match(/M/g)?.length).toBe(2);
    expect(el.querySelector('.chart-legend')?.textContent).toContain('Failed requests');
  });

  it('has no legend for a single series and draws one bar per non-zero value', () => {
    const fixture = render('bar', [{ name: 'Exceptions', values: [5, 0, 2] }]);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.chart-legend')).toBeNull();
    expect(el.querySelectorAll('path[fill]').length).toBe(2);
  });

  it('shows every series in the tooltip when navigating with the keyboard', () => {
    const fixture = render('line', [
      { name: 'Requests', values: [10, 20, 30] },
      { name: 'Failed requests', values: [1, 2, 3] },
    ]);
    const hit: SVGRectElement = fixture.nativeElement.querySelector('.chart-hit');

    hit.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
    fixture.detectChanges();

    const tooltip = fixture.nativeElement.querySelector('.chart-tooltip')?.textContent ?? '';
    expect(tooltip).toContain('10');
    expect(tooltip).toContain('Failed requests');
  });

  it('switches to an accessible table view', () => {
    const fixture = render('line', [{ name: 'Requests', values: [10, 20, 30] }]);
    fixture.nativeElement.querySelector('.chart-table-toggle').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.chart-table tbody tr').length).toBe(3);
  });
});
