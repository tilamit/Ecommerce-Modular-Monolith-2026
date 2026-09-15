/**
 * Formatting helpers.
 *
 * Everything goes through `Intl`, never hand-rolled string maths (spec §11.4). All
 * timestamps arrive as UTC and are rendered in the browser's own zone.
 */

const currencyFormatters = new Map<string, Intl.NumberFormat>();

/** Formatters are expensive to construct, so they are memoised per currency. */
const currencyFormatter = (currencyCode: string): Intl.NumberFormat => {
  let formatter = currencyFormatters.get(currencyCode);

  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(undefined, { style: 'currency', currency: currencyCode });
    currencyFormatters.set(currencyCode, formatter);
  }

  return formatter;
};

export const formatCurrency = (amount: number, currencyCode = 'USD'): string =>
  currencyFormatter(currencyCode).format(amount);

const dateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });
const dateTimeFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

/**
 * Parses an API timestamp.
 *
 * The API emits UTC, but not every value carries a trailing Z. Appending one when it is
 * missing prevents the browser from reading the value as local time - which would silently
 * shift every displayed timestamp by the user's offset.
 */
const parseUtc = (utcIso: string): Date =>
  new Date(/[Zz]|[+-]\d{2}:\d{2}$/.test(utcIso) ? utcIso : `${utcIso}Z`);

export const formatDate = (utcIso: string): string => dateFormatter.format(parseUtc(utcIso));

export const formatDateTime = (utcIso: string): string => dateTimeFormatter.format(parseUtc(utcIso));

const relativeFormatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

export const formatRelative = (utcIso: string): string => {
  const deltaMs = parseUtc(utcIso).getTime() - Date.now();
  const deltaMinutes = Math.round(deltaMs / 60_000);

  if (Math.abs(deltaMinutes) < 60) {
    return relativeFormatter.format(deltaMinutes, 'minute');
  }

  const deltaHours = Math.round(deltaMinutes / 60);

  return Math.abs(deltaHours) < 24
    ? relativeFormatter.format(deltaHours, 'hour')
    : relativeFormatter.format(Math.round(deltaHours / 24), 'day');
};

/** Percentage saved, for a strike-through price badge. Null when there is no genuine discount. */
export const discountPercent = (price: number, compareAtPrice?: number | null): number | null => {
  if (compareAtPrice === null || compareAtPrice === undefined || compareAtPrice <= price) {
    return null;
  }

  return Math.round(((compareAtPrice - price) / compareAtPrice) * 100);
};
