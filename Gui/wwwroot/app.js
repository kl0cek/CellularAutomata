import { Volume, sliceShape, sliceCell, sliceRGBA } from './volume.js';

const $ = id => document.getElementById(id);

const STATUS_LABELS = { queued: 'W kolejce', running: 'Trwa', done: 'Gotowe', failed: 'Błąd', unknown: 'Przerwane' };
const PENDING = new Set(['queued', 'running']);
const TABS = ['slice', 'cube', 'steps', 'log'];

const state = {
  selectedId: null,
  loadToken: 0,       // chroni przed spóźnionymi odpowiedziami po przełączeniu uruchomienia
  poll: null,
  resultsFor: null,
  volume: null,
  cubeView: null,
  cubeShownFor: null,
  stepsText: '',
  tab: 'slice',
  config: null,
  view: 'run',
  listPoll: null,
  results: [],
  compareFilter: null,
};

// ---------- pomocnicze ----------

const number = (value, digits = 1) =>
  Number(value).toLocaleString('pl-PL', { maximumFractionDigits: digits });

function formatMs(value) {
  const ms = Number(value);
  return ms < 1000 ? `${number(ms, 0)} ms` : `${number(ms / 1000, 2)} s`;
}

// run_20261029_182133_123 → 29.10.2026 18:21:33
function runDate(id) {
  const m = /^run_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})/.exec(id);
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6]}` : id;
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error(`${url}: ${response.status}`);
  return response.json();
}

function showMessage(text) {
  $('view-message').textContent = text;
}

// ---------- formularz ----------

const form = $('run-form');

function algorithm() {
  return form.elements.algorithm.value;
}

function updateAlgorithmFields() {
  const mc = algorithm() === 'MC';
  $('ca-params').hidden = mc;
  $('mc-params').hidden = !mc;
}

function readForm() {
  const int = id => Number.parseInt($(id).value, 10) || 0;
  return {
    algorithm: algorithm(),
    sizeX: int('size-x'),
    sizeY: int('size-y'),
    sizeZ: int('size-z'),
    neighbourhood: $('neighbourhood').value,
    boundary: $('boundary').value,
    nucleons: int('nucleons'),
    states: int('states'),
    mcSteps: int('mc-steps'),
    j: Number.parseFloat($('j').value) || 0,
    mcProposal: $('mc-proposal').value,
    seed: int('seed'),
  };
}

// Wypełnia formularz parametrami wybranego uruchomienia (pola jak w config.json).
function fillForm(config) {
  const set = (id, value) => { if (value !== undefined && value !== null) $(id).value = value; };
  form.elements.algorithm.value = config.Algorithm ?? 'CA';
  set('size-x', config.FullSpaceSizeX);
  set('size-y', config.FullSpaceSizeY);
  set('size-z', config.FullSpaceSizeZ);
  set('neighbourhood', config.Neighbourhood);
  set('boundary', config.BC);
  set('nucleons', config.Nucleons);
  set('states', config.States);
  set('mc-steps', config.McSteps);
  set('j', config.J);
  set('mc-proposal', config.McProposal);
  set('seed', config.Seed);
  updateAlgorithmFields();
}

function setErrors(errors, listId = 'form-errors') {
  const list = $(listId);
  list.replaceChildren(...errors.map(text => {
    const li = document.createElement('li');
    li.textContent = text;
    return li;
  }));
}

form.addEventListener('change', event => {
  if (event.target.name === 'algorithm') updateAlgorithmFields();
});

form.addEventListener('submit', async event => {
  event.preventDefault();
  setErrors([]);
  const button = $('run-button');
  button.disabled = true;
  button.textContent = 'Uruchamianie…';
  try {
    const response = await fetch('/api/runs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(readForm()),
    });
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      setErrors(Array.isArray(body?.errors) ? body.errors : [`Serwer odrzucił parametry (kod ${response.status}).`]);
      return;
    }
    await refreshRunList();
    selectRun(body.id);
  } catch {
    setErrors(['Brak połączenia z serwerem GUI – czy działa `dotnet run` w folderze Gui?']);
  } finally {
    button.disabled = false;
    button.textContent = 'Uruchom symulację';
  }
});

$('reuse-button').addEventListener('click', () => {
  if (state.config) fillForm(state.config);
});

// ---------- lista uruchomień ----------

async function refreshRunList() {
  const runs = await fetchJson('/api/runs');
  const list = $('run-list');
  list.replaceChildren();

  if (runs.length === 0) {
    const li = document.createElement('li');
    li.className = 'empty-note';
    li.textContent = 'Jeszcze nic nie uruchomiono.';
    list.append(li);
  }

  for (const run of runs) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.id = run.id;
    if (run.id === state.selectedId) button.setAttribute('aria-current', 'true');

    const title = document.createElement('span');
    title.textContent = `${run.algorithm ?? '?'} · ${run.size ?? ''}`;

    const badge = document.createElement('span');
    badge.className = `badge ${run.status}`;
    badge.textContent = STATUS_LABELS[run.status] ?? run.status;

    const meta = document.createElement('span');
    meta.className = 'meta';
    meta.textContent = [
      runDate(run.id),
      run.series ? 'seria' : null,
      run.grainsEnd ? `ziaren: ${number(run.grainsEnd, 0)}` : null,
      run.simulationMs ? formatMs(run.simulationMs) : null,
    ].filter(Boolean).join(' · ');

    button.append(title, badge, meta);
    button.addEventListener('click', () => selectRun(run.id));

    const li = document.createElement('li');
    li.append(button);
    list.append(li);
  }

  // Dopóki coś czeka w kolejce albo się liczy, lista odświeża się sama.
  const pending = runs.some(run => PENDING.has(run.status));
  if (pending && !state.listPoll) {
    state.listPoll = setInterval(() => refreshRunList().catch(() => {}), 1500);
  } else if (!pending && state.listPoll) {
    clearInterval(state.listPoll);
    state.listPoll = null;
    if (state.view === 'compare') loadComparison().catch(() => {});
  }
  return runs;
}

// ---------- wybrane uruchomienie ----------

async function selectRun(id) {
  if (state.view !== 'run') selectView('run');
  state.selectedId = id;
  state.resultsFor = null;
  state.volume = null;
  state.stepsText = '';
  const token = ++state.loadToken;
  clearInterval(state.poll);
  state.poll = null;

  for (const button of document.querySelectorAll('#run-list button')) {
    if (button.dataset.id === id) button.setAttribute('aria-current', 'true');
    else button.removeAttribute('aria-current');
  }
  $('empty-state').hidden = true;
  $('run-view').hidden = false;
  showMessage('Wczytywanie…');

  try {
    await updateRun(id, token);
  } catch (error) {
    if (token === state.loadToken) showMessage(`Nie udało się wczytać uruchomienia: ${error.message}`);
  }
}

async function updateRun(id, token) {
  const run = await fetchJson(`/api/runs/${id}`);
  if (token !== state.loadToken) return;

  state.config = run.config;
  renderHeader(run);
  renderSummary(run);
  $('log').textContent = run.log || '(brak wyjścia programu)';

  if (PENDING.has(run.status)) {
    showMessage(run.status === 'queued'
      ? 'W kolejce – obliczenia ruszą po zakończeniu wcześniejszych zleceń.'
      : 'Symulacja trwa – wyniki pojawią się po jej zakończeniu.');
    if (!state.poll) {
      state.poll = setInterval(() => updateRun(id, token).catch(() => {}), 700);
    }
    return;
  }

  clearInterval(state.poll);
  state.poll = null;
  refreshRunList().catch(() => {});

  if (run.status === 'done') {
    await loadResults(id, token);
  } else {
    showMessage('Symulacja nie zakończyła się poprawnie – szczegóły w zakładce Konsola.');
    selectTab('log');
  }
}

function renderHeader(run) {
  const c = run.config ?? {};
  const name = c.Algorithm === 'MC' ? 'Monte Carlo' : 'Rozrost CA';
  $('run-title').textContent =
    `${name} · ${c.FullSpaceSizeX}×${c.FullSpaceSizeY}×${c.FullSpaceSizeZ} · ${c.Neighbourhood}, ${c.BC}`;
  $('run-folder').textContent = run.folder;

  const badge = $('run-status');
  badge.className = `badge ${run.status}`;
  badge.textContent = STATUS_LABELS[run.status] ?? run.status;

  $('run-elapsed').textContent =
    run.elapsedSeconds != null ? `${number(run.elapsedSeconds, 1)} s` : runDate(run.id);
}

function renderSummary(run) {
  const s = run.summary;
  const c = run.config ?? {};
  const dl = $('run-summary');
  dl.replaceChildren();
  if (!s) return;

  const items = c.Algorithm === 'MC'
    ? [['Stany Q', c.States], ['Kroki MCS', s.steps], ['Nowy stan', c.McProposal === 'Neighbour' ? 'od sąsiada' : 'losowy']]
    : [['Zarodki', c.Nucleons], ['Kroki CA', s.steps]];
  items.push(
    ['Ziarna na końcu', number(s.grains_end, 0)],
    ['Symulacja', formatMs(s.simulation_ms)],
    ['Zapis pliku', formatMs(s.file_write_ms)],
    ['Plik wynikowy', `${number(s.file_mb, 1)} MB`],
    ['Całość', formatMs(s.total_ms)],
    ['Seed', c.Seed],
  );

  for (const [label, value] of items) {
    const item = document.createElement('div');
    const dt = document.createElement('dt');
    const dd = document.createElement('dd');
    dt.textContent = label;
    dd.textContent = value ?? '–';
    item.append(dt, dd);
    dl.append(item);
  }
}

async function loadResults(id, token) {
  if (state.resultsFor === id) return; // np. dwa nakładające się odpytania
  state.resultsFor = id;
  showMessage('Wczytywanie mikrostruktury…');

  const [micro, steps] = await Promise.all([fetch(`/api/runs/${id}/micro`), fetch(`/api/runs/${id}/steps`)]);
  if (token !== state.loadToken) return;
  if (!micro.ok) {
    showMessage('Brak pliku z mikrostrukturą dla tego uruchomienia.');
    return;
  }

  state.volume = new Volume(await micro.arrayBuffer());
  state.stepsText = steps.ok ? await steps.text() : '';
  if (token !== state.loadToken) return;

  showMessage('');
  setupSliceControls();
  drawSlice();
  if (state.tab === 'cube') showCube();
  if (state.tab === 'steps') drawSteps();
}

// ---------- zakładki ----------

function selectTab(name) {
  state.tab = name;
  for (const button of document.querySelectorAll('.tabs button')) {
    button.setAttribute('aria-selected', String(button.dataset.tab === name));
  }
  for (const tab of TABS) $(`tab-${tab}`).hidden = tab !== name;
  if (name === 'cube') showCube();
  if (name === 'steps') drawSteps();
}

for (const button of document.querySelectorAll('.tabs button')) {
  button.addEventListener('click', () => selectTab(button.dataset.tab));
}

// ---------- przekrój 2D ----------

const AXIS_OF_LAYER = { XY: 'z', XZ: 'y', YZ: 'x' };

function setupSliceControls() {
  const { layers } = sliceShape(state.volume, $('plane').value);
  const layer = $('layer');
  layer.max = String(layers - 1);
  layer.value = String(Math.floor(layers / 2));
  layer.disabled = layers === 1;
}

function drawSlice() {
  const vol = state.volume;
  if (!vol) return;
  const plane = $('plane').value;
  const layer = Number($('layer').value);
  const { width, height, rgba } = sliceRGBA(vol, plane, layer);

  const canvas = $('slice-canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.getContext('2d').putImageData(new ImageData(rgba, width, height), 0, 0);
  $('layer-label').textContent = `${AXIS_OF_LAYER[plane]} = ${layer} z ${sliceShape(vol, plane).layers - 1}`;
}

$('plane').addEventListener('change', () => {
  if (!state.volume) return;
  setupSliceControls();
  drawSlice();
});
$('layer').addEventListener('input', drawSlice);

$('slice-canvas').addEventListener('mousemove', event => {
  const vol = state.volume;
  if (!vol) return;
  const canvas = event.currentTarget;
  const rect = canvas.getBoundingClientRect();
  const col = Math.min(canvas.width - 1, Math.max(0, Math.floor((event.clientX - rect.left) / rect.width * canvas.width)));
  const row = Math.min(canvas.height - 1, Math.max(0, Math.floor((event.clientY - rect.top) / rect.height * canvas.height)));
  const [x, y, z] = sliceCell(vol, $('plane').value, Number($('layer').value), col, row);
  $('slice-hover').textContent = `x = ${x}, y = ${y}, z = ${z} · ziarno ${vol.at(x, y, z)}`;
});

// ---------- kostka 3D ----------

async function showCube() {
  if (!state.volume) return;
  try {
    if (!state.cubeView) {
      const { CubeView } = await import('./view3d.js'); // three.js ładujemy dopiero przy pierwszym użyciu
      state.cubeView = new CubeView($('cube-container'));
    }
    if (state.cubeShownFor !== state.selectedId) {
      state.cubeView.show(state.volume);
      state.cubeShownFor = state.selectedId;
    }
  } catch (error) {
    showMessage(`Nie udało się uruchomić widoku 3D (WebGL): ${error.message}`);
  }
}

$('reset-view').addEventListener('click', () => state.cubeView?.resetView());

// ---------- przebieg (StepTimes.txt) ----------

function drawSteps() {
  const canvas = $('steps-chart');
  const note = $('steps-note');
  if (canvas.clientWidth === 0) return; // zakładka ukryta – narysujemy po jej pokazaniu

  const lines = state.stepsText.trim().split('\n').filter(Boolean);
  if (lines.length < 2) {
    canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
    note.textContent = 'Brak danych o kolejnych krokach.';
    return;
  }

  const header = lines[0].split(';');
  const rows = lines.slice(1).map(line => line.split(';').map(Number));
  const isMc = header[0] === 'mcs';
  const yColumn = header.indexOf(isMc ? 'energy' : 'empty_left');
  const points = rows.map(row => [row[0], row[yColumn]]);
  const totalMs = rows.at(-1)[header.indexOf('cumulative_ms')];

  drawChart(canvas, [{ name: '', color: '--chart-line', points }], {
    xLabel: isMc ? 'krok MCS' : 'krok CA',
    yLabel: isMc ? 'energia (pary sąsiadów na granicy)' : 'puste komórki',
    integerX: true,
  });

  const first = points[0][1];
  const last = points.at(-1)[1];
  note.textContent = isMc
    ? `Energia spadła z ${number(first, 0)} do ${number(last, 0)} (o ${number((1 - last / first) * 100, 1)}%). Czas wszystkich kroków: ${formatMs(totalMs)}.`
    : `Siatka zapełniona po ${points.at(-1)[0]} krokach. Czas wszystkich kroków: ${formatMs(totalMs)}.`;
}

// Okrągłe wartości podziałki osi (1, 2, 5 × 10^n).
function niceTicks(min, max, count = 5) {
  if (max === min) return [min];
  const raw = (max - min) / count;
  const power = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map(f => f * power).find(s => s >= raw);
  const ticks = [];
  for (let v = Math.ceil(min / step) * step; v <= max + step * 1e-9; v += step) ticks.push(v);
  return ticks;
}

// Wykres liniowy z jedną albo kilkoma seriami: [{ name, color (nazwa tokenu CSS), points: [[x, y], …] }].
function drawChart(canvas, series, { xLabel, yLabel, xFormat = v => String(v), integerX = false }) {
  const css = getComputedStyle(document.documentElement);
  const color = name => css.getPropertyValue(name).trim();
  const dpr = window.devicePixelRatio || 1;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  canvas.width = width * dpr;
  canvas.height = height * dpr;

  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  ctx.font = '12px ui-monospace, Menlo, monospace';

  const all = series.flatMap(s => s.points);
  if (all.length === 0 || width === 0) return;

  const pad = { left: 64, right: 16, top: 30, bottom: 40 };
  const xs = all.map(p => p[0]);
  const xMin = Math.min(...xs), xMax = Math.max(...xs);
  const yMax = Math.max(...all.map(p => p[1])) * 1.08 || 1;
  const X = v => pad.left + (v - xMin) / ((xMax - xMin) || 1) * (width - pad.left - pad.right);
  const Y = v => height - pad.bottom - v / yMax * (height - pad.top - pad.bottom);
  const compact = new Intl.NumberFormat('pl-PL', { notation: 'compact', maximumFractionDigits: 1 });

  // siatka i opisy osi
  ctx.strokeStyle = color('--chart-grid');
  ctx.fillStyle = color('--muted');
  ctx.lineWidth = 1;
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';
  for (const v of niceTicks(0, yMax)) {
    ctx.beginPath(); ctx.moveTo(pad.left, Y(v)); ctx.lineTo(width - pad.right, Y(v)); ctx.stroke();
    ctx.fillText(compact.format(v), pad.left - 8, Y(v));
  }
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  const xTicks = niceTicks(xMin, xMax, 6).filter(v => !integerX || Number.isInteger(v));
  for (const v of xTicks) ctx.fillText(xFormat(v), X(v), height - pad.bottom + 8);
  ctx.fillText(xLabel, pad.left + (width - pad.left - pad.right) / 2, height - 16);
  ctx.textAlign = 'left';
  ctx.fillText(yLabel, pad.left, 6);

  // legenda w prawym górnym rogu, gdy serii jest kilka
  if (series.length > 1) {
    ctx.textAlign = 'right';
    let right = width - pad.right;
    for (const s of [...series].reverse()) {
      const textWidth = ctx.measureText(s.name).width;
      ctx.fillStyle = color('--ink');
      ctx.fillText(s.name, right, 6);
      ctx.fillStyle = color(s.color);
      ctx.fillRect(right - textWidth - 16, 7, 10, 10);
      right -= textWidth + 32;
    }
  }

  // linie + punkty
  for (const s of series) {
    ctx.strokeStyle = color(s.color);
    ctx.fillStyle = color(s.color);
    ctx.lineWidth = 2;
    ctx.beginPath();
    s.points.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(X(x), Y(y)) : ctx.lineTo(X(x), Y(y))));
    ctx.stroke();
    if (s.points.length <= 60) {
      for (const [x, y] of s.points) {
        ctx.beginPath(); ctx.arc(X(x), Y(y), 3.5, 0, Math.PI * 2); ctx.fill();
      }
    }
  }
}

window.addEventListener('resize', () => {
  if (state.view === 'compare') renderComparison();
  else if (state.tab === 'steps') drawSteps();
});

// ---------- seria uruchomień (Lab 6) ----------

$('series-form').addEventListener('submit', async event => {
  event.preventDefault();
  setErrors([], 'series-errors');
  $('series-message').textContent = '';

  const algorithms = [...document.querySelectorAll('input[name="series-algorithm"]:checked')].map(input => input.value);
  const sizes = $('series-sizes').value.split(/[\s,;]+/).filter(Boolean).map(Number);
  if (sizes.some(size => !Number.isInteger(size))) {
    setErrors(['Rozmiary podaj jako liczby całkowite, np. 25, 50, 75.'], 'series-errors');
    return;
  }

  const button = $('series-button');
  button.disabled = true;
  try {
    const response = await fetch('/api/series', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        base: readForm(),
        algorithms,
        sizes,
        repeats: Number.parseInt($('series-repeats').value, 10) || 0,
      }),
    });
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      setErrors(Array.isArray(body?.errors) ? body.errors : [`Serwer odrzucił serię (kod ${response.status}).`], 'series-errors');
      return;
    }
    state.compareFilter = body.series;
    $('series-message').textContent =
      `Utworzono ${body.ids.length} plików config.json – obliczenia idą po kolei. Wykresy: „Zestawienie wyników”.`;
    await refreshRunList();
  } catch {
    setErrors(['Brak połączenia z serwerem GUI.'], 'series-errors');
  } finally {
    button.disabled = false;
  }
});

// ---------- widoki: uruchomienie / zestawienie ----------

function selectView(view) {
  state.view = view;
  for (const button of document.querySelectorAll('.view-switch button')) {
    button.setAttribute('aria-selected', String(button.dataset.view === view));
  }
  $('run-area').hidden = view !== 'run';
  $('compare-view').hidden = view !== 'compare';
  if (view === 'compare') {
    loadComparison().catch(error => { $('compare-note').textContent = `Nie udało się wczytać wyników: ${error.message}`; });
  } else if (state.tab === 'steps') {
    drawSteps();
  } else if (state.tab === 'cube') {
    showCube();
  }
}

for (const button of document.querySelectorAll('.view-switch button')) {
  button.addEventListener('click', () => selectView(button.dataset.view));
}

// ---------- zestawienie wyników ----------

const ALGORITHMS = [
  { id: 'CA', name: 'Rozrost CA', color: '--series-ca' },
  { id: 'MC', name: 'Monte Carlo', color: '--series-mc' },
];

// series_20261030_101500 → 30.10.2026 10:15:00
function seriesDate(id) {
  const m = /^series_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})$/.exec(id);
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6]}` : id;
}

