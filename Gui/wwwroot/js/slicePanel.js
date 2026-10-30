import { $ } from './dom.js';
import { sliceShape, sliceCell, sliceRGBA } from './volume.js';

// Zakładka „Przekrój 2D”: wybór płaszczyzny i warstwy, podgląd komórki pod kursorem.
const AXIS_OF_LAYER = { XY: 'z', XZ: 'y', YZ: 'x' };
let volume = null;

export function initSlicePanel() {
  $('plane').addEventListener('change', () => {
    if (!volume) return;
    setupLayerSlider();
    draw();
  });
  $('layer').addEventListener('input', draw);
  $('slice-canvas').addEventListener('mousemove', showHoveredCell);
}

export function showVolume(vol) {
  volume = vol;
  setupLayerSlider();
  draw();
}

// Suwak warstwy: od 0 do liczby warstw − 1, na start środkowa warstwa.
function setupLayerSlider() {
  const { layers } = sliceShape(volume, $('plane').value);
  const layer = $('layer');
  layer.max = String(layers - 1);
  layer.value = String(Math.floor(layers / 2));
  layer.disabled = layers === 1;
}

function draw() {
  if (!volume) return;
  const plane = $('plane').value;
  const layer = Number($('layer').value);
  const { width, height, rgba } = sliceRGBA(volume, plane, layer);

  const canvas = $('slice-canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.getContext('2d').putImageData(new ImageData(rgba, width, height), 0, 0);
  $('layer-label').textContent = `${AXIS_OF_LAYER[plane]} = ${layer} z ${sliceShape(volume, plane).layers - 1}`;
}

function showHoveredCell(event) {
  if (!volume) return;
  const canvas = event.currentTarget;
  const rect = canvas.getBoundingClientRect();
  const col = Math.min(canvas.width - 1, Math.max(0, Math.floor((event.clientX - rect.left) / rect.width * canvas.width)));
  const row = Math.min(canvas.height - 1, Math.max(0, Math.floor((event.clientY - rect.top) / rect.height * canvas.height)));
  const [x, y, z] = sliceCell(volume, $('plane').value, Number($('layer').value), col, row);
  $('slice-hover').textContent = `x = ${x}, y = ${y}, z = ${z} · ziarno ${volume.at(x, y, z)}`;
}
