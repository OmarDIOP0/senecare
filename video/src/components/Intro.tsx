import React from "react";
import { Easing, Img, interpolate, spring, staticFile, useVideoConfig } from "remotion";
import { color, font } from "../theme";
import { Badge } from "./Badge";

const ease = Easing.bezier(0.45, 0, 0.2, 1);

/**
 * Cinematic opening: warm wooden desk, sunlight through blinds, the phone on its stand showing the app, slow push-in;
 * gold « TUTO 0X » badge and the episode title.
 */
export const Intro: React.FC<{ number: string; title: string; screen: string; time: number; duration: number }> = ({ number, title, screen, time, duration }) => {
  const { fps } = useVideoConfig();
  const push = 1 + 0.12 * ease(Math.min(1, time / duration));
  const drift = Math.sin(time * 0.6) * 12;
  const badge = spring({ frame: Math.round((time - 0.35) * fps), fps, config: { damping: 14, mass: 0.7 } });
  const titleIn = spring({ frame: Math.round((time - 0.75) * fps), fps, config: { damping: 18 } });
  const out = interpolate(time, [duration - 0.45, duration], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });

  return (
    <div style={{ position: "absolute", inset: 0, overflow: "hidden", background: "#2b1c12", opacity: out }}>
      <div style={{ position: "absolute", inset: 0, transform: `scale(${push})`, transformOrigin: "50% 62%" }}>
        {/* Wall */}
        <div style={{ position: "absolute", inset: 0, background: "linear-gradient(180deg, #e9d7bd 0%, #d6bb96 55%, #b99670 100%)" }} />
        {/* Desk: warm wood with grain */}
        <div
          style={{
            position: "absolute",
            left: -100,
            right: -100,
            top: 1380,
            bottom: -100,
            background:
              "repeating-linear-gradient(178deg, rgba(0,0,0,0.05) 0 3px, rgba(255,255,255,0.02) 3px 9px), linear-gradient(180deg, #8a5a36 0%, #6e4527 45%, #4a2d18 100%)",
            boxShadow: "0 -6px 0 rgba(60,36,20,0.6)",
          }}
        />
        <svg width="1080" height="1920" style={{ position: "absolute", inset: 0, opacity: 0.18, mixBlendMode: "multiply" }}>
          <filter id="grain">
            <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="3" />
          </filter>
          <rect width="1080" height="1920" filter="url(#grain)" />
        </svg>
        {/* Sunlight through blinds */}
        <div
          style={{
            position: "absolute",
            left: -400,
            top: -200,
            width: 2000,
            height: 2400,
            transform: `rotate(-24deg) translateX(${drift}px)`,
            background: "repeating-linear-gradient(180deg, rgba(255,226,170,0.34) 0 46px, rgba(255,226,170,0) 46px 92px)",
            filter: "blur(10px)",
            mixBlendMode: "screen",
          }}
        />
        {/* Phone on its stand */}
        <div style={{ position: "absolute", left: 540 - 150, top: 1290, width: 300, height: 130, borderRadius: 24, background: "linear-gradient(180deg,#3b3f44,#1d2024)", boxShadow: "0 30px 50px rgba(0,0,0,0.45)" }} />
        <div style={{ position: "absolute", left: 540 - 36, top: 1180, width: 72, height: 150, background: "linear-gradient(90deg,#2a2d31,#4a4e53,#2a2d31)", borderRadius: 10 }} />
        <div
          style={{
            position: "absolute",
            left: 540 - 180,
            top: 560,
            width: 360,
            height: 760,
            borderRadius: 58,
            padding: 13,
            background: "linear-gradient(145deg,#5b5f63,#16181a 40%,#2e3134 85%,#6a6e72)",
            transform: "perspective(1600px) rotateX(9deg)",
            boxShadow: "0 50px 90px rgba(0,0,0,0.45)",
          }}
        >
          <div style={{ width: "100%", height: "100%", borderRadius: 46, overflow: "hidden", background: color.sable, position: "relative" }}>
            <Img src={staticFile(screen)} style={{ width: "100%", height: "100%", objectFit: "cover", objectPosition: "top" }} />
            <div style={{ position: "absolute", inset: 0, background: "linear-gradient(125deg, rgba(255,255,255,0.28), rgba(255,255,255,0) 40%)" }} />
          </div>
        </div>
        {/* Vignette */}
        <div style={{ position: "absolute", inset: 0, background: "radial-gradient(ellipse at 50% 45%, rgba(0,0,0,0) 45%, rgba(30,18,10,0.55) 100%)" }} />
      </div>

      {/* Title block, top third */}
      <div style={{ position: "absolute", top: 120, left: 60, right: 60, display: "flex", flexDirection: "column", alignItems: "center", gap: 34 }}>
        <div style={{ transform: `scale(${badge}) translateY(${(1 - badge) * 20}px)`, opacity: badge }}>
          <Badge size={38}>TUTO {number}</Badge>
        </div>
        <div
          style={{
            opacity: titleIn,
            transform: `translateY(${(1 - titleIn) * 30}px)`,
            fontFamily: font.display,
            fontWeight: 700,
            fontSize: 74,
            lineHeight: 1.08,
            letterSpacing: "-0.02em",
            color: color.white,
            textAlign: "center",
            textShadow: "0 6px 30px rgba(20,10,4,0.55)",
          }}
        >
          {title}
        </div>
      </div>
    </div>
  );
};
