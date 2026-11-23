// Renders an episode: voice files (one per sentence), music, subtitles (.srt) and the MP4.
//   npm run render:ep -- 01
// Needs: public/epXX/screens.json (npm run capture:ep -- XX), Python with edge-tts and numpy, ffmpeg, Microsoft Edge.
// Voice: ElevenLabs when ELEVENLABS_API_KEY is set (voice ELEVENLABS_VOICE_ID), otherwise edge-tts « fr-FR-DeniseNeural ».
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { timeline } from "../src/timeline.ts";
import { subtitleTrack } from "../src/track.ts";
import { plain } from "../src/subtitles.ts";

const ep = process.argv[2] ?? "01";
const root = resolve(import.meta.dirname, "..");
const publicDir = join(root, "public");
const voDir = join(publicDir, `ep${ep}`, "vo");
const outDir = join(root, "out");
mkdirSync(voDir, { recursive: true });
mkdirSync(outDir, { recursive: true });

const episode = (await import(pathToFileURL(join(root, "episodes", `${ep}.ts`)).href)).default;
const screensFile = join(publicDir, `ep${ep}`, "screens.json");
if (!existsSync(screensFile)) throw new Error(`No screens: run « npm run capture:ep -- ${ep} » first.`);
const { screens } = JSON.parse(readFileSync(screensFile, "utf8"));

// ---- Voice: one file per sentence, regenerated only when the sentence changes ------------------------------------
const manifestFile = join(voDir, "manifest.json");
const manifest = existsSync(manifestFile) ? JSON.parse(readFileSync(manifestFile, "utf8")) : {};
const hash = (text) => createHash("sha1").update(text).digest("hex").slice(0, 12);

async function speak(text, file) {
  if (process.env.ELEVENLABS_API_KEY) {
    const voice = process.env.ELEVENLABS_VOICE_ID ?? "21m00Tcm4TlvDq8ikWAM";
    const response = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${voice}`, {
      method: "POST",
      headers: { "xi-api-key": process.env.ELEVENLABS_API_KEY, "Content-Type": "application/json", Accept: "audio/mpeg" },
      body: JSON.stringify({ text, model_id: "eleven_multilingual_v2" }),
    });
    if (!response.ok) throw new Error("ElevenLabs: " + response.status);
    writeFileSync(file, Buffer.from(await response.arrayBuffer()));
    return;
  }
  execFileSync("python", ["-m", "edge_tts", "--voice", "fr-FR-DeniseNeural", "--rate=-4%", "--text", text, "--write-media", file], { stdio: "inherit" });
}

const durationOf = (file) =>
  Number(execFileSync("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file]).toString().trim());

const voice = [];
for (const scene of episode.scenes) {
  const file = join(voDir, `${scene.id}.mp3`);
  if (!existsSync(file) || manifest[scene.id] !== hash(scene.voice)) {
    console.log(`Voix « ${scene.id} »…`);
    await speak(scene.voice, file);
    manifest[scene.id] = hash(scene.voice);
  }
  voice.push(Math.round(durationOf(file) * 100) / 100);
}
writeFileSync(manifestFile, JSON.stringify(manifest, null, 2));

// ---- Music ---------------------------------------------------------------------------------------------------------
const { total, scenes } = timeline(episode, voice);
const musicDir = join(publicDir, "music");
mkdirSync(musicDir, { recursive: true });
const music = join(musicDir, `ep${ep}.mp3`);
if (!existsSync(music)) {
  const wav = join(musicDir, `ep${ep}.wav`);
  execFileSync("python", ["-I", join(root, "scripts", "make_music.py"), wav, String(Math.ceil(total) + 2)], { stdio: "inherit" });
  execFileSync("ffmpeg", ["-y", "-loglevel", "error", "-i", wav, "-b:a", "160k", music]);
}

// ---- Subtitles (.srt) ----------------------------------------------------------------------------------------------
const stamp = (s) => {
  const ms = Math.round(s * 1000);
  const p = (v, n = 2) => String(v).padStart(n, "0");
  return `${p(Math.floor(ms / 3600000))}:${p(Math.floor(ms / 60000) % 60)}:${p(Math.floor(ms / 1000) % 60)},${p(ms % 1000, 3)}`;
};
const srt = subtitleTrack(scenes).map((c, i) => `${i + 1}\n${stamp(c.start)} --> ${stamp(c.end)}\n${plain(c)}\n`).join("\n");
writeFileSync(join(outDir, `ep${ep}.srt`), srt);

// ---- Render ----------------------------------------------------------------------------------------------------------
const props = join(outDir, `ep${ep}.props.json`);
writeFileSync(props, JSON.stringify({ ep, voice, screens, music: `music/ep${ep}.mp3` }));
const browser = process.env.REMOTION_BROWSER ?? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const raw = join(outDir, `ep${ep}.raw.mp4`);
const video = join(outDir, `ep${ep}.mp4`);
// Node runs the Remotion CLI directly (no shell): paths with spaces stay whole.
execFileSync(
  process.execPath,
  [join(root, "node_modules", "@remotion", "cli", "remotion-cli.js"), "render", "src/index.ts", "Episode", raw, `--props=${props}`, "--codec=h264", "--audio-codec=aac", "--crf=18", `--browser-executable=${browser}`],
  { cwd: root, stdio: "inherit" },
);
// Loudness for stories (about −14 LUFS, peaks under −1.5 dB); the picture is copied as is.
execFileSync("ffmpeg", ["-y", "-loglevel", "error", "-i", raw, "-c:v", "copy", "-af", "loudnorm=I=-14:TP=-1.5:LRA=11", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", video]);
rmSync(raw, { force: true });
console.log(`\nVidéo : ${video}\nSous-titres : ${join(outDir, `ep${ep}.srt`)}\nDurée : ${total.toFixed(1)} s`);
