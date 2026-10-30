import { $ } from './dom.js';
import { getHealth } from './api.js';
import { initForms, fillForm } from './forms.js';
import { initRunList, refreshRunList, markSelected } from './runList.js';
import { initRunView, openRun, currentConfig, redraw } from './runView.js';
import { initCompare, loadComparison, renderComparison, preferSeries } from './compare.js';

// Punkt startowy strony: łączy moduły i przełącza widoki „Uruchomienie” / „Zestawienie wyników”.
let view = 'run';

function selectView(name) {
  view = name;
  for (const button of document.querySelectorAll('.view-switch button')) {
    button.setAttribute('aria-selected', String(button.dataset.view === name));
  }
  $('run-area').hidden = name !== 'run';
  $('compare-view').hidden = name !== 'compare';
  if (name === 'compare') loadComparison();
  else redraw();
}

async function showRun(id) {
  if (view !== 'run') selectView('run');
  markSelected(id);
  await openRun(id);
}

initForms({
  onRunCreated: async id => {
    await refreshRunList();
    showRun(id);
  },
  onSeriesCreated: async series => {
    preferSeries(series);
    await refreshRunList();
  },
});
initRunList({
  onSelect: showRun,
  onIdle: () => { if (view === 'compare') loadComparison(); },
});
initRunView({ onFinished: () => refreshRunList().catch(() => {}) });
initCompare({ onOpenRun: showRun });

for (const button of document.querySelectorAll('.view-switch button')) {
  button.addEventListener('click', () => selectView(button.dataset.view));
}
$('reuse-button').addEventListener('click', () => {
  const config = currentConfig();
  if (config) fillForm(config);
});
window.addEventListener('resize', () => (view === 'compare' ? renderComparison() : redraw()));

getHealth()
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
if (latest) showRun(latest.id);
