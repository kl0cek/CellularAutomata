import { $, fillList } from './dom.js';
import { postJson, errorsOf } from './api.js';

// Formularz pojedynczej symulacji i formularz serii (Lab 6).
// onRunCreated(id) i onSeriesCreated(series) wywołuje się po przyjęciu zlecenia przez serwer.
export function initForms({ onRunCreated, onSeriesCreated }) {
  const form = $('run-form');
  form.addEventListener('change', event => {
    if (event.target.name === 'algorithm') updateAlgorithmFields();
  });
  form.addEventListener('submit', event => submitRun(event, onRunCreated));
  $('series-form').addEventListener('submit', event => submitSeries(event, onSeriesCreated));
  updateAlgorithmFields();
}

// Wypełnia formularz parametrami uruchomienia (pola jak w config.json).
export function fillForm(config) {
  const set = (id, value) => { if (value !== undefined && value !== null) $(id).value = value; };
  $('run-form').elements.algorithm.value = config.Algorithm ?? 'CA';
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

function algorithm() {
  return $('run-form').elements.algorithm.value;
}

// Pola CA albo MC – widać tylko te, które dotyczą wybranego algorytmu.
function updateAlgorithmFields() {
  const mc = algorithm() === 'MC';
  $('ca-params').hidden = mc;
  $('mc-params').hidden = !mc;
}

// Parametry w kształcie RunRequest po stronie serwera.
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

async function submitRun(event, onRunCreated) {
  event.preventDefault();
  fillList('form-errors', []);
  const button = $('run-button');
  button.disabled = true;
  button.textContent = 'Uruchamianie…';
  try {
    const result = await postJson('/api/runs', readForm());
    if (!result.ok) {
      fillList('form-errors', errorsOf(result, 'Serwer odrzucił parametry'));
      return;
    }
    await onRunCreated(result.body.id);
  } catch {
    fillList('form-errors', ['Brak połączenia z serwerem GUI – czy działa `dotnet run` w folderze Gui?']);
  } finally {
    button.disabled = false;
    button.textContent = 'Uruchom symulację';
  }
}

// Seria: parametry bazowe z formularza symulacji × algorytmy × rozmiary × powtórzenia.
async function submitSeries(event, onSeriesCreated) {
  event.preventDefault();
  fillList('series-errors', []);
  $('series-message').textContent = '';

  const algorithms = [...document.querySelectorAll('input[name="series-algorithm"]:checked')].map(input => input.value);
  const sizes = $('series-sizes').value.split(/[\s,;]+/).filter(Boolean).map(Number);
  if (sizes.some(size => !Number.isInteger(size))) {
    fillList('series-errors', ['Rozmiary podaj jako liczby całkowite, np. 25, 50, 75.']);
    return;
  }

  const button = $('series-button');
  button.disabled = true;
  try {
    const result = await postJson('/api/series', {
      base: readForm(),
      algorithms,
      sizes,
      repeats: Number.parseInt($('series-repeats').value, 10) || 0,
    });
    if (!result.ok) {
      fillList('series-errors', errorsOf(result, 'Serwer odrzucił serię'));
      return;
    }
    $('series-message').textContent =
      `Utworzono ${result.body.ids.length} plików config.json – obliczenia idą po kolei. Wykresy: „Zestawienie wyników”.`;
    await onSeriesCreated(result.body.series);
  } catch {
    fillList('series-errors', ['Brak połączenia z serwerem GUI.']);
  } finally {
    button.disabled = false;
  }
}
