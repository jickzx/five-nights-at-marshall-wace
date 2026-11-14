// PERSON B owns src/world. This is a minimal VR-ready starter:
// one renderer, WebXR enabled, a pointer ray that works for mouse AND controller.
import * as THREE from "three";
import { VRButton } from "three/examples/jsm/webxr/VRButton.js";

export function createScene() {
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.setSize(innerWidth, innerHeight);
  renderer.xr.enabled = true;
  document.body.appendChild(renderer.domElement);
  document.body.appendChild(VRButton.createButton(renderer));

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x080000);
  scene.fog = new THREE.Fog(0x200000, 2, 18);

  const camera = new THREE.PerspectiveCamera(70, innerWidth / innerHeight, 0.05, 100);
  camera.position.set(0, 1.6, 3);

  scene.add(new THREE.HemisphereLight(0xff6666, 0x110000, 0.6));

  const floor = new THREE.Mesh(new THREE.PlaneGeometry(6, 40), new THREE.MeshStandardMaterial({ color: 0x1a0505 }));
  floor.rotation.x = -Math.PI / 2;
  scene.add(floor);

  // Pickable objects: anything in this array can be selected by mouse or controller.
  const pickables: THREE.Object3D[] = [];
  const raycaster = new THREE.Raycaster();
  const onPick: ((o: THREE.Object3D) => void)[] = [];

  // Mouse
  addEventListener("pointerdown", (e) => {
    const ndc = new THREE.Vector2((e.clientX / innerWidth) * 2 - 1, -(e.clientY / innerHeight) * 2 + 1);
    raycaster.setFromCamera(ndc, camera);
    const hit = raycaster.intersectObjects(pickables, true)[0];
    if (hit) onPick.forEach((f) => f(hit.object));
  });

  // VR controllers (same code path)
  for (let i = 0; i < 2; i++) {
    const c = renderer.xr.getController(i);
    c.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, -5)]),
      new THREE.LineBasicMaterial({ color: 0xff0000 })));
    c.addEventListener("select", () => {
      const m = new THREE.Matrix4().extractRotation(c.matrixWorld);
      raycaster.ray.origin.setFromMatrixPosition(c.matrixWorld);
      raycaster.ray.direction.set(0, 0, -1).applyMatrix4(m);
      const hit = raycaster.intersectObjects(pickables, true)[0];
      if (hit) onPick.forEach((f) => f(hit.object));
    });
    scene.add(c);
  }

  addEventListener("resize", () => {
    camera.aspect = innerWidth / innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(innerWidth, innerHeight);
  });

  return { renderer, scene, camera, pickables, onPick };
}
