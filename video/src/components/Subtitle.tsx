import React from "react";
import { interpolate } from "remotion";
import type { Chunk } from "../subtitles";
import { color, font } from "../theme";

/**
 * Burned-in subtitle: rounded Atlantic-blue band, white bold text, gold key words, two lines at most. Its bottom edge
 * sits 18 % above the bottom of the frame (clear of the « Répondre » bar of stories).
 */
export const Subtitle: React.FC<{ chunk: Chunk | null; time: number }> = ({ chunk, time }) => {
  if (!chunk) return null;
  const appear = interpolate(time, [chunk.start, chunk.start + 0.16], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return (
    <div style={{ position: "absolute", left: 60, right: 60, bottom: Math.round(1920 * 0.18), display: "flex", justifyContent: "center", zIndex: 50 }}>
      <div
        style={{
          maxWidth: 960,
          padding: "22px 38px 24px",
          borderRadius: 30,
          background: "rgba(10,31,42,0.86)",
          boxShadow: "0 18px 40px rgba(10,31,42,0.28)",
          color: color.white,
          fontFamily: font.text,
          fontWeight: 700,
          fontSize: 54,
          lineHeight: 1.22,
          textAlign: "center",
          opacity: appear,
          transform: `translateY(${(1 - appear) * 14}px)`,
        }}
      >
        {chunk.lines.map((line, i) => (
          <div key={i}>
            {line.map((word, k) => (
              <React.Fragment key={k}>
                <span style={{ color: word.gold ? color.ambre : color.white }}>{word.text}</span>
                {k < line.length - 1 ? " " : ""}
              </React.Fragment>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
};
