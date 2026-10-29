/**
 * Every input the game reads, both ways: a mouse and keyboard on a laptop, controllers or hands in VR.
 * Use only these calls for input:
 * - onSelect(entity, fn): pointing at the entity and pressing (a click on a laptop, the trigger or a pinch in VR).
 * - onTouch(entity, fn): touching it with a controller or hand in VR (a click on a laptop, which cannot touch).
 * - moveAxis(): which way to move, { x: right, y: forward }, from W A S D or the arrows, or the left thumbstick.
 * - pressed('jump') and pressed('action'): true in the frame the button goes down (Space and E, or A and B).
 * - mode(): 'laptop' or 'vr'.
 * On a laptop, dragging on empty space turns the view.
 */
import {
  createSystem,
  type Entity,
  Hovered,
  InputActions,
  InputComponent,
  Pressed,
  RayInteractable,
  VisibilityState,
  type World,
} from '@iwsdk/core';
import { Vector3 } from 'three';
import {
  BUTTONS,
  type ButtonName,
  Counts,
  Edge,
  headsetSource,
  isDrag,
  look,
  type PlaySource,
  within,
  yawPitchOf,
} from './play-input-core.js';

type Handler = (from: PlaySource) => void;

interface Target {
  readonly edge: Edge;
  readonly handlers: Set<Handler>;
}

interface TouchTarget {
  readonly radius: number;
  readonly edges: { left: Edge; right: Edge };
  readonly handlers: Set<Handler>;
}

const ACTION = 'play.action';
const selects = new Map<Entity, Target>();
const touches = new Map<Entity, TouchTarget>();
const counts = new Counts();
const buttonEdges = { jump: new Edge(), action: new Edge() };
let current: World | null = null;

function world(): World {
  if (current === null) throw new Error('Call setupPlayInput(world) in src/index.ts first.');
  return current;
}

/** 'laptop' outside a headset session, 'vr' inside one. */
export function mode(): 'laptop' | 'vr' {
  return world().visibilityState.value === VisibilityState.NonImmersive ? 'laptop' : 'vr';
}

/** Calls fn each time the entity is pointed at and pressed. Returns a function that stops it. */
export function onSelect(entity: Entity, fn: Handler): () => void {
  if (!entity.hasComponent(RayInteractable)) entity.addComponent(RayInteractable);
  const target = selects.get(entity) ?? { edge: new Edge(), handlers: new Set<Handler>() };
  selects.set(entity, target);
  target.handlers.add(fn);
  return () => target.handlers.delete(fn);
}

/** Calls fn each time a controller or hand comes within radius metres (in VR), or the entity is clicked (laptop). */
export function onTouch(entity: Entity, fn: Handler, radius = 0.12): () => void {
  const stopClick = onSelect(entity, (from) => {
    if (from === 'mouse') fn(from);
  });
  const target = touches.get(entity) ?? {
    radius,
    edges: { left: new Edge(), right: new Edge() },
    handlers: new Set<Handler>(),
  };
  touches.set(entity, target);
  target.handlers.add(fn);
  return () => {
    stopClick();
    target.handlers.delete(fn);
  };
}

/** Which way to move: x to the right, y forward, each from -1 to 1. */
export function moveAxis(): { x: number; y: number } {
  const axis = world().input.actions.getAxis2D(InputActions.LocomotionMove);
  return { x: axis.x, y: -axis.y };
}

/** True in the frame the button goes down. */
export function pressed(name: ButtonName): boolean {
  return world().input.actions.getButtonDown(name === 'jump' ? InputActions.LocomotionJump : ACTION);
}

const here = new Vector3();
const hand = new Vector3();
const forward = new Vector3();

function holdingController(w: World): boolean {
  return w.input.xr.gamepads.left !== undefined || w.input.xr.gamepads.right !== undefined;
}

