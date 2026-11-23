import type { Episode, Scene } from "../episodes/types.ts";

export const FPS = 30;
export const WIDTH = 1080;
export const HEIGHT = 1920;
/** The voice starts a little after the scene, and the scene lingers a little after it. */
export const LEAD = 0.25;
export const TAIL = 0.55;

export type TimedScene = Scene & { start: number; duration: number; voiceDuration: number; index: number };

/** Scene starts and lengths (seconds) from the measured voice durations. */
export function timeline(episode: Episode, voiceDurations: number[]): { scenes: TimedScene[]; total: number } {
  let start = 0;
  const scenes = episode.scenes.map((scene, index) => {
    const voiceDuration = voiceDurations[index] ?? 3;
    const duration = Math.max(LEAD + voiceDuration + TAIL, scene.minSeconds ?? 0);
    const timed = { ...scene, start, duration, voiceDuration, index };
    start += duration;
    return timed;
  });
  return { scenes, total: start };
}
