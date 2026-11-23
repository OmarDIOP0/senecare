// Shape of an episode: every scene has one voice-over sentence; its length (measured on the real audio) sets the
// scene duration, and every « at » below is a fraction of that duration (0 = start, 1 = end).

/** A moment on the phone: show a screen, tap a target and / or zoom on it (targets = keys of screens.json boxes). */
export type PhoneStep = {
  at: number;
  screen?: string;
  tap?: string;
  zoom?: { target: string; scale: number } | null;
};

export type SceneKind = "intro" | "phone" | "offline" | "slots" | "tracking" | "outro";

export type Scene = {
  id: string;
  kind: SceneKind;
  voice: string;
  /** Words or phrases of the voice shown in gold in the subtitles. */
  gold: string[];
  /** Minimum length in seconds (intro, outro, scenes with a long animation). */
  minSeconds?: number;
  steps?: PhoneStep[];
  /** Texts drawn by the « wow » animations (not app screens). */
  texts?: Record<string, string>;
};

export type Episode = {
  number: string;
  title: string;
  kicker: string;
  next: { number: string; teaser: string };
  scenes: Scene[];
};
