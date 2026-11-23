import React from "react";

// Phone geometry in the 1080×1920 frame. The app screens are 390×844 CSS px (captured at ×3).
export const SCREEN_CSS = { width: 390, height: 844 };
export const SCREEN_W = 560;
export const K = SCREEN_W / SCREEN_CSS.width;
export const SCREEN_H = Math.round(SCREEN_CSS.height * K);
export const BEZEL = 20;
export const PHONE_W = SCREEN_W + BEZEL * 2;
export const PHONE_H = SCREEN_H + BEZEL * 2;
export const PHONE_LEFT = (1080 - PHONE_W) / 2;
export const PHONE_TOP = 150;

/** Video coordinates of a point given in app-screen CSS px. */
export const toVideo = (x: number, y: number) => ({ x: PHONE_LEFT + BEZEL + x * K, y: PHONE_TOP + BEZEL + y * K });

/** A realistic modern phone: titanium edge, black bezel, dynamic island, side buttons, glass glare. */
export const PhoneFrame: React.FC<{ children: React.ReactNode; glare?: number }> = ({ children, glare = 1 }) => (
  <div
    style={{
      position: "absolute",
      left: PHONE_LEFT,
      top: PHONE_TOP,
      width: PHONE_W,
      height: PHONE_H,
      borderRadius: 86,
      background: "linear-gradient(145deg, #5b5f63 0%, #2a2c2f 18%, #16181a 50%, #2e3134 82%, #6a6e72 100%)",
      padding: 4,
      boxShadow: "0 60px 120px rgba(19,34,43,0.35), 0 20px 40px rgba(19,34,43,0.25), inset 0 0 0 1px rgba(255,255,255,0.12)",
    }}
  >
    {/* Side buttons */}
    {[
      { left: -7, top: 230, height: 70 },
      { left: -7, top: 330, height: 110 },
      { left: -7, top: 460, height: 110 },
      { left: PHONE_W - 1, top: 360, height: 160 },
    ].map((b, i) => (
      <div key={i} style={{ position: "absolute", left: b.left, top: b.top, width: 8, height: b.height, borderRadius: 4, background: "linear-gradient(90deg,#3a3d40,#77797c,#3a3d40)" }} />
    ))}
    <div style={{ position: "absolute", inset: 4, borderRadius: 82, background: "#050607", padding: BEZEL - 4 }}>
      <div style={{ position: "relative", width: SCREEN_W, height: SCREEN_H, borderRadius: 62, overflow: "hidden", background: "#F6F3EC" }}>
        {children}
        {/* Dynamic island */}
        <div style={{ position: "absolute", top: 14, left: SCREEN_W / 2 - 64, width: 128, height: 36, borderRadius: 20, background: "#000", zIndex: 20 }} />
        {/* Glass glare */}
        <div
          style={{
            position: "absolute",
            inset: 0,
            zIndex: 19,
            pointerEvents: "none",
            opacity: 0.55 * glare,
            background: "linear-gradient(125deg, rgba(255,255,255,0.18) 0%, rgba(255,255,255,0.04) 28%, rgba(255,255,255,0) 45%)",
          }}
        />
      </div>
    </div>
  </div>
);
