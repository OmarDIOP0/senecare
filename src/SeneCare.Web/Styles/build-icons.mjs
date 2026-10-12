// Generates the PWA icons from the SeneCare roof mark (run after changing the logo: npm run icons:build).
import { mkdirSync, writeFileSync } from "node:fs";
import { Resvg } from "@resvg/resvg-js";

const colors = { laterite: "#a84b24", atlantique: "#12355b", baobab: "#2e5e3e", sable: "#f7f1e6", soleil: "#e8a317" };

// Mark drawn in a 32×32 box (same as Views/Shared/Ui/_BrandMark.cshtml).
const mark = (roof, body, dot) => `
  <path d="M3 15 16 5l13 10" fill="none" stroke="${roof}" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M8 14v13h16V14" fill="none" stroke="${body}" stroke-width="3" stroke-linejoin="round"/>
  <circle cx="16" cy="20" r="2.5" fill="${dot}"/>`;

/** Square icon: background + mark occupying `ratio` of the size, centred. */
const icon = (size, background, ratio, palette) => {
  const scale = (size * ratio) / 32;
  const offset = (size - size * ratio) / 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
    <rect width="${size}" height="${size}" fill="${background}"/>
    <g transform="translate(${offset} ${offset}) scale(${scale})">${mark(...palette)}</g></svg>`;
};

const standard = [colors.laterite, colors.atlantique, colors.baobab];
const onLaterite = [colors.sable, colors.sable, colors.soleil];

const outputs = {
  "icon-192.png": icon(192, colors.sable, 0.72, standard),
  "icon-512.png": icon(512, colors.sable, 0.72, standard),
  // Maskable: the platform crops up to 20 % on each side, so the mark stays inside the central 60 %.
  "maskable-192.png": icon(192, colors.laterite, 0.56, onLaterite),
  "maskable-512.png": icon(512, colors.laterite, 0.56, onLaterite),
  "apple-touch-icon.png": icon(180, colors.sable, 0.7, standard),
  "shortcut-report-96.png": icon(96, colors.laterite, 0.6, onLaterite),
};

mkdirSync("wwwroot/img/icons", { recursive: true });
for (const [file, svg] of Object.entries(outputs)) {
  writeFileSync(`wwwroot/img/icons/${file}`, new Resvg(svg).render().asPng());
}

writeFileSync(
  "wwwroot/img/icons/favicon.svg",
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">${mark(...standard).replace(/\s*\n\s*/g, "")}</svg>\n`,
);

console.log(`icons: ${Object.keys(outputs).length + 1} files`);
