import { DatePipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, inject, input, signal, viewChild, afterNextRender } from '@angular/core';

export interface ChartSeries {
  name: string;
  /** CSS color, normally a token such as var(--series-1). */
  color: string;
  values: (number | null)[];
}

interface Tick {
  value: number;
  y: number;
  label: string;
}

const MARGIN = { top: 12, right: 16, bottom: 26, left: 52 };

/**
 * Time series chart (line or bar) drawn as SVG: one y axis, recessive grid, 2px lines, rounded bar tops anchored to
 * the baseline, a crosshair tooltip listing every series, keyboard access, and a table view.
 */
@Component({
  selector: 'app-time-chart',
  imports: [DatePipe],
  templateUrl: './time-chart.html',
})
export class TimeChart {
  readonly title = input.required<string>();
  readonly timestamps = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();
  readonly kind = input<'line' | 'bar'>('line');
  readonly height = input(200);
  /** Formats values for axis, tooltip and table. */
  readonly format = input<(value: number) => string>((v) => v.toLocaleString());
  /** Bucket size: decides whether the axis shows times or dates. */
  readonly binMinutes = input(30);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  protected readonly width = signal(600);
  protected readonly hoverIndex = signal<number | null>(null);
  protected readonly showTable = signal(false);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly plot = computed(() => ({
    left: MARGIN.left,
    top: MARGIN.top,
    width: Math.max(10, this.width() - MARGIN.left - MARGIN.right),
    height: Math.max(10, this.height() - MARGIN.top - MARGIN.bottom),
  }));

  /** Round tick step (1, 2 or 5 × 10^n, about five ticks) and the axis maximum: the first tick above the data. */
  private readonly scale = computed(() => {
    let max = 0;
    for (const s of this.series()) {
      for (const v of s.values) {
        if (v !== null && v > max) {
          max = v;
        }
      }
    }
    return niceScale(max);
  });

  private readonly maxValue = computed(() => this.scale().max);

  protected readonly yTicks = computed<Tick[]>(() => {
    const { top, height } = this.plot();
    const { max, step } = this.scale();
    const ticks: Tick[] = [];
    for (let i = 0; i * step <= max + step / 1000; i++) {
      const value = i * step;
      ticks.push({ value, y: top + height - (value / max) * height, label: this.format()(value) });
    }
    return ticks;
  });

  /** Width of one time bucket. */
  private readonly slot = computed(() => this.plot().width / Math.max(1, this.timestamps().length));

  protected readonly xTicks = computed(() => {
    const count = this.timestamps().length;
    if (count === 0) {
      return [];
    }
    const wanted = Math.max(2, Math.min(6, Math.floor(this.plot().width / 110)));
    const step = Math.max(1, Math.round((count - 1) / (wanted - 1)));
    const ticks: { x: number; label: string }[] = [];
    for (let i = 0; i < count; i += step) {
      ticks.push({ x: this.xAt(i), label: this.formatTime(this.timestamps()[i]) });
    }
    return ticks;
  });

  protected readonly linePaths = computed(() =>
    this.kind() !== 'line'
      ? []
      : this.series().map((s) => {
          let d = '';
          let penDown = false;
          s.values.forEach((v, i) => {
            if (v === null) {
              penDown = false;
              return;
            }
            d += `${penDown ? 'L' : 'M'}${this.xAt(i).toFixed(1)},${this.yAt(v).toFixed(1)}`;
            penDown = true;
          });
          return { name: s.name, color: s.color, d };
        }),
  );

  protected readonly bars = computed(() => {
    if (this.kind() !== 'bar' || this.series().length === 0) {
      return [];
    }
    const s = this.series()[0];
    const slot = this.slot();
    const width = Math.max(1, slot - 2); // 2px surface gap between bars
    const baseline = this.plot().top + this.plot().height;
    return s.values.map((v, i) => {
      const x = this.plot().left + i * slot + (slot - width) / 2;
      const h = v ? Math.max(1, baseline - this.yAt(v)) : 0;
      return { i, d: h > 0 ? roundedTopBar(x, baseline - h, width, h, Math.min(4, width / 2, h)) : '', color: s.color };
    });
  });

