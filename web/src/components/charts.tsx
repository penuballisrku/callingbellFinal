import type { ReactNode } from 'react';
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { chartColors } from '@/app/theme';
import { compactNumber, moneyShort, number } from '@/lib/format';
import { useResolvedMode } from '@/stores/theme';

/** A series; `color` is a slot in the validated categorical palette (0-7) or an explicit colour. */
export interface SeriesDef { key: string; label: string; color?: number | string }

/** Chart colours for the active mode - dark mode uses its own validated palette steps, not an inversion. */
export function useChartTheme() {
  const mode = useResolvedMode();
  const c = chartColors(mode);
  const pick = (color: number | string | undefined, i: number) => (typeof color === 'string' ? color : c.series[color ?? i]!);
  return { ...c, pick };
}

function TooltipBox({ active, payload, label, money }: { active?: boolean; payload?: { name: string; value: number; color: string }[]; label?: string; money?: boolean }) {
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-lg border border-line bg-surface px-3 py-2 text-xs shadow-[0_4px_16px_var(--cb-shadow)]">
      <div className="mb-1 font-semibold text-ink">{label}</div>
      {payload.map((p) => (
        <div key={p.name} className="flex items-center justify-between gap-4">
          <span className="inline-flex items-center gap-1.5 text-muted"><span className="h-2 w-2 rounded-sm" style={{ background: p.color }} />{p.name}</span>
          <span className="font-semibold tabular-nums text-ink">{money ? moneyShort(p.value) : number(p.value)}</span>
        </div>
      ))}
    </div>
  );
}

const legend = (show: boolean) => show
  ? <Legend iconType="square" iconSize={10} wrapperStyle={{ fontSize: 12, color: 'var(--cb-ink-2)', paddingTop: 8 }} />
  : null;

export function TrendChart({ data, x, lines, height = 260, money, area }: {
  data: object[]; x: string; lines: SeriesDef[]; height?: number; money?: boolean; area?: boolean;
}) {
  const t = useChartTheme();
  const fmt = money ? moneyShort : compactNumber;
  const axis = { stroke: t.axis, fontSize: 12, tickLine: false, axisLine: false } as const;
  const cursor = { stroke: t.axis, strokeDasharray: '3 3' };
  const dot = { r: 4, strokeWidth: 2, stroke: t.surface };
  return (
    <ResponsiveContainer width="100%" height={height}>
      {area ? (
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <CartesianGrid stroke={t.grid} vertical={false} />
          <XAxis dataKey={x} {...axis} minTickGap={24} />
          <YAxis {...axis} tickFormatter={fmt} width={56} />
          <Tooltip content={<TooltipBox money={money} />} cursor={cursor} />
          {legend(lines.length > 1)}
          {lines.map((l, i) => (
            <Area key={l.key} type="monotone" dataKey={l.key} name={l.label} stroke={t.pick(l.color, i)} strokeWidth={2}
              fill={t.pick(l.color, i)} fillOpacity={0.1} dot={false} activeDot={dot} />
          ))}
        </AreaChart>
      ) : (
        <LineChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <CartesianGrid stroke={t.grid} vertical={false} />
          <XAxis dataKey={x} {...axis} minTickGap={16} />
          <YAxis {...axis} tickFormatter={fmt} width={56} />
          <Tooltip content={<TooltipBox money={money} />} cursor={cursor} />
          {legend(lines.length > 1)}
          {lines.map((l, i) => (
            <Line key={l.key} type="monotone" dataKey={l.key} name={l.label} stroke={t.pick(l.color, i)} strokeWidth={2} dot={false} activeDot={dot} />
          ))}
        </LineChart>
      )}
    </ResponsiveContainer>
  );
}

export function BarsChart({ data, x, bars, height = 260, money, stacked }: {
  data: object[]; x: string; bars: SeriesDef[]; height?: number; money?: boolean; stacked?: boolean;
}) {
  const t = useChartTheme();
  const axis = { stroke: t.axis, fontSize: 12, tickLine: false, axisLine: false } as const;
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }} barGap={2}>
        <CartesianGrid stroke={t.grid} vertical={false} />
        <XAxis dataKey={x} {...axis} minTickGap={8} />
        <YAxis {...axis} tickFormatter={money ? moneyShort : compactNumber} width={56} />
        <Tooltip content={<TooltipBox money={money} />} cursor={{ fill: t.cursor }} />
        {legend(bars.length > 1)}
        {bars.map((b, i) => (
          <Bar key={b.key} dataKey={b.key} name={b.label} fill={t.pick(b.color, i)} stackId={stacked ? 's' : undefined}
            maxBarSize={28} stroke={t.surface} strokeWidth={stacked ? 1 : 0}
            radius={stacked ? (i === bars.length - 1 ? [4, 4, 0, 0] : [0, 0, 0, 0]) : [4, 4, 0, 0]} />
        ))}
      </BarChart>
    </ResponsiveContainer>
  );
}

/** Ranked horizontal bars rendered in HTML - a readable alternative to pie charts for distributions. */
export function RankedBars({ rows, valueLabel, max, tone = 0, format = number }: {
  rows: { key: string; label: ReactNode; value: number; sub?: ReactNode }[]; valueLabel?: string; max?: number; tone?: number; format?: (v: number) => string;
}) {
  const t = useChartTheme();
  const color = t.pick(tone, 0);
  const top = max ?? Math.max(1, ...rows.map((r) => r.value));
  return (
    <ul className="space-y-3" aria-label={valueLabel}>
      {rows.map((r) => (
        <li key={r.key} className="group">
          <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
            <span className="truncate text-ink">{r.label}</span>
            <span className="shrink-0 tabular-nums font-semibold text-ink">{format(r.value)}{r.sub && <span className="ml-1 font-normal text-muted">{r.sub}</span>}</span>
          </div>
          <div className="h-2 rounded-full bg-subtle" title={`${format(r.value)}`}>
            <div className="h-2 rounded-full transition-[width]" style={{ width: `${Math.max(2, (r.value / top) * 100)}%`, backgroundColor: color }} />
          </div>
        </li>
      ))}
    </ul>
  );
}
