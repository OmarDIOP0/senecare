import React from "react";
import { Easing, Img, interpolate, staticFile } from "remotion";
import type { PhoneStep } from "../../episodes/types";
import { K, PhoneFrame, SCREEN_CSS, toVideo } from "./PhoneFrame";
import { TapIndicator, type Tap } from "./TapIndicator";

export type Box = { x: number; y: number; w: number; h: number };
export type Screens = Record<string, { file: string; boxes: Record<string, Box> }>;

const ease = Easing.bezier(0.45, 0, 0.2, 1);
const FOCUS = { x: 540, y: 720 };

type Cam = { scale: number; px: number; py: number; pull: number };

/** Box of a target on the screen shown at that moment (the last screen set at or before the step). */
function boxAt(steps: PhoneStep[], index: number, target: string, screens: Screens): Box | null {
  for (let i = index; i >= 0; i--) {
    const name = steps[i].screen;
    if (name && screens[name]?.boxes[target]) return screens[name].boxes[target];
  }
  for (const s of Object.values(screens)) if (s.boxes[target]) return s.boxes[target];
  return null;
}

/** Camera on the phone: zooms toward a target and back, eased, back to rest at the end of the scene. */
function camera(steps: PhoneStep[], screens: Screens, duration: number, time: number): Cam {
  const rest: Cam = { scale: 1, px: FOCUS.x, py: FOCUS.y, pull: 0 };
  const keys: { t: number; cam: Cam }[] = [{ t: 0, cam: rest }];
  steps.forEach((step, i) => {
    if (step.zoom === undefined) return;
    if (step.zoom === null) {
      keys.push({ t: step.at * duration, cam: { ...keys[keys.length - 1].cam, scale: 1, pull: 0 } });
      return;
    }
    const box = boxAt(steps, i, step.zoom.target, screens);
    if (!box) return;
    const p = toVideo(box.x + box.w / 2, box.y + box.h / 2);
    keys.push({ t: step.at * duration, cam: { scale: step.zoom.scale, px: p.x, py: p.y, pull: 0.5 } });
  });
  keys.push({ t: Math.max(duration - 0.55, keys[keys.length - 1].t + 0.1), cam: { ...keys[keys.length - 1].cam, scale: 1, pull: 0 } });

  let current = rest;
  for (let i = 1; i < keys.length; i++) {
    const { t, cam } = keys[i];
    if (time < t) break;
    const p = ease(Math.min(1, (time - t) / 0.85));
    const from = current;
    current = {
      scale: from.scale + (cam.scale - from.scale) * p,
      px: from.px + (cam.px - from.px) * p,
      py: from.py + (cam.py - from.py) * p,
      pull: from.pull + (cam.pull - from.pull) * p,
    };
  }
  return current;
}

export function tapsOf(steps: PhoneStep[], screens: Screens, duration: number): Tap[] {
  return steps.flatMap((step, i) => {
    if (!step.tap) return [];
    const box = boxAt(steps, i, step.tap, screens);
    return box ? [{ t: step.at * duration, x: box.x + box.w / 2, y: box.y + box.h / 2 }] : [];
  });
}

/** The phone showing the real app screens of a scene, with camera moves and taps. */
export const PhoneStage: React.FC<{
  steps: PhoneStep[];
  screens: Screens;
  duration: number;
  time: number;
  /** Extra transform on the whole phone (wow sequences push it back). */
  style?: React.CSSProperties;
  dim?: number;
  /** Drawn over the screen, in app-screen CSS px (follows the camera). */
  screenOverlay?: React.ReactNode;
}> = ({ steps, screens, duration, time, style, dim = 0, screenOverlay }) => {
  const shown = steps.filter((s) => s.screen).map((s) => ({ name: s.screen as string, t: s.at * duration }));
  const currentIndex = Math.max(0, shown.filter((s) => s.t <= time).length - 1);
  const current = shown[currentIndex];
  const previous = currentIndex > 0 ? shown[currentIndex - 1] : null;
  const fade = previous ? interpolate(time, [current.t, current.t + 0.28], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" }) : 1;

  const cam = camera(steps, screens, duration, time);
  const qx = cam.px + (FOCUS.x - cam.px) * cam.pull;
  const qy = cam.py + (FOCUS.y - cam.py) * cam.pull;
  const tx = qx - cam.scale * cam.px;
  const ty = qy - cam.scale * cam.py;

  const layer = (name: string, opacity: number) => (
    <Img key={name} src={staticFile(screens[name].file)} style={{ position: "absolute", inset: 0, width: "100%", height: "100%", opacity }} />
  );

  return (
    <div style={{ position: "absolute", inset: 0, transformOrigin: "0 0", transform: `translate(${tx}px, ${ty}px) scale(${cam.scale})` }}>
      <div style={{ position: "absolute", inset: 0, ...style }}>
        <PhoneFrame>
          {previous && fade < 1 && layer(previous.name, 1)}
          {layer(current.name, fade)}
          <div style={{ position: "absolute", left: 0, top: 0, width: SCREEN_CSS.width, height: SCREEN_CSS.height, transformOrigin: "0 0", transform: `scale(${K})` }}>
            <TapIndicator taps={tapsOf(steps, screens, duration)} time={time} />
            {screenOverlay}
          </div>
          {dim > 0 && <div style={{ position: "absolute", inset: 0, background: `rgba(10,31,42,${dim})`, zIndex: 25 }} />}
        </PhoneFrame>
      </div>
    </div>
  );
};
