import React from "react";
import { cancelRender, Composition, continueRender, delayRender, staticFile } from "remotion";
import { EpisodeVideo, type EpisodeProps } from "./Episode";
import { EPISODES } from "./episodes";
import { FPS, HEIGHT, timeline, WIDTH } from "./timeline";

// The app's own fonts (public/brand), loaded before any frame is rendered.
const fonts: [string, string, string][] = [
  ["Unbounded", "brand/unbounded-latin-700-normal.woff2", "700"],
  ["Bricolage Grotesque", "brand/bricolage-grotesque-latin-600-normal.woff2", "600"],
  ["Bricolage Grotesque", "brand/bricolage-grotesque-latin-700-normal.woff2", "700"],
  ["Space Mono", "brand/space-mono-latin-700-normal.woff2", "700"],
];
const waitForFonts = delayRender("fonts");
Promise.all(
  fonts.map(([family, file, weight]) => {
    const face = new FontFace(family, `url(${staticFile(file)}) format("woff2")`, { weight });
    (document.fonts as unknown as Set<FontFace>).add(face);
    return face.load();
  }),
)
  .then(() => continueRender(waitForFonts))
  .catch((err) => cancelRender(err));

const defaults: EpisodeProps = { ep: "01", voice: [], screens: {}, music: null };

export const Root: React.FC = () => (
  <Composition
    id="Episode"
    component={EpisodeVideo}
    fps={FPS}
    width={WIDTH}
    height={HEIGHT}
    durationInFrames={FPS * 60}
    defaultProps={defaults}
    calculateMetadata={({ props }) => ({
      durationInFrames: Math.ceil(timeline(EPISODES[props.ep], props.voice).total * FPS),
    })}
  />
);
