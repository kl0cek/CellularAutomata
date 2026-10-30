import { $ } from './dom.js';
import { listRuns } from './api.js';
import { STATUS_LABELS, PENDING, number, formatMs, runDate } from './format.js';

// Lista uruchomień w panelu bocznym. Dopóki coś czeka w kolejce albo się liczy, odświeża się sama.
let selectedId = null;
let poll = null;
let callbacks = { onSelect: () => {}, onIdle: () => {} };

// onSelect(id) – kliknięcie pozycji; onIdle() – kolejka właśnie się opróżniła.
export function initRunList(options) {
  callbacks = options;
}

export async function refreshRunList() {
  const runs = await listRuns();
  const list = $('run-list');
  list.replaceChildren(...(runs.length > 0 ? runs.map(runItem) : [emptyItem()]));

  const pending = runs.some(run => PENDING.has(run.status));
  if (pending && !poll) {
    poll = setInterval(() => refreshRunList().catch(() => {}), 1500);
  } else if (!pending && poll) {
    clearInterval(poll);
    poll = null;
    callbacks.onIdle();
  }
  return runs;
}

// Podświetla wybrane uruchomienie (bez ponownego pobierania listy).
export function markSelected(id) {
  selectedId = id;
  for (const button of document.querySelectorAll('#run-list button')) {
    if (button.dataset.id === id) button.setAttribute('aria-current', 'true');
    else button.removeAttribute('aria-current');
  }
}

function runItem(run) {
  const button = document.createElement('button');
  button.type = 'button';
  button.dataset.id = run.id;
  if (run.id === selectedId) button.setAttribute('aria-current', 'true');

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
  button.addEventListener('click', () => callbacks.onSelect(run.id));

  const li = document.createElement('li');
  li.append(button);
  return li;
}

function emptyItem() {
  const li = document.createElement('li');
  li.className = 'empty-note';
  li.textContent = 'Jeszcze nic nie uruchomiono.';
  return li;
}