  protected readonly tooltip = computed(() => {
    const i = this.hoverIndex();
    if (i === null || i >= this.timestamps().length) {
      return null;
    }
    const x = this.xAt(i);
    return {
      i,
      x,
      left: Math.min(Math.max(x + 12, 0), this.width() - 200),
      time: this.timestamps()[i],
      rows: this.series().map((s) => ({ name: s.name, color: s.color, value: s.values[i] })),
      markers: this.kind() === 'line' ? this.series().filter((s) => s.values[i] !== null).map((s) => ({ color: s.color, y: this.yAt(s.values[i]!) })) : [],
    };
  });

  protected readonly summary = computed(() => {
    const parts = this.series().map((s) => {
      const values = s.values.filter((v): v is number => v !== null);
      const max = values.length ? Math.max(...values) : 0;
      return `${s.name}: peak ${this.format()(max)}`;
    });
    return `${this.title()}. ${parts.join('; ')}. Use the arrow keys to read values, or show the table.`;
  });

  constructor() {
    afterNextRender(() => {
      const element = this.host().nativeElement;
      const observer = new ResizeObserver((entries) => this.width.set(Math.floor(entries[0].contentRect.width)));
      observer.observe(element);
      this.width.set(element.clientWidth || 600);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  protected onPointerMove(event: PointerEvent): void {
    const rect = (event.currentTarget as SVGRectElement).getBoundingClientRect();
    const x = event.clientX - rect.left;
    const index = Math.floor(x / this.slot());
    this.hoverIndex.set(Math.min(Math.max(index, 0), this.timestamps().length - 1));
  }

  protected onKeyDown(event: KeyboardEvent): void {
    const count = this.timestamps().length;
    if (count === 0) {
      return;
    }
    const current = this.hoverIndex() ?? count - 1;
    const next = event.key === 'ArrowLeft' ? current - 1 : event.key === 'ArrowRight' ? current + 1 : event.key === 'Home' ? 0 : event.key === 'End' ? count - 1 : null;
    if (next !== null) {
      event.preventDefault();
      this.hoverIndex.set(Math.min(Math.max(next, 0), count - 1));
    } else if (event.key === 'Escape') {
      this.hoverIndex.set(null);
    }
  }

  protected formatValue(value: number | null): string {
    return value === null ? '—' : this.format()(value);
  }

  protected timeFormat(): string {
    return this.binMinutes() >= 720 ? 'MMM d' : this.binMinutes() >= 60 ? 'MMM d, HH:mm' : 'HH:mm';
  }

  private xAt(index: number): number {
    return this.plot().left + (index + 0.5) * this.slot();
  }

  private yAt(value: number): number {
    const { top, height } = this.plot();
    return top + height - (value / this.maxValue()) * height;
  }

  private formatTime(iso: string): string {
    const date = new Date(iso);
    const days = this.binMinutes() >= 60;
    return days
      ? date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) +
          (this.binMinutes() < 720 ? ' ' + date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : '')
      : date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  }
}

/** Round tick step (1, 2 or 5 × 10^n) giving about five ticks, and the axis maximum as a whole number of steps. */
export function niceScale(value: number): { max: number; step: number } {
  if (value <= 0) {
    return { max: 4, step: 1 };
  }
  const rawStep = value / 5;
  const magnitude = 10 ** Math.floor(Math.log10(rawStep));
  const step = [1, 2, 5, 10].map((m) => m * magnitude).find((candidate) => candidate >= rawStep) ?? 10 * magnitude;
  return { max: Math.ceil(value / step - 1e-9) * step, step };
}

function roundedTopBar(x: number, y: number, w: number, h: number, r: number): string {
  return `M${x},${y + h}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + h}Z`;
}
