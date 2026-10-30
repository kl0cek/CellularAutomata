import { Volume, sliceShape, sliceCell, sliceRGBA } from './volume.js';

const $ = id => document.getElementById(id);

const STATUS_LABELS = { running: 'Trwa', done: 'Gotowe', failed: 'Błąd', unknown: 'Przerwane' };
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

function setErrors(errors) {
  const list = $('form-errors');
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
      run.grainsEnd ? `ziaren: ${number(run.grainsEnd, 0)}` : null,
      run.simulationMs ? formatMs(run.simulationMs) : null,
    ].filter(Boolean).join(' · ');

    button.append(title, badge, meta);
    button.addEventListener('click', () => selectRun(run.id));

    const li = document.createElement('li');
    li.append(button);
    list.append(li);
  }
  return runs;
}

// ---------- wybrane uruchomienie ----------

async function selectRun(id) {
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

  if (run.status === 'running') {
    showMessage('Symulacja trwa – wyniki pojawią się po jej zakończeniu.');
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

  drawLineChart(canvas, points, {
    xLabel: isMc ? 'krok MCS' : 'krok CA',
    yLabel: isMc ? 'energia (pary sąsiadów na granicy)' : 'puste komórki',
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

function drawLineChart(canvas, points, { xLabel, yLabel }) {
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

  const pad = { left: 76, right: 16, top: 26, bottom: 40 };
  const xs = points.map(p => p[0]);
  const ys = points.map(p => p[1]);
  const xMin = Math.min(...xs), xMax = Math.max(...xs);
  const yMin = 0, yMax = Math.max(...ys) * 1.05 || 1;
  const X = v => pad.left + (v - xMin) / ((xMax - xMin) || 1) * (width - pad.left - pad.right);
  const Y = v => height - pad.bottom - (v - yMin) / (yMax - yMin) * (height - pad.top - pad.bottom);
  const compact = new Intl.NumberFormat('pl-PL', { notation: 'compact', maximumFractionDigits: 1 });

  // siatka i opisy osi
  ctx.strokeStyle = color('--chart-grid');
  ctx.fillStyle = color('--muted');
  ctx.lineWidth = 1;
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';
  for (const v of niceTicks(yMin, yMax)) {
    ctx.beginPath(); ctx.moveTo(pad.left, Y(v)); ctx.lineTo(width - pad.right, Y(v)); ctx.stroke();
    ctx.fillText(compact.format(v), pad.left - 8, Y(v));
  }
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  for (const v of niceTicks(xMin, xMax, Math.min(8, xMax - xMin || 1)).filter(Number.isInteger)) {
    ctx.fillText(String(v), X(v), height - pad.bottom + 8);
  }
  ctx.fillText(xLabel, pad.left + (width - pad.left - pad.right) / 2, height - 16);
  ctx.textAlign = 'left';
  ctx.fillText(yLabel, pad.left, 6);

  // linia + punkty
  ctx.strokeStyle = color('--chart-line');
  ctx.fillStyle = color('--chart-line');
  ctx.lineWidth = 2;
  ctx.beginPath();
  points.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(X(x), Y(y)) : ctx.lineTo(X(x), Y(y))));
  ctx.stroke();
  if (points.length <= 60) {
    for (const [x, y] of points) {
      ctx.beginPath(); ctx.arc(X(x), Y(y), 3, 0, Math.PI * 2); ctx.fill();
    }
  }
}

window.addEventListener('resize', () => { if (state.tab === 'steps') drawSteps(); });

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
