import React from "react";
import { AbsoluteFill, Audio, interpolate, Sequence, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import { EPISODES } from "./episodes";
import { Backdrop } from "./components/Backdrop";
import { Intro } from "./components/Intro";
import { Outro } from "./components/Outro";
import { LiveMap, OfflineOverlay, offlineDim, SlotCards, slotsDepth, Sparks } from "./components/Overlays";
import { PhoneStage, tapsOf, type Screens } from "./components/PhoneStage";
import { Subtitle } from "./components/Subtitle";
import { subtitleTrack } from "./track";
import { LEAD, timeline } from "./timeline";
import { color } from "./theme";

export type EpisodeProps = {
  ep: string;
  /** Measured length (s) of each scene's voice file, in scene order. */
  voice: number[];
  screens: Screens;
  music: string | null;
};

export const EpisodeVideo: React.FC<EpisodeProps> = ({ ep, voice, screens, music }) => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames } = useVideoConfig();
  const time = frame / fps;
  const episode = EPISODES[ep];
  const { scenes } = timeline(episode, voice);
  const scene = scenes.find((s) => time >= s.start && time < s.start + s.duration) ?? scenes[scenes.length - 1];
  const local = time - scene.start;
  const chunk = subtitleTrack(scenes).find((c) => time >= c.start && time < c.end) ?? null;
  const label = `TUTO ${episode.number} · ${episode.title}`;
  const steps = scene.steps ?? [];
  const end = durationInFrames / fps;

  let content: React.ReactNode;
  switch (scene.kind) {
    case "intro":
      content = <Intro number={episode.number} title={episode.title} screen={screens.espace.file} time={local} duration={scene.duration} />;
      break;
    case "outro":
      content = <Outro next={episode.next.number} teaser={episode.next.teaser} time={local} duration={scene.duration} />;
      break;
    case "offline":
      content = (
        <>
          <Backdrop label={label} time={time} />
          <PhoneStage steps={steps} screens={screens} duration={scene.duration} time={local} dim={offlineDim(local, scene.duration)} />
          <OfflineOverlay texts={scene.texts ?? {}} time={local} duration={scene.duration} />
        </>
      );
      break;
    case "slots": {
      const depth = slotsDepth(local, scene.duration);
      content = (
        <>
          <Backdrop label={label} time={time} />
          <PhoneStage steps={steps} screens={screens} duration={scene.duration} time={local} style={depth.style} dim={depth.back * 0.35} />
          <SlotCards texts={scene.texts ?? {}} time={local} duration={scene.duration} />
        </>
      );
      break;
    }
    case "tracking":
      content = (
        <>
          <Backdrop label={label} time={time} />
          <PhoneStage steps={steps} screens={screens} duration={scene.duration} time={local} />
          <LiveMap texts={scene.texts ?? {}} time={local} duration={scene.duration} />
        </>
      );
      break;
    default: {
      const taps = tapsOf(steps, screens, scene.duration);
      const rating = scene.id === "validation" ? taps[0] : undefined;
      content = (
        <>
          <Backdrop label={label} time={time} />
          <PhoneStage
            steps={steps}
            screens={screens}
            duration={scene.duration}
            time={local}
            screenOverlay={rating ? <Sparks x={rating.x} y={rating.y} at={rating.t} time={local} /> : null}
          />
        </>
      );
    }
  }

  return (
    <AbsoluteFill style={{ background: color.sable }}>
      {content}
      <Subtitle chunk={chunk} time={time} />
      {scenes.map((s) => (
        <Sequence key={s.id} from={Math.round((s.start + LEAD) * fps)} durationInFrames={Math.ceil((s.voiceDuration + 0.3) * fps)}>
          <Audio src={staticFile(`ep${ep}/vo/${s.id}.mp3`)} />
        </Sequence>
      ))}
      {music && (
        <Audio
          src={staticFile(music)}
          // About −20 dB under the voice, faded in and out.
          volume={(f) => 0.1 * interpolate(f / fps, [0, 1.2, end - 1.8, end], [0, 1, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })}
        />
      )}
    </AbsoluteFill>
  );
};
