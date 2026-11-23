import type { Episode } from "./types.ts";

// TUTO 01 — signal a breakdown, then follow it to the end. Screens: public/ep01/screens (npm run capture:ep -- 01).
// Demo people are fictional (Awa Ndiaye, Villa Ouakam, technician Ibrahima Ndoye).
const episode: Episode = {
  number: "01",
  title: "Signaler une panne en 30 secondes",
  kicker: "SENECARE · TUTO",
  next: { number: "02", teaser: "Payer son abonnement avec Wave ou Orange Money" },
  scenes: [
    {
      id: "intro",
      kind: "intro",
      voice: "Une fuite, une coupure de courant ? Voici comment la signaler à SeneCare.",
      gold: ["signaler"],
      minSeconds: 4.6,
    },
    {
      id: "espace",
      kind: "phone",
      voice: "Depuis votre espace, touchez Signaler.",
      gold: ["Signaler"],
      steps: [
        { at: 0, screen: "espace" },
        { at: 0.3, zoom: { target: "report", scale: 1.35 } },
        { at: 0.72, tap: "report" },
      ],
    },
    {
      id: "metier",
      kind: "phone",
      voice: "Choisissez le métier concerné : ici, la plomberie.",
      gold: ["la plomberie"],
      steps: [
        { at: 0, screen: "report-trade" },
        { at: 0.45, tap: "plumbing" },
        { at: 0.52, screen: "report-trade-chosen", zoom: { target: "plumbing", scale: 1.45 } },
      ],
    },
    {
      id: "description",
      kind: "phone",
      voice: "Décrivez la panne en une phrase, et ajoutez une photo.",
      gold: ["une photo"],
      steps: [
        { at: 0, screen: "report-describe", zoom: { target: "description", scale: 1.18 } },
        { at: 0.25, tap: "description" },
        { at: 0.4, screen: "report-described" },
        { at: 0.62, zoom: { target: "photo", scale: 1.45 } },
      ],
    },
    {
      id: "urgence",
      kind: "phone",
      voice: "Indiquez l'urgence : une grosse fuite est traitée en priorité.",
      gold: ["l'urgence", "en priorité"],
      steps: [
        { at: 0, screen: "report-urgency" },
        { at: 0.3, tap: "critical" },
        { at: 0.36, screen: "report-urgency-chosen", zoom: { target: "critical", scale: 1.45 } },
      ],
    },
    {
      id: "envoi",
      kind: "offline",
      voice: "Envoyez. Même sans réseau, la demande part dès que la connexion revient.",
      gold: ["sans réseau"],
      minSeconds: 6,
      steps: [
        { at: 0, screen: "report-send" },
        { at: 0.12, tap: "send" },
        { at: 0.78, screen: "sent" },
      ],
      texts: {
        offline: "Hors connexion",
        waiting: "Demande enregistrée",
        waitingText: "Elle partira dès le retour du réseau",
        online: "Réseau retrouvé",
        sent: "Demande envoyée",
      },
    },
    {
      id: "creneaux",
      kind: "slots",
      voice: "SeneCare vous propose des créneaux. Choisissez celui qui vous arrange.",
      gold: ["des créneaux"],
      minSeconds: 6.5,
      steps: [
        { at: 0, screen: "slots" },
        { at: 0.82, screen: "slots-chosen" },
      ],
      texts: {
        notifTitle: "Choisissez votre créneau",
        notifText: "Demande SC-2026-000001 · Plomberie",
        // As captured (development simulator: tomorrow 9 h, 15 h, the day after 10 h).
        slot1: "mar. 24 nov. · 09:00 – 11:00",
        slot2: "mar. 24 nov. · 15:00 – 17:00",
        slot3: "mer. 25 nov. · 10:00 – 12:00",
        chosen: "Créneau confirmé",
      },
    },
    {
      id: "suivi",
      kind: "tracking",
      voice: "Le jour J, vous savez qui vient, et vous suivez son arrivée en direct.",
      gold: ["en direct"],
      minSeconds: 6,
      steps: [
        { at: 0, screen: "on-the-way", zoom: { target: "technician", scale: 1.3 } },
        { at: 0.35, zoom: null },
      ],
      texts: { name: "Ibrahima Ndoye", status: "Technicien en route", home: "Villa Ouakam" },
    },
    {
      id: "validation",
      kind: "phone",
      voice: "Le travail est fini ? Validez, et donnez votre note.",
      gold: ["Validez"],
      steps: [
        { at: 0, screen: "completed", zoom: { target: "five", scale: 1.2 } },
        { at: 0.55, tap: "five" },
      ],
    },
    {
      id: "outro",
      kind: "outro",
      voice: "Prochain épisode : payer votre abonnement avec Wave ou Orange Money.",
      gold: ["Wave", "Orange Money"],
      minSeconds: 5,
    },
  ],
};

export default episode;