async function loadComparison() {
  state.results = await fetchJson('/api/results');
  const seriesIds = [...new Set(state.results.map(r => r.series).filter(Boolean))].sort().reverse();

  const filter = $('compare-filter');
  filter.replaceChildren(
    new Option('Wszystkie uruchomienia', ''),
    ...seriesIds.map(id => new Option(`Seria z ${seriesDate(id)}`, id)));
  // domyślnie najnowsza seria; wybór użytkownika zostaje
  const wanted = state.compareFilter ?? seriesIds[0] ?? '';
  filter.value = [...filter.options].some(o => o.value === wanted) ? wanted : '';
  renderComparison();
}

$('compare-filter').addEventListener('change', event => {
  state.compareFilter = event.target.value;
  renderComparison();
});

function renderComparison() {
  const filter = $('compare-filter').value;
  const rows = state.results.filter(r => !filter || r.series === filter);
  const tbody = document.querySelector('#compare-table tbody');
  tbody.replaceChildren();

  if (rows.length === 0) {
    $('compare-note').textContent = 'Brak zakończonych uruchomień – uruchom serię w panelu po lewej.';
    drawChart($('compare-time'), [], { xLabel: '', yLabel: '' });
    drawChart($('compare-step'), [], { xLabel: '', yLabel: '' });
    return;
  }

  // Każdy punkt to średnia z powtórzeń dla tej samej pary (algorytm, liczba komórek).
  const groups = new Map();
  for (const r of rows) {
    const key = `${r.algorithm}|${r.cells}`;
    const g = groups.get(key) ?? { algorithm: r.algorithm, cells: r.cells, sim: 0, step: 0, n: 0 };
    g.sim += r.simulationMs;
    g.step += r.steps ? r.growthMs / r.steps : 0;
    g.n += 1;
    groups.set(key, g);
  }
  const points = (algorithm, pick) => [...groups.values()]
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

  // Opis parametrów, żeby było wiadomo, co porównujemy.
  const distinct = values => [...new Set(values.filter(v => v !== null && v !== undefined))].join(' / ');
  const ca = rows.filter(r => r.algorithm === 'CA');
  const mc = rows.filter(r => r.algorithm === 'MC');
  const repeats = Math.max(...[...groups.values()].map(g => g.n));
  $('compare-note').textContent = [
    `${rows.length} uruchomień`,
    ca.length ? `CA: zarodki ${distinct(ca.map(r => r.nucleons))}` : null,
    mc.length ? `MC: Q = ${distinct(mc.map(r => r.states))}, kroki MCS ${distinct(mc.map(r => r.mcSteps))}, nowy stan ${distinct(mc.map(r => r.mcProposal === 'Neighbour' ? 'od sąsiada' : 'losowy'))}` : null,
    `sąsiedztwo ${distinct(rows.map(r => r.neighbourhood))}, brzegi ${distinct(rows.map(r => r.boundary))}`,
    repeats > 1 ? `punkt = średnia z ${repeats} powtórzeń` : null,
  ].filter(Boolean).join(' · ');

  const sorted = [...rows].sort((a, b) => a.algorithm.localeCompare(b.algorithm) || a.cells - b.cells || a.id.localeCompare(b.id));
  for (const r of sorted) {
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
    tr.addEventListener('click', () => { selectView('run'); selectRun(r.id); });
    tbody.append(tr);
  }
}

// ---------- start ----------

updateAlgorithmFields();

fetchJson('/api/health')
  .then(health => {
    if (!health.simulatorFound) {
      const banner = $('simulator-warning');
      banner.textContent = `Nie znaleziono symulatora (${health.simulatorDll}). Zbuduj go: dotnet build -c Release w folderze CellularAutomata.`;
      banner.hidden = false;
    }
  })
  .catch(() => {});

// Na start pokazujemy ostatnie udane uruchomienie, jeśli jakieś jest.
const runs = await refreshRunList().catch(() => []);
const latest = runs.find(run => run.status === 'done') ?? runs[0];
if (latest) selectRun(latest.id);
