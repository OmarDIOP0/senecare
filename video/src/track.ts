import { chunkSentence, type Chunk } from "./subtitles.ts";
import { LEAD, type TimedScene } from "./timeline.ts";

/** Subtitle chunks over the whole video; each stays until the next one (or a little after its voice). */
export function subtitleTrack(scenes: TimedScene[]): Chunk[] {
  const chunks = scenes.flatMap((s) =>
    chunkSentence(s.voice, s.gold, s.voiceDuration).map((c) => ({ ...c, start: c.start + s.start + LEAD, end: c.end + s.start + LEAD })),
  );
  return chunks.map((c, i) => ({ ...c, end: Math.min(c.end + 0.35, chunks[i + 1]?.start ?? c.end + 0.35) }));
}
