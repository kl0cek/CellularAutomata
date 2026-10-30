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
// Rysuje w rozmiarze, jaki canvas ma na stronie – ukryty canvas (szerokość 0) zostaje pusty.
export function drawChart(canvas, series, { xLabel, yLabel, xFormat = v => String(v), integerX = false }) {
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
