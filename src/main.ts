// PERSON C owns main.ts. Others: ask before editing.
import * as THREE from "three";
import { engine } from "./lang";
import { createScene } from "./world/scene";
import { initialState, tick } from "./game/state";
import { renderOverlay } from "./ui/overlay";

const params = new URLSearchParams(location.search);
const DEMO = params.has("demo");               // ?demo → slow creature, fixed seed
let world = engine.generate(DEMO ? 1337 : Number(params.get("seed")) || Date.now() % 100000);
const state = initialState();
state.phase = "door1";

const { renderer, scene, camera, pickables, onPick } = createScene();

// Placeholder test cube: proves picking works on laptop AND in the headset (VR gate step 3).
const cube = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.4, 0.4), new THREE.MeshStandardMaterial({ color: 0x990000, emissive: 0x330000 }));
cube.position.set(0, 1.5, 0);
scene.add(cube);
pickables.push(cube);
onPick.push((o) => { (o as THREE.Mesh).rotation.y += 0.5; console.log("picked", o); });

// Hotkeys for the pitch: N = new world, R = reset
addEventListener("keydown", (e) => {
  if (e.key === "n") world = engine.generate(Math.floor(Math.random() * 100000));
  if (e.key === "r") Object.assign(state, initialState(), { phase: "door1" });
});

const clock = new THREE.Clock();
renderer.setAnimationLoop(() => {
  const dt = clock.getDelta();
  tick(state, dt, DEMO);
  cube.rotation.x += dt * 0.3;
  renderOverlay(world, state);
  renderer.render(scene, camera);
});
