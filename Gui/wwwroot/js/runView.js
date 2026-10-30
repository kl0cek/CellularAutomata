import { $ } from './dom.js';
import { getRun, getRunData } from './api.js';
import { STATUS_LABELS, PENDING, number, formatMs, runDate } from './format.js';
import { Volume } from './volume.js';
import { initSlicePanel, showVolume } from './slicePanel.js';
import { drawSteps } from './stepsPanel.js';

// Widok wybranego uruchomienia: nagłówek, podsumowanie i zakładki 2D / 3D / przebieg / konsola.
// Dopóki uruchomienie czeka albo się liczy, stan jest odpytywany co 0,7 s.
const TABS = ['slice', 'cube', 'steps', 'log'];

const state = {
  id: null,
  token: 0,           // chroni przed spóźnionymi odpowiedziami po przełączeniu uruchomienia
  poll: null,
  loadedFor: null,
  volume: null,
  stepsText: '',
  config: null,
  tab: 'slice',
  cube: null,
  cubeShownFor: null,
};
let onRunFinished = () => {};

// onFinished() – uruchomienie właśnie przestało czekać / się liczyć (np. żeby odświeżyć listę).
export function initRunView({ onFinished }) {
  onRunFinished = onFinished;
  for (const button of document.querySelectorAll('.tabs button')) {
    button.addEventListener('click', () => selectTab(button.dataset.tab));
  }
  $('reset-view').addEventListener('click', () => state.cube?.resetView());
  initSlicePanel();
}

// config.json wybranego uruchomienia (dla przycisku „Użyj tych parametrów”).
export const currentConfig = () => state.config;

export async function openRun(id) {
  Object.assign(state, { id, loadedFor: null, volume: null, stepsText: '' });
  const token = ++state.token;
  clearInterval(state.poll);
  state.poll = null;

  $('empty-state').hidden = true;
  $('run-view').hidden = false;
  showMessage('Wczytywanie…');
  try {
    await refresh(id, token);
  } catch (error) {
    if (token === state.token) showMessage(`Nie udało się wczytać uruchomienia: ${error.message}`);
  }
}

// Ponowne narysowanie widocznej zakładki (po pokazaniu widoku albo zmianie rozmiaru okna).
export function redraw() {
  if (state.tab === 'steps') drawSteps(state.stepsText);
  if (state.tab === 'cube') showCube();
}

async function refresh(id, token) {
  const run = await getRun(id);
  if (token !== state.token) return;

  state.config = run.config;
  renderHeader(run);
  renderSummary(run);
  $('log').textContent = run.log || '(brak wyjścia programu)';

  if (PENDING.has(run.status)) {
    showMessage(run.status === 'queued'
      ? 'W kolejce – obliczenia ruszą po zakończeniu wcześniejszych zleceń.'
      : 'Symulacja trwa – wyniki pojawią się po jej zakończeniu.');
    state.poll ??= setInterval(() => refresh(id, token).catch(() => {}), 700);
    return;
  }

  clearInterval(state.poll);
  state.poll = null;
  onRunFinished();

  if (run.status === 'done') {
    await loadResults(id, token);
  } else {
    showMessage('Symulacja nie zakończyła się poprawnie – szczegóły w zakładce Konsola.');
    selectTab('log');
  }
}

async function loadResults(id, token) {
  if (state.loadedFor === id) return; // np. dwa nakładające się odpytania
  state.loadedFor = id;
  showMessage('Wczytywanie mikrostruktury…');

  const { micro, stepsText } = await getRunData(id);
  if (token !== state.token) return;
  if (!micro) {
    showMessage('Brak pliku z mikrostrukturą dla tego uruchomienia.');
    return;
  }

  state.volume = new Volume(micro);
  state.stepsText = stepsText;
  showMessage('');
  showVolume(state.volume);
  redraw();
}

function selectTab(name) {
  state.tab = name;
  for (const button of document.querySelectorAll('.tabs button')) {
    button.setAttribute('aria-selected', String(button.dataset.tab === name));
  }
  for (const tab of TABS) $(`tab-${tab}`).hidden = tab !== name;
  redraw();
}

async function showCube() {
  if (!state.volume) return;
  try {
    if (!state.cube) {
      const { CubeView } = await import('./view3d.js'); // three.js ładujemy dopiero przy pierwszym użyciu
      state.cube = new CubeView($('cube-container'));
    }
    if (state.cubeShownFor !== state.id) {
      state.cube.show(state.volume);
      state.cubeShownFor = state.id;
    }
  } catch (error) {
    showMessage(`Nie udało się uruchomić widoku 3D (WebGL): ${error.message}`);
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

function showMessage(text) {
  $('view-message').textContent = text;
}
