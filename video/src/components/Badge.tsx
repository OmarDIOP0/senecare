import React from "react";
import { color, font } from "../theme";

/** Gold pill in small mono capitals (« TUTO 01 », « ÉPISODE 02 »), like the app's eyebrows. */
export const Badge: React.FC<{ children: React.ReactNode; size?: number; style?: React.CSSProperties }> = ({ children, size = 34, style }) => (
  <div
    style={{
      display: "inline-flex",
      alignItems: "center",
      gap: size * 0.4,
      padding: `${size * 0.42}px ${size * 0.9}px`,
      borderRadius: 999,
      background: `linear-gradient(180deg, ${color.or}, ${color.ambre})`,
      color: color.nuit,
      fontFamily: font.mono,
      fontWeight: 700,
      fontSize: size,
      letterSpacing: "0.16em",
      boxShadow: "0 10px 30px rgba(242,181,68,0.35), inset 0 1px 0 rgba(255,255,255,0.6)",
      ...style,
    }}
  >
    <span style={{ width: size * 0.32, height: size * 0.32, borderRadius: "50%", background: color.laterite }} />
    {children}
  </div>
);
