import React from "react";
import { Easing, interpolate, spring, useVideoConfig } from "remotion";
import { color, font } from "../theme";

const ease = Easing.bezier(0.45, 0, 0.2, 1);
const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

const Card: React.FC<{ children: React.ReactNode; style?: React.CSSProperties }> = ({ children, style }) => (
  <div
    style={{
      borderRadius: 30,
      background: color.white,
      border: `1px solid ${color.ligne}`,
      boxShadow: "0 30px 60px rgba(19,34,43,0.22), 0 8px 18px rgba(19,34,43,0.12)",
      fontFamily: font.text,
      color: color.charbon,
      ...style,
    }}
  >
    {children}
  </div>
);

const Dot: React.FC<{ fill: string; size?: number; children?: React.ReactNode }> = ({ fill, size = 64, children }) => (
  <div style={{ width: size, height: size, borderRadius: "50%", background: fill, display: "grid", placeItems: "center", flexShrink: 0 }}>{children}</div>
);

const Check = ({ size = 34, stroke = "#fff" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={stroke} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
    <path d="M5 12.5l4.5 4.5L19 7.5" />
  </svg>
);

/**
 * « Even offline »: airplane mode switches on, the request is kept on the phone, the network returns, it leaves —
 * floating cards next to the phone, timed on the scene (0–1).
 */
export const OfflineOverlay: React.FC<{ texts: Record<string, string>; time: number; duration: number }> = ({ texts, time, duration }) => {
  const { fps } = useVideoConfig();
  const p = (t: number) => t * duration;
  const pop = (at: number) => spring({ frame: Math.round((time - p(at)) * fps), fps, config: { damping: 14, mass: 0.8 } });
  const online = time >= p(0.6);
  const sent = time >= p(0.66);
  const leave = interpolate(time, [p(0.84), p(0.94)], [1, 0], clamp);

  return (
    <div style={{ position: "absolute", inset: 0, zIndex: 40, opacity: leave }}>
      {/* Status pill: offline → online */}
      <div style={{ position: "absolute", top: 180, left: 0, right: 0, display: "flex", justifyContent: "center", transform: `scale(${pop(0.22)})` }}>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 18,
            padding: "18px 34px",
            borderRadius: 999,
            background: online ? color.baobab : color.charbon,
            color: color.white,
            fontFamily: font.text,
            fontWeight: 700,
            fontSize: 38,
            boxShadow: "0 18px 40px rgba(19,34,43,0.3)",
          }}
        >
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
            {online ? (
              <>
                <path d="M2 9a15 15 0 0 1 20 0" />
                <path d="M5 12.5a10 10 0 0 1 14 0" />
                <path d="M8.5 16a5 5 0 0 1 7 0" />
                <circle cx="12" cy="19.5" r="1" fill="#fff" />
              </>
            ) : (
              <path d="M10 13l-6 2v-2l6-4V5a2 2 0 0 1 4 0v4l6 4v2l-6-2v4l2 2v1l-4-1-4 1v-1l2-2z" />
            )}
          </svg>
          {online ? texts.online : texts.offline}
        </div>
      </div>
      {/* The request card */}
      <div style={{ position: "absolute", left: 90, right: 90, top: 760, transform: `translateY(${(1 - pop(0.32)) * 80}px) rotate(${-2 + pop(0.32) * 2}deg)`, opacity: pop(0.32) }}>
        <Card style={{ padding: 40, display: "flex", gap: 30, alignItems: "center" }}>
          <Dot fill={sent ? color.baobab : color.ocreClair}>
            {sent ? (
              <Check />
            ) : (
              <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke={color.laterite} strokeWidth="2.4" strokeLinecap="round">
                <path d="M7 3h10M7 21h10M8 3c0 5 8 5 8 9s-8 4-8 9M16 3c0 5-8 5-8 9" />
              </svg>
            )}
          </Dot>
          <div>
            <div style={{ fontWeight: 700, fontSize: 44 }}>{sent ? texts.sent : texts.waiting}</div>
            <div style={{ marginTop: 6, fontSize: 32, color: color.charbonDoux }}>{sent ? "SC-2026-000001 · Plomberie" : texts.waitingText}</div>
          </div>
        </Card>
      </div>
    </div>
  );
};