class PlayInputSystem extends createSystem({}) {
  update(): void {
    const w = this.world;
    const laptop = mode() === 'laptop';
    for (const [entity, target] of selects) {
      if (!target.edge.rise(entity.hasComponent(Pressed))) continue;
      const from = laptop ? 'mouse' : headsetSource(holdingController(w));
      counts.add(from);
      for (const fn of target.handlers) fn(from);
    }
    if (!laptop) {
      for (const [entity, target] of touches) {
        if (entity.object3D === undefined) continue;
        entity.object3D.getWorldPosition(here);
        for (const side of ['left', 'right'] as const) {
          const spaces = [w.playerSpaceEntities.gripSpaces[side], w.playerSpaceEntities.indexTipSpaces[side]];
          const near = spaces.some((space) => {
            if (space.object3D === undefined) return false;
            space.object3D.getWorldPosition(hand);
            return within(here, hand, target.radius);
          });
          if (!target.edges[side].rise(near)) continue;
          counts.add('touch');
          for (const fn of target.handlers) fn('touch');
        }
      }
    }
    for (const name of Object.keys(BUTTONS) as ButtonName[]) {
      if (buttonEdges[name].rise(pressed(name))) counts.add(laptop ? 'key' : 'trigger');
    }
  }
}

/** Dragging on empty space turns the view on a laptop; a press on something the game listens to is a click. */
function dragToLook(w: World): void {
  const canvas = w.renderer.domElement;
  let drag: { x: number; y: number; yaw: number; pitch: number; moved: boolean } | null = null;
  canvas.addEventListener('pointerdown', (event) => {
    if (event.button !== 0 || mode() !== 'laptop') return;
    if ([...selects.keys()].some((entity) => entity.hasComponent(Hovered))) return;
    w.camera.getWorldDirection(forward);
    drag = { x: event.clientX, y: event.clientY, ...yawPitchOf(forward), moved: false };
  });
  canvas.addEventListener('pointermove', (event) => {
    if (drag === null) return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (!drag.moved && !isDrag(dx, dy)) return;
    drag.moved = true;
    const view = look(drag.yaw, drag.pitch, dx, dy);
    w.camera.rotation.set(view.pitch, view.yaw, 0, 'YXZ');
  });
  const end = () => {
    drag = null;
  };
  canvas.addEventListener('pointerup', end);
  canvas.addEventListener('pointercancel', end);
}

/** Screen points (CSS pixels on the canvas) of what the game listens to, for the computer's check of the app. */
function targets(w: World): { x: number; y: number }[] {
  const rect = w.renderer.domElement.getBoundingClientRect();
  const points: { x: number; y: number }[] = [];
  for (const entity of selects.keys()) {
    if (entity.object3D === undefined || !entity.object3D.visible) continue;
    entity.object3D.getWorldPosition(here);
    here.project(w.camera);
    if (here.z < -1 || here.z > 1 || Math.abs(here.x) > 1 || Math.abs(here.y) > 1) continue;
    points.push({ x: rect.left + ((here.x + 1) / 2) * rect.width, y: rect.top + ((1 - here.y) / 2) * rect.height });
  }
  return points;
}

/** Call once, in src/index.ts, before registering the game's systems. */
export function setupPlayInput(w: World): void {
  current = w;
  w.input.actions.enableDefaultBindings('browser-first-person');
  w.input.actions.addBindings([
    { source: 'keyboard', action: ACTION, kind: 'button', code: BUTTONS.action.key },
    { source: 'xrGamepad', action: ACTION, kind: 'button', handedness: 'right', button: InputComponent.B_Button },
  ]);
  w.registerSystem(PlayInputSystem);
  dragToLook(w);
  // Only when the person's computer checks the app (it adds ?check): what reached the game, and where to click.
  if (new URLSearchParams(location.search).has('check')) {
    Object.assign(globalThis, {
      __playCheck: { counts: () => counts.snapshot(), mode, targets: () => targets(w) },
    });
  }
}
