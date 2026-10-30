// Mikrostruktura z /api/runs/{id}/micro: 3 × int32 (X, Y, Z), potem ID komórek, indeks = x + X·(y + Y·z).
export class Volume {
  constructor(buffer) {
    const data = new Int32Array(buffer);
    [this.sizeX, this.sizeY, this.sizeZ] = data;
    this.cells = data.subarray(3);
    if (this.cells.length !== this.sizeX * this.sizeY * this.sizeZ) {
      throw new Error('Niepoprawne dane mikrostruktury.');
    }
  }

  at(x, y, z) {
    return this.cells[x + this.sizeX * (y + this.sizeY * z)];
  }
}

// Stały, jasny kolor z ID ziarna – ten sam w 2D i 3D. Puste komórki (0) są czarne.
export function grainColor(id) {
  if (id === 0) return [0, 0, 0];
  const h = Math.imul(id, 2654435761) >>> 0;
  return [60 + ((h >>> 24) % 196), 60 + ((h >>> 12) % 196), 60 + (h % 196)];
}

// Przekroje 2D: XY (z = const), XZ (y = const), YZ (x = const).
export function sliceShape(vol, plane) {
  switch (plane) {
    case 'XY': return { width: vol.sizeX, height: vol.sizeY, layers: vol.sizeZ };
    case 'XZ': return { width: vol.sizeX, height: vol.sizeZ, layers: vol.sizeY };
    case 'YZ': return { width: vol.sizeY, height: vol.sizeZ, layers: vol.sizeX };
    default: throw new Error(`Nieznana płaszczyzna ${plane}`);
  }
}

// Komórka (x, y, z) pod pikselem (col, row) przekroju. Wiersz 0 to góra obrazka,
// więc oś pionowa (y albo z) rośnie do góry – tak samo jak na kostce 3D.
export function sliceCell(vol, plane, layer, col, row) {
  const up = sliceShape(vol, plane).height - 1 - row;
  switch (plane) {
    case 'XY': return [col, up, layer];
    case 'XZ': return [col, layer, up];
    case 'YZ': return [layer, col, up];
    default: throw new Error(`Nieznana płaszczyzna ${plane}`);
  }
}

// Piksele RGBA przekroju, gotowe do ImageData.
export function sliceRGBA(vol, plane, layer) {
  const { width, height } = sliceShape(vol, plane);
  const rgba = new Uint8ClampedArray(width * height * 4);
  for (let row = 0; row < height; row++) {
    for (let col = 0; col < width; col++) {
      const [x, y, z] = sliceCell(vol, plane, layer, col, row);
      const [r, g, b] = grainColor(vol.at(x, y, z));
      const o = (row * width + col) * 4;
      rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
    }
  }
  return { width, height, rgba };
}

// Ściany zewnętrzne kostki w kolejności materiałów BoxGeometry z three.js: +x, −x, +y, −y, +z, −z.
// Dla każdej: wymiary tekstury i komórka pod tekselem (i, j), gdzie j = 0 to dół tekstury.
// Mapowanie dopasowane do współrzędnych tekstur, które BoxGeometry nadaje każdej ścianie.
export const FACES = [
  { name: '+x', size: v => [v.sizeZ, v.sizeY], cell: (v, i, j) => [v.sizeX - 1, j, v.sizeZ - 1 - i] },
  { name: '-x', size: v => [v.sizeZ, v.sizeY], cell: (v, i, j) => [0, j, i] },
  { name: '+y', size: v => [v.sizeX, v.sizeZ], cell: (v, i, j) => [i, v.sizeY - 1, v.sizeZ - 1 - j] },
  { name: '-y', size: v => [v.sizeX, v.sizeZ], cell: (v, i, j) => [i, 0, j] },
  { name: '+z', size: v => [v.sizeX, v.sizeY], cell: (v, i, j) => [i, j, v.sizeZ - 1] },
  { name: '-z', size: v => [v.sizeX, v.sizeY], cell: (v, i, j) => [v.sizeX - 1 - i, j, 0] },
];

// Tekstura RGBA jednej ściany (wiersz 0 = dół tekstury).
export function faceRGBA(vol, face) {
  const [width, height] = face.size(vol);
  const data = new Uint8Array(width * height * 4);
  for (let j = 0; j < height; j++) {
    for (let i = 0; i < width; i++) {
      const [x, y, z] = face.cell(vol, i, j);
      const [r, g, b] = grainColor(vol.at(x, y, z));
      const o = (j * width + i) * 4;
      data[o] = r; data[o + 1] = g; data[o + 2] = b; data[o + 3] = 255;
    }
  }
  return { width, height, data };
}