/** The phone pushed back while it is in « airplane mode » (slightly greyed). */
export const offlineDim = (time: number, duration: number) =>
  interpolate(time, [0.24 * duration, 0.3 * duration, 0.6 * duration, 0.66 * duration], [0, 0.35, 0.35, 0], clamp);

/**
 * « Choose your slot »: the notification drops from the top, three slot cards float out of the phone in 3D, the
 * second one is tapped and lights up.
 */
export const SlotCards: React.FC<{ texts: Record<string, string>; time: number; duration: number }> = ({ texts, time, duration }) => {
  const { fps } = useVideoConfig();
  const p = (t: number) => t * duration;
  const pop = (at: number) => spring({ frame: Math.round((time - p(at)) * fps), fps, config: { damping: 13, mass: 0.9 } });
  const notif = pop(0.04);
  const notifUp = interpolate(time, [p(0.3), p(0.38)], [0, 1], clamp);
  const chosenAt = p(0.62);
  const chosen = time >= chosenAt;
  const leave = interpolate(time, [p(0.8), p(0.9)], [1, 0], clamp);
  const slots = [texts.slot1, texts.slot2, texts.slot3];

  return (
    <div style={{ position: "absolute", inset: 0, zIndex: 40, opacity: leave }}>
      {/* Notification */}
      <div style={{ position: "absolute", left: 70, right: 70, top: 150, transform: `translateY(${(1 - notif) * -260 - notifUp * 300}px)`, opacity: 1 - notifUp }}>
        <Card style={{ padding: "30px 34px", display: "flex", gap: 26, alignItems: "center", borderRadius: 36, background: "rgba(255,255,255,0.97)" }}>
          <Dot fill={color.nuit} size={84}>
            <svg width="46" height="46" viewBox="0 0 24 24" fill="none" stroke={color.ambre} strokeWidth="2.2" strokeLinejoin="round">
              <path d="M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />
            </svg>
          </Dot>
          <div style={{ minWidth: 0 }}>
            <div style={{ fontFamily: font.mono, fontWeight: 700, fontSize: 22, letterSpacing: "0.12em", color: color.charbonDoux }}>SENECARE · MAINTENANT</div>
            <div style={{ fontWeight: 700, fontSize: 42, marginTop: 4 }}>{texts.notifTitle}</div>
            <div style={{ fontSize: 30, color: color.charbonDoux, marginTop: 2 }}>{texts.notifText}</div>
          </div>
        </Card>
      </div>
      {/* Floating slot cards in 3D */}
      <div style={{ position: "absolute", left: 0, right: 0, top: 470, perspective: 1400 }}>
        {slots.map((label, i) => {
          const inP = pop(0.32 + i * 0.07);
          const isChosen = i === 1 && chosen;
          const float = Math.sin(time * 1.6 + i) * 8;
          const tilt = [-14, 0, 14][i];
          return (
            <div
              key={i}
              style={{
                position: "absolute",
                left: 110,
                right: 110,
                top: i * 250,
                transform: `translateY(${(1 - inP) * 200 + float}px) translateZ(${isChosen ? 80 : 0}px) rotateX(10deg) rotateY(${tilt * (1 - inP * 0.6)}deg) scale(${0.6 + inP * 0.4 + (isChosen ? 0.06 : 0)})`,
                opacity: inP,
              }}
            >
              <Card
                style={{
                  padding: "40px 44px",
                  display: "flex",
                  alignItems: "center",
                  gap: 30,
                  border: isChosen ? `4px solid ${color.ambre}` : `1px solid ${color.ligne}`,
                  background: isChosen ? color.ocreClair : color.white,
                  boxShadow: isChosen ? "0 40px 80px rgba(242,181,68,0.45)" : undefined,
                }}
              >
                <Dot fill={isChosen ? color.laterite : color.sourdine} size={66}>
                  {isChosen ? (
                    <Check />
                  ) : (
                    <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke={color.charbonDoux} strokeWidth="2.2">
                      <rect x="3.5" y="5" width="17" height="15" rx="2.5" />
                      <path d="M3.5 10h17M8 3v4M16 3v4" />
                    </svg>
                  )}
                </Dot>
                <div style={{ fontWeight: 700, fontSize: 44 }}>{label}</div>
              </Card>
            </div>
          );
        })}
      </div>
      {/* The tap on the second slot */}
      {time > chosenAt - 0.6 && time < chosenAt + 0.7 && (
        <div
          style={{
            position: "absolute",
            left: 540 - 30,
            top: 470 + 250 + 70 - 30 + interpolate(time, [chosenAt - 0.6, chosenAt], [220, 0], clamp),
            width: 60,
            height: 60,
            borderRadius: "50%",
            background: "radial-gradient(circle at 40% 35%, rgba(255,255,255,0.95), rgba(255,255,255,0.6))",
            border: "3px solid rgba(19,34,43,0.35)",
            transform: `scale(${interpolate(time, [chosenAt - 0.06, chosenAt, chosenAt + 0.15], [1, 0.8, 1], clamp)})`,
            opacity: interpolate(time, [chosenAt - 0.6, chosenAt - 0.4, chosenAt + 0.4, chosenAt + 0.7], [0, 1, 1, 0], clamp),
            zIndex: 45,
          }}
        />
      )}
    </div>
  );
};

