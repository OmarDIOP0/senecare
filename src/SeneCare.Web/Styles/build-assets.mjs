// Builds front-end assets that are committed to wwwroot (no CDN: one less DNS/TLS round-trip on 3G).
// - wwwroot/img/icons.svg: sprite of the Lucide icons listed in Styles/icons.json (<use href="…#name">)
// - wwwroot/lib: minified HTMX, Leaflet, SignalR client
// - wwwroot/fonts: Google Fonts files (latin subset, via @fontsource), self-hosted for speed and privacy
import { readFileSync, writeFileSync, mkdirSync, copyFileSync } from "node:fs";

const icons = JSON.parse(readFileSync("Styles/icons.json", "utf8"));
const symbols = icons.map((name) => {
  const svg = readFileSync(`node_modules/lucide-static/icons/${name}.svg`, "utf8");
  const body = svg.slice(svg.indexOf(">", svg.indexOf("<svg")) + 1, svg.lastIndexOf("</svg>"))
    .replace(/\s*\n\s*/g, "")
    .replace(/\s+\/>/g, "/>");
  return `<symbol id="${name}" viewBox="0 0 24 24">${body}</symbol>`;
});

mkdirSync("wwwroot/img", { recursive: true });
writeFileSync(
  "wwwroot/img/icons.svg",
  `<svg xmlns="http://www.w3.org/2000/svg"><!-- Lucide (ISC license) --><defs>${symbols.join("")}</defs></svg>\n`,
);

mkdirSync("wwwroot/lib", { recursive: true });
copyFileSync("node_modules/htmx.org/dist/htmx.min.js", "wwwroot/lib/htmx.min.js");

// Leaflet (map of the « Mes logements » form only).
mkdirSync("wwwroot/lib/leaflet/images", { recursive: true });
for (const file of ["leaflet.js", "leaflet.css", "images/marker-icon.png", "images/marker-icon-2x.png", "images/marker-shadow.png"]) {
  copyFileSync(`node_modules/leaflet/dist/${file}`, `wwwroot/lib/leaflet/${file}`);
}

// SignalR client (live technician position, loaded only on the pages that track it).
mkdirSync("wwwroot/lib/signalr", { recursive: true });
copyFileSync("node_modules/@microsoft/signalr/dist/browser/signalr.min.js", "wwwroot/lib/signalr/signalr.min.js");

mkdirSync("wwwroot/fonts", { recursive: true });
for (const [pkg, file] of [
  // Text: Bricolage Grotesque. Titles: Unbounded (bold only).
  ["bricolage-grotesque", "bricolage-grotesque-latin-400-normal.woff2"],
  ["bricolage-grotesque", "bricolage-grotesque-latin-600-normal.woff2"],
  ["bricolage-grotesque", "bricolage-grotesque-latin-700-normal.woff2"],
  ["unbounded", "unbounded-latin-700-normal.woff2"],
]) {
  copyFileSync(`node_modules/@fontsource/${pkg}/files/${file}`, `wwwroot/fonts/${file}`);
}

console.log(`icons.svg: ${icons.length} icons`);
