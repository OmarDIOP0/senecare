// Subtitle chunks of a voice sentence: cut at the punctuation (a clause per chunk), long clauses split at a comma
// near their middle, two balanced lines at most, no orphan word. Each chunk stays on screen for a share of the
// sentence's audio proportional to its length. Shared by the video and the .srt export.

export const MAX_LINE = 30;

export type Word = { text: string; gold: boolean };
export type Chunk = { lines: Word[][]; start: number; end: number };

const size = (words: Word[]) => words.map((w) => w.text).join(" ").length;

/** Words of the sentence, marked gold when they belong to one of the gold phrases (even across a line break). */
export function markGold(text: string, gold: string[]): Word[] {
  const words = text.split(/\s+/).filter(Boolean);
  const marks = words.map(() => false);
  for (const phrase of gold) {
    const target = phrase.split(/\s+/);
    for (let i = 0; i + target.length <= words.length; i++) {
      if (target.every((t, k) => words[i + k].replace(/[.,:;!?]+$/, "") === t)) {
        for (let k = 0; k < target.length; k++) marks[i + k] = true;
      }
    }
  }
  return words.map((w, i) => ({ text: w, gold: marks[i] }));
}

/** Clauses: a new one after « . ? ! : ; » (French spacing puts « ? » and « : » as words of their own). */
function clauses(words: Word[]): Word[][] {
  const out: Word[][] = [];
  let current: Word[] = [];
  for (const w of words) {
    current.push(w);
    if (/[.?!:;]$/.test(w.text)) {
      out.push(current);
      current = [];
    }
  }
  if (current.length) out.push(current);
  return out;
}

/** Splits a clause longer than two lines: at the comma nearest the middle, else at the middle in characters. */
function parts(words: Word[]): Word[][] {
  if (size(words) <= MAX_LINE * 2 || words.length < 2) return [words];
  const half = size(words) / 2;
  let best = -1;
  let bestGap = Infinity;
  for (let i = 1; i < words.length; i++) {
    const gap = Math.abs(size(words.slice(0, i)) - half);
    const comma = words[i - 1].text.endsWith(",");
    const score = comma ? gap : gap + 1000;
    if (score < bestGap) {
      bestGap = score;
      best = i;
    }
  }
  return [...parts(words.slice(0, best)), ...parts(words.slice(best))];
}

/** One line, or two lines balanced in length. */
function lines(words: Word[]): Word[][] {
  if (size(words) <= MAX_LINE || words.length < 2) return [words];
  let best = 1;
  let bestMax = Infinity;
  for (let i = 1; i < words.length; i++) {
    const longest = Math.max(size(words.slice(0, i)), size(words.slice(i)));
    if (longest < bestMax) {
      bestMax = longest;
      best = i;
    }
  }
  return [words.slice(0, best), words.slice(best)];
}

export function chunkSentence(text: string, gold: string[], duration: number): Chunk[] {
  const blocks: Word[][][] = clauses(markGold(text, gold)).flatMap(parts).map(lines);
  // Two single lines in a row share one chunk (« Envoyez. » + « Même sans réseau, »).
  const merged: Word[][][] = [];
  for (const block of blocks) {
    const last = merged[merged.length - 1];
    if (last && last.length === 1 && block.length === 1) last.push(block[0]);
    else merged.push(block);
  }
  const length = (b: Word[][]) => b.reduce((sum, l) => sum + size(l), 0);
  const total = merged.reduce((sum, b) => sum + length(b), 0);
  let t = 0;
  return merged.map((b) => {
    const share = (length(b) / total) * duration;
    const chunk = { lines: b, start: t, end: t + share };
    t += share;
    return chunk;
  });
}

export const plain = (chunk: Chunk) => chunk.lines.map((l) => l.map((w) => w.text).join(" ")).join("\n");
