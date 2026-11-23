import React from "react";
import { interpolate, spring, staticFile, useVideoConfig } from "remotion";
import { color, font } from "../theme";
import { Badge } from "./Badge";

/** Closing panel in SENECARE Atlantic blue (fine grid, pattern band): next episode badge and teaser, signature. */
export const Outro: React.FC<{ next: string; teaser: string; time: number; duration: number }> = ({ next, teaser, time, duration }) => {
  const { fps } = useVideoConfig();
  const inP = (delay: number) => spring({ frame: Math.round((time - delay) * fps), fps, config: { damping: 16 } });
  const fadeIn = interpolate(time, [0, 0.4], [0, 1], { extrapolateRight: "clamp" });
  const fadeOut = interpolate(time, [duration - 0.5, duration], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });

  return (
    <div style={{ position: "absolute", inset: 0, background: color.nuit, opacity: fadeIn * fadeOut, overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          inset: 0,
          backgroundImage: "linear-gradient(rgba(255,255,255,0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.05) 1px, transparent 1px)",
          backgroundSize: "56px 56px",
        }}
      />
      <div style={{ position: "absolute", width: 1100, height: 1100, left: -10, top: 200, borderRadius: "50%", background: "radial-gradient(circle, rgba(242,181,68,0.18), rgba(242,181,68,0) 60%)" }} />

      <div style={{ position: "absolute", top: 300, left: 70, right: 70, display: "flex", flexDirection: "column", alignItems: "center", gap: 56 }}>
        <div style={{ opacity: inP(0.2), transform: `translateY(${(1 - inP(0.2)) * 30}px)`, fontFamily: font.display, fontWeight: 700, fontSize: 92, letterSpacing: "0.02em" }}>
          <span style={{ color: color.white }}>SENE</span>
          <span style={{ color: color.ambre }}>CARE</span>
        </div>
        <div style={{ transform: `scale(${inP(0.55)})`, opacity: inP(0.55) }}>
          <Badge size={40}>ÉPISODE {next}</Badge>
        </div>
        <div
          style={{
            opacity: inP(0.85),
            transform: `translateY(${(1 - inP(0.85)) * 24}px)`,
            fontFamily: font.display,
            fontWeight: 700,
            fontSize: 64,
            lineHeight: 1.12,
            color: color.white,
            textAlign: "center",
          }}
        >
          {teaser}
        </div>
        <div style={{ opacity: inP(1.3), fontFamily: font.text, fontWeight: 600, fontSize: 38, color: color.or }}>Du plan à la réalité.</div>
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, bottom: 0, height: 56, backgroundImage: `url(${staticFile("brand/bande.svg")})`, backgroundRepeat: "repeat-x", backgroundSize: "auto 56px" }} />
    </div>
  );
};
