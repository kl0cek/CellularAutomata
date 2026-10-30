import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { FACES, faceRGBA } from './volume.js';

// Jasność każdej ściany (kolejność jak FACES). Bez oświetlenia kolory zostają dokładnie takie jak w 2D,
// a różna jasność ścian daje wrażenie bryły.
const SHADE = [0.82, 0.7, 1.0, 0.6, 0.92, 0.66];

// Kostka 3D: rzut ścian zewnętrznych mikrostruktury, obracana myszką.
export class CubeView {
  constructor(container) {
    this.container = container;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(window.devicePixelRatio);
    container.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(35, 1, 0.01, 100);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.cube = null;

    new ResizeObserver(() => this.resize()).observe(container);
    this.resetView();

    const loop = () => {
      this.controls.update();
      this.renderer.render(this.scene, this.camera);
      requestAnimationFrame(loop);
    };
    loop();
  }

  resize() {
    const width = this.container.clientWidth;
    const height = this.container.clientHeight;
    if (width === 0 || height === 0) return; // zakładka ukryta
    this.renderer.setSize(width, height);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
  }

  resetView() {
    this.camera.position.set(1.55, 1.25, 1.95);
    this.controls.target.set(0, 0, 0);
    this.controls.update();
  }

  show(vol) {
    this.clear();

    // Najdłuższy bok kostki ma długość 1, proporcje jak w siatce.
    const scale = 1 / Math.max(vol.sizeX, vol.sizeY, vol.sizeZ);
    const geometry = new THREE.BoxGeometry(vol.sizeX * scale, vol.sizeY * scale, vol.sizeZ * scale);

    const materials = FACES.map((face, k) => {
      const { width, height, data } = faceRGBA(vol, face);
      const texture = new THREE.DataTexture(data, width, height, THREE.RGBAFormat);
      texture.magFilter = THREE.NearestFilter; // ostre granice komórek
      texture.minFilter = THREE.NearestFilter;
      texture.generateMipmaps = false;
      texture.colorSpace = THREE.SRGBColorSpace;
      texture.needsUpdate = true;
      return new THREE.MeshBasicMaterial({ map: texture, color: new THREE.Color().setScalar(SHADE[k]) });
    });

    this.cube = new THREE.Group();
    this.cube.add(new THREE.Mesh(geometry, materials));
    this.cube.add(new THREE.LineSegments(new THREE.EdgesGeometry(geometry),
      new THREE.LineBasicMaterial({ color: 0x1d2329 })));

    // Osie w narożniku (0, 0, 0): x czerwona, y zielona, z niebieska.
    const axes = new THREE.AxesHelper(0.3);
    axes.position.set(-vol.sizeX * scale / 2, -vol.sizeY * scale / 2, -vol.sizeZ * scale / 2);
    this.cube.add(axes);

    this.scene.add(this.cube);
  }

  clear() {
    if (!this.cube) return;
    this.cube.traverse(object => {
      object.geometry?.dispose();
      const materials = Array.isArray(object.material) ? object.material : [object.material];
      for (const material of materials) {
        material?.map?.dispose();
        material?.dispose();
      }
    });
    this.scene.remove(this.cube);
    this.cube = null;
  }
}
