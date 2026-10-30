export const STATUS_LABELS = { queued: 'W kolejce', running: 'Trwa', done: 'Gotowe', failed: 'Błąd', unknown: 'Przerwane' };

// Statusy, przy których wynik dopiero powstaje.
export const PENDING = new Set(['queued', 'running']);

export const number = (value, digits = 1) =>
  Number(value).toLocaleString('pl-PL', { maximumFractionDigits: digits });

export function formatMs(value) {
  const ms = Number(value);
  return ms < 1000 ? `${number(ms, 0)} ms` : `${number(ms / 1000, 2)} s`;
}

// run_20261029_182133_123 → 29.10.2026 18:21:33
export function runDate(id) {
  const m = /^run_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})/.exec(id);
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6]}` : id;
}

// series_20261030_101500 → 30.10.2026 10:15:00
export function seriesDate(id) {
  const m = /^series_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})$/.exec(id);
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6]}` : id;
}
