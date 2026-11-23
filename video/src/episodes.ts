import type { Episode } from "../episodes/types";
import ep01 from "../episodes/01";

/** Every episode, by number (add the next ones here: episodes/02.ts…). */
export const EPISODES: Record<string, Episode> = { "01": ep01 };
