import { $ } from './dom.js';
import { drawChart } from './chart.js';
import { number, formatMs } from './format.js';

// Zakładka „Przebieg”: wykres z StepTimes.txt – energia (MC) albo puste komórki (CA) w kolejnych krokach.
export function drawSteps(stepsText) {
  const canvas = $('steps-chart');
  const note = $('steps-note');
  if (canvas.clientWidth === 0) return; // zakładka ukryta – narysujemy po jej pokazaniu

  const lines = stepsText.trim().split('\n').filter(Boolean);
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
