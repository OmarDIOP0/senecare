import React from "react";
import { Easing, interpolate } from "remotion";

export type Tap = { t: number; x: number; y: number };

const ease = Easing.bezier(0.45, 0, 0.2, 1);

/**
 * A fingertip (in app-screen CSS px, inside the scaled screen layer): glides to each target, presses, and leaves a
 * ripple. Coming from below the screen for the first tap.
 */
export const TapIndicator: React.FC<{ taps: Tap[]; time: number }> = ({ taps, time }) => {
  if (taps.length === 0) return null;
  const travel = 0.55;
  const index = taps.findIndex((tap) => time < tap.t + 0.9);
  if (index < 0) return null;
  const tap = taps[index];
  const from = index === 0 ? { x: tap.x + 60, y: 900 } : taps[index - 1];
  const start = tap.t - travel - 0.15;
  if (time < start) return null;

  const move = ease(Math.min(1, Math.max(0, (time - start) / travel)));
  const x = from.x + (tap.x - from.x) * move;
  const y = from.y + (tap.y - from.y) * move;
  const press = interpolate(time, [tap.t - 0.08, tap.t, tap.t + 0.14], [1, 0.82, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const appear = interpolate(time, [start, start + 0.2, tap.t + 0.6, tap.t + 0.9], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const ripple = interpolate(time, [tap.t, tap.t + 0.6], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });

  return (
    <div style={{ position: "absolute", left: 0, top: 0, width: "100%", height: "100%", pointerEvents: "none", zIndex: 30 }}>
      {time >= tap.t && ripple < 1 && (
        <div
          style={{
            position: "absolute",
            left: tap.x - 12 - ripple * 34,
            top: tap.y - 12 - ripple * 34,
            width: 24 + ripple * 68,
            height: 24 + ripple * 68,
            borderRadius: "50%",
            border: "2.5px solid rgba(242,181,68,0.95)",
            opacity: 1 - ripple,
          }}
        />
      )}
      <div
        style={{
          position: "absolute",
          left: x - 17,
          top: y - 17,
          width: 34,
          height: 34,
          borderRadius: "50%",
          opacity: appear,
          transform: `scale(${press})`,
          background: "radial-gradient(circle at 40% 35%, rgba(255,255,255,0.95), rgba(255,255,255,0.65) 60%, rgba(255,255,255,0.35))",
          border: "2px solid rgba(19,34,43,0.35)",
          boxShadow: "0 6px 18px rgba(19,34,43,0.35)",
        }}
      />
    </div>
  );
};