/** The phone pushed back and dimmed while the cards float. */
export const slotsDepth = (time: number, duration: number) => {
  const back = interpolate(time, [0.2 * duration, 0.32 * duration, 0.8 * duration, 0.9 * duration], [0, 1, 1, 0], clamp);
  return { back, style: { transform: `scale(${1 - 0.1 * ease(back)})`, transformOrigin: "50% 45%", filter: `blur(${back * 6}px)` } as React.CSSProperties };
};

/**
 * Live follow-up: a small map card (streets, route, home) where the technician's dot moves toward the house, with
 * the technician chip.
 */
export const LiveMap: React.FC<{ texts: Record<string, string>; time: number; duration: number }> = ({ texts, time, duration }) => {
  const { fps } = useVideoConfig();
  const inP = spring({ frame: Math.round((time - 0.38 * duration) * fps), fps, config: { damping: 15 } });
  const leave = interpolate(time, [duration - 0.6, duration - 0.2], [1, 0], clamp);
  const progress = ease(interpolate(time, [0.45 * duration, duration - 0.6], [0, 1], clamp));
  // Route in the card's own coordinates (800×520).
  const route = [
    [80, 440],
    [80, 300],
    [330, 300],
    [330, 150],
    [620, 150],
  ];
  const lengths = route.slice(1).map((pt, i) => Math.hypot(pt[0] - route[i][0], pt[1] - route[i][1]));
  const total = lengths.reduce((a, b) => a + b, 0);
  let left = progress * total;
  let pos = route[0];
  for (let i = 0; i < lengths.length; i++) {
    if (left <= lengths[i]) {
      const f = left / lengths[i];
      pos = [route[i][0] + (route[i + 1][0] - route[i][0]) * f, route[i][1] + (route[i + 1][1] - route[i][1]) * f];
      break;
    }
    left -= lengths[i];
    pos = route[i + 1];
  }
  const pulse = (time * 1.4) % 1;

  return (
    <div style={{ position: "absolute", left: 100, right: 100, top: 600, zIndex: 40, opacity: inP * leave, transform: `translateY(${(1 - inP) * 160}px) rotate(${(1 - inP) * -4}deg)` }}>
      <Card style={{ overflow: "hidden", padding: 0 }}>
        <svg width="880" height="520" viewBox="0 0 800 520" style={{ display: "block", width: "100%", height: "auto", background: "#EFE9DC" }}>
          {/* Blocks and streets */}
          {[...Array(6)].map((_, i) => (
            <rect key={"b" + i} x={20 + (i % 3) * 270} y={20 + Math.floor(i / 3) * 250} width={230} height={210} rx={18} fill="#E4DCCB" />
          ))}
          <path d="M0 300 H800 M0 150 H800 M80 0 V520 M330 0 V520 M620 0 V520" stroke="#FFFFFF" strokeWidth="26" />
          <path d="M0 300 H800 M0 150 H800 M80 0 V520 M330 0 V520 M620 0 V520" stroke="#F6F3EC" strokeWidth="18" />
          <rect x="420" y="330" width="320" height="150" rx="20" fill="#CFE3C8" />
          {/* Route and progress */}
          <polyline points={route.map((p) => p.join(",")).join(" ")} fill="none" stroke={color.nuit} strokeOpacity="0.25" strokeWidth="10" strokeDasharray="2 18" strokeLinecap="round" />
          <polyline
            points={route.map((p) => p.join(",")).join(" ")}
            fill="none"
            stroke={color.laterite}
            strokeWidth="10"
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeDasharray={`${progress * total} ${total}`}
          />
          {/* Home */}
          <g transform="translate(620 150)">
            <circle r="34" fill={color.nuit} />
            <path d="M-15 2 L0 -12 L15 2 V16 H-15 Z" fill={color.ambre} />
          </g>
          {/* Technician */}
          <circle cx={pos[0]} cy={pos[1]} r={22 + pulse * 30} fill={color.boutonHaut} opacity={0.35 * (1 - pulse)} />
          <circle cx={pos[0]} cy={pos[1]} r="22" fill={color.boutonHaut} stroke="#fff" strokeWidth="6" />
        </svg>
        <div style={{ display: "flex", alignItems: "center", gap: 24, padding: "28px 34px" }}>
          <Dot fill={color.ocreClair} size={70}>
            <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke={color.laterite} strokeWidth="2.2" strokeLinecap="round">
              <path d="M14.5 5.5a4 4 0 0 0-5.3 5.3L3.5 16.5l4 4 5.7-5.7a4 4 0 0 0 5.3-5.3l-2.6 2.6-2.6-2.6z" />
            </svg>
          </Dot>
          <div>
            <div style={{ fontWeight: 700, fontSize: 40 }}>{texts.name}</div>
            <div style={{ fontSize: 30, color: color.laterite, fontWeight: 600 }}>{texts.status} · {texts.home}</div>
          </div>
        </div>
      </Card>
    </div>
  );
};

/** Gold sparks around the « 5 » when the customer rates. */
export const Sparks: React.FC<{ x: number; y: number; at: number; time: number }> = ({ x, y, at, time }) => {
  const t = (time - at) / 0.9;
  if (t < 0 || t > 1) return null;
  return (
    <div style={{ position: "absolute", inset: 0, zIndex: 45, pointerEvents: "none" }}>
      {[...Array(10)].map((_, i) => {
        const angle = (i / 10) * Math.PI * 2;
        const r = 40 + ease(t) * 150;
        return (
          <svg key={i} width="40" height="40" viewBox="0 0 24 24" style={{ position: "absolute", left: x + Math.cos(angle) * r - 20, top: y + Math.sin(angle) * r - 20, opacity: 1 - t, transform: `rotate(${t * 120}deg)` }}>
            <path d="M12 2l2.6 6.6L21 9.3l-5 4.4 1.6 6.7L12 16.8 6.4 20.4 8 13.7 3 9.3l6.4-.7z" fill={color.ambre} />
          </svg>
        );
      })}
    </div>
  );
};
