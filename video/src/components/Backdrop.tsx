import React from "react";
import { color, font } from "../theme";

/** Sable background of the phone scenes: warm glows and the episode label at the top (inside the 60 px safe area). */
export const Backdrop: React.FC<{ label: string; time: number }> = ({ label, time }) => (
  <div style={{ position: "absolute", inset: 0, background: color.sable, overflow: "hidden" }}>
    <div
      style={{
        position: "absolute",
        width: 900,
        height: 900,
        left: -300 + Math.sin(time * 0.35) * 30,
        top: 80,
        borderRadius: "50%",
        background: "radial-gradient(circle, rgba(242,181,68,0.30), rgba(242,181,68,0) 65%)",
      }}
    />
    <div
      style={{
        position: "absolute",
        width: 1000,
        height: 1000,
        right: -420,
        bottom: 160 + Math.cos(time * 0.3) * 30,
        borderRadius: "50%",
        background: "radial-gradient(circle, rgba(217,98,43,0.20), rgba(217,98,43,0) 65%)",
      }}
    />
    <div style={{ position: "absolute", top: 66, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
      <div
        style={{
          padding: "12px 26px",
          borderRadius: 999,
          background: "rgba(255,255,255,0.75)",
          border: `1px solid ${color.ligne}`,
          fontFamily: font.mono,
          fontWeight: 700,
          fontSize: 24,
          letterSpacing: "0.14em",
          color: color.nuit,
          textTransform: "uppercase",
        }}
      >
        {label}
      </div>
    </div>
  </div>
);
