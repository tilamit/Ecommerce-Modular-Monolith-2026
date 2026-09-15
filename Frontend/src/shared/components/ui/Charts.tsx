import { ResponsiveContainer, AreaChart, Area, XAxis, YAxis, Tooltip, CartesianGrid } from 'recharts';
import { formatCurrency, formatDate } from '../../lib/format';

interface DailySeriesProps {
  title: string;
  data: { day: string; value: number }[];
  /** Formats the value for the tooltip and the screen-reader summary. */
  format?: (value: number) => string;
  color?: string;
}

/**
 * A 30-day area chart.
 *
 * Charts are decorative to a screen reader no matter how good the SVG is, so each one is
 * paired with a text summary and the SVG itself is hidden from the accessibility tree
 * (spec §11.4: "a text summary for screen readers"). The summary is the real content;
 * the picture is the convenience.
 */
export const DailySeriesChart = ({
  title,
  data,
  format = (value) => String(value),
  color = 'var(--color-brand-500)',
}: DailySeriesProps) => {
  const total = data.reduce((sum, point) => sum + point.value, 0);
  const peak = data.reduce((best, point) => (point.value > best.value ? point : best), data[0]);

  const isEmpty = total === 0;

  return (
    <section className="rounded-card border border-border-subtle p-4">
      <h3 className="text-sm font-medium text-content">{title}</h3>

      {/* The accessible equivalent of the picture. */}
      <p className="sr-only">
        {isEmpty
          ? `${title}: no activity in the last ${data.length} days.`
          : `${title} over the last ${data.length} days: ${format(total)} in total, peaking at ${format(
              peak.value,
            )} on ${formatDate(peak.day)}.`}
      </p>

      <p aria-hidden="true" className="mt-1 text-2xl font-semibold text-content">
        {format(total)}
      </p>

      {isEmpty ? (
        // Spec §11.4: "a sensible empty state for a month with no data" - a flat line at
        // zero looks like a broken chart rather than a quiet month.
        <p aria-hidden="true" className="mt-6 text-center text-sm text-content-muted">
          No activity in this period.
        </p>
      ) : (
        <div aria-hidden="true" className="mt-2 h-40">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: 0 }}>
              <defs>
                <linearGradient id={`fill-${title.replace(/\W/g, '')}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={color} stopOpacity={0.35} />
                  <stop offset="100%" stopColor={color} stopOpacity={0} />
                </linearGradient>
              </defs>

              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border-subtle)" vertical={false} />

              <XAxis
                dataKey="day"
                tick={{ fontSize: 11, fill: 'var(--color-content-muted)' }}
                tickFormatter={(day: string) => day.slice(5)}
                interval="preserveStartEnd"
                minTickGap={24}
              />
              <YAxis tick={{ fontSize: 11, fill: 'var(--color-content-muted)' }} width={40} />

              <Tooltip
                contentStyle={{
                  backgroundColor: 'var(--color-surface-raised)',
                  border: '1px solid var(--color-border-subtle)',
                  borderRadius: '0.5rem',
                  fontSize: '0.8rem',
                }}
                // Recharts types these loosely (ReactNode / ValueType), so both callbacks
                // narrow defensively rather than asserting.
                labelFormatter={(day) => (typeof day === 'string' ? formatDate(day) : '')}
                formatter={(value) => [typeof value === 'number' ? format(value) : String(value), title]}
              />

              <Area
                type="monotone"
                dataKey="value"
                stroke={color}
                strokeWidth={2}
                fill={`url(#fill-${title.replace(/\W/g, '')})`}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </section>
  );
};

export const StatTile = ({
  label,
  value,
  hint,
}: {
  label: string;
  value: string | number;
  hint?: string;
}) => (
  <div className="rounded-card border border-border-subtle p-4">
    <p className="text-xs font-medium uppercase tracking-wide text-content-muted">{label}</p>
    <p className="mt-1 text-2xl font-semibold text-content">{value}</p>
    {hint !== undefined && <p className="mt-0.5 text-xs text-content-muted">{hint}</p>}
  </div>
);

export const money = (currencyCode: string) => (value: number) => formatCurrency(value, currencyCode);
