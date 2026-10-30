import { $ } from './dom.js';
import { getResults } from './api.js';
import { drawChart } from './chart.js';
import { number, formatMs, seriesDate } from './format.js';

// Zestawienie wyników (Lab 6): porównanie czasów CA i MC ze wszystkich zakończonych uruchomień.
const ALGORITHMS = [
  { id: 'CA', name: 'Rozrost CA', color: '--series-ca' },
  { id: 'MC', name: 'Monte Carlo', color: '--series-mc' },
];

let results = [];
let preferredSeries = null;   // seria wybrana przez użytkownika albo właśnie zlecona
let openRun = () => {};

// onOpenRun(id) – kliknięcie wiersza tabeli.
export function initCompare({ onOpenRun }) {
  openRun = onOpenRun;
  $('compare-filter').addEventListener('change', event => {
    preferredSeries = event.target.value;
    renderComparison();
  });
}

export function preferSeries(series) {
  preferredSeries = series;
}

export async function loadComparison() {
  try {
    results = await getResults();
  } catch (error) {
    $('compare-note').textContent = `Nie udało się wczytać wyników: ${error.message}`;
    return;
  }

  const seriesIds = [...new Set(results.map(r => r.series).filter(Boolean))].sort().reverse();
  const filter = $('compare-filter');
  filter.replaceChildren(
    new Option('Wszystkie uruchomienia', ''),
    ...seriesIds.map(id => new Option(`Seria z ${seriesDate(id)}`, id)));
  // domyślnie najnowsza seria; wybór użytkownika zostaje
  const wanted = preferredSeries ?? seriesIds[0] ?? '';
  filter.value = [...filter.options].some(o => o.value === wanted) ? wanted : '';
  renderComparison();
}

export function renderComparison() {
  const filter = $('compare-filter').value;
  const rows = results.filter(r => !filter || r.series === filter);
  if (rows.length === 0) {
    $('compare-note').textContent = 'Brak zakończonych uruchomień – uruchom serię w panelu po lewej.';
    drawChart($('compare-time'), [], { xLabel: '', yLabel: '' });
    drawChart($('compare-step'), [], { xLabel: '', yLabel: '' });
    renderTable([]);
    return;
  }

  const groups = groupByAlgorithmAndSize(rows);
  renderCharts(rows, groups);
  $('compare-note').textContent = describeParameters(rows, groups);
  renderTable(rows);
}

// Każdy punkt wykresu to średnia z powtórzeń dla tej samej pary (algorytm, liczba komórek).
function groupByAlgorithmAndSize(rows) {
  const groups = new Map();
  for (const r of rows) {
    const key = `${r.algorithm}|${r.cells}`;
    const g = groups.get(key) ?? { algorithm: r.algorithm, cells: r.cells, sim: 0, step: 0, n: 0 };
    g.sim += r.simulationMs;
    g.step += r.steps ? r.growthMs / r.steps : 0;
    g.n += 1;
    groups.set(key, g);
  }
  return [...groups.values()];
}

function renderCharts(rows, groups) {
  const points = (algorithm, pick) => groups
    .filter(g => g.algorithm === algorithm)
    .sort((a, b) => a.cells - b.cells)
    .map(g => [g.cells, pick(g)]);
  const present = ALGORITHMS.filter(a => rows.some(r => r.algorithm === a.id));
  const cellsFormat = new Intl.NumberFormat('pl-PL', { notation: 'compact', maximumFractionDigits: 1 });
  const axis = { xLabel: 'liczba komórek', xFormat: v => cellsFormat.format(v) };

  drawChart($('compare-time'),
    present.map(a => ({ name: a.name, color: a.color, points: points(a.id, g => g.sim / g.n / 1000) })),
    { ...axis, yLabel: 'czas symulacji [s]' });
  drawChart($('compare-step'),
    present.map(a => ({ name: a.name, color: a.color, points: points(a.id, g => g.step / g.n) })),
    { ...axis, yLabel: 'czas jednego kroku [ms]' });
}

// Opis parametrów pod wykresami, żeby było wiadomo, co porównujemy.
function describeParameters(rows, groups) {
  const distinct = values => [...new Set(values.filter(v => v !== null && v !== undefined))].join(' / ');
  const ca = rows.filter(r => r.algorithm === 'CA');
  const mc = rows.filter(r => r.algorithm === 'MC');
  const repeats = Math.max(...groups.map(g => g.n));
  return [
    `${rows.length} uruchomień`,
    ca.length ? `CA: zarodki ${distinct(ca.map(r => r.nucleons))}` : null,
    mc.length ? `MC: Q = ${distinct(mc.map(r => r.states))}, kroki MCS ${distinct(mc.map(r => r.mcSteps))}, nowy stan ${distinct(mc.map(r => r.mcProposal === 'Neighbour' ? 'od sąsiada' : 'losowy'))}` : null,
    `sąsiedztwo ${distinct(rows.map(r => r.neighbourhood))}, brzegi ${distinct(rows.map(r => r.boundary))}`,
    repeats > 1 ? `punkt = średnia z ${repeats} powtórzeń` : null,
  ].filter(Boolean).join(' · ');
}

function renderTable(rows) {
  const sorted = [...rows].sort((a, b) => a.algorithm.localeCompare(b.algorithm) || a.cells - b.cells || a.id.localeCompare(b.id));
  document.querySelector('#compare-table tbody').replaceChildren(...sorted.map(tableRow));
}

function tableRow(r) {
  const tr = document.createElement('tr');
  const cells = [
    [r.algorithm === 'MC' ? 'Monte Carlo' : 'Rozrost CA', false],
    [`${r.sizeX}×${r.sizeY}×${r.sizeZ}`, false],
    [number(r.cells, 0), true],
    [String(r.steps), true],
    [number(r.grainsEnd, 0), true],
    [formatMs(r.simulationMs), true],
    [r.steps ? formatMs(r.growthMs / r.steps) : '–', true],
    [`${number(r.fileMb, 1)} MB`, true],
  ];
  for (const [text, numeric] of cells) {
    const td = document.createElement('td');
    td.textContent = text;
    if (numeric) td.className = 'num';
    tr.append(td);
  }
  tr.title = 'Kliknij, żeby otworzyć to uruchomienie';
  tr.style.cursor = 'pointer';
  tr.addEventListener('click', () => openRun(r.id));
  return tr;
}
