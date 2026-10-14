// Design review: screenshots of the key screens, before / after, 1440 px and 390 px, light and dark.
//   node design-review.mjs after                      → docs/design-review/after/*.png
//   node design-review.mjs before ../../../SeneCare-before   → docs/design-review/before/*.png
// Starts the app (Development, local PostgreSQL), signs in with phone + OTP read from the console log
// (SMS are only logged in Development), creates sample accounts, then captures each screen.
import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { resolve, join } from "node:path";
import { chromium } from "playwright";

const label = process.argv[2] ?? "after";
const rootArg = process.argv[3]?.startsWith("--") ? undefined : process.argv[3];
const root = resolve(rootArg ?? join(import.meta.dirname, "..", ".."));
const port = label === "before" ? 5311 : 5310;
const base = `http://localhost:${port}`;
const out = resolve(import.meta.dirname, "..", "..", "docs", "design-review", label);
mkdirSync(out, { recursive: true });

// ---- Server -----------------------------------------------------------------
const randomPhone = () => "77" + String(Math.floor(1_000_000 + Math.random() * 8_999_999));
// A fresh SuperAdmin for this run, granted at start-up (Bootstrap:SuperAdminPhones, local server only).
const adminPhone = randomPhone();
// The technician played by the development simulator (RequestSimulator).
const technicianPhone = "700000001";
const log = [];
const server = spawn("dotnet", ["run", "--no-build", "--project", join(root, "src", "SeneCare.Web"), "--urls", base], {
  // Each run signs up three accounts from localhost: lift the per-IP OTP limit for this local server only.
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_ENVIRONMENT: "Development", Otp__MaxPerIpPerHour: "1000", Otp__MaxPerPhonePerHour: "1000", Bootstrap__SuperAdminPhones: adminPhone },
});
server.stdout.on("data", (d) => log.push(d.toString()));
server.stderr.on("data", (d) => log.push(d.toString()));
const stop = () => { try { server.kill(); } catch { /* already gone */ } };
process.on("exit", stop);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let started = false;
for (let i = 0; i < 300 && !started; i++) {
  started = log.join("").includes("Now listening");
  if (!started) await sleep(1000);
}
if (!started) {
  console.error(log.join("").slice(-3000));
  stop();
  process.exit(1);
}

const formatted = (p) => `${p.slice(0, 2)} ${p.slice(2, 5)} ${p.slice(5, 7)} ${p.slice(7)}`;
const codeFor = async (phone) => {
  for (let i = 0; i < 30; i++) {
    const lines = log.join("").split("\n").filter((l) => l.includes(formatted(phone)) && /\b\d{6}\b/.test(l));
    if (lines.length) return lines.at(-1).match(/\b(\d{6})\b/)[1];
    await sleep(500);
  }
  throw new Error("No OTP for " + phone);
};

// On failure, the end of the server log says why.
process.on("uncaughtException", (e) => {
  console.error(e.message);
  console.error(log.join("").split("\n").filter((l) => /WRN|ERR|HTTP (GET|POST)/.test(l)).slice(-15).join("\n"));
  stop();
  process.exit(1);
});

// ---- Flows ---------------------------------------------------------------------
// Microsoft Edge (installed with Windows): no browser download needed.
const browser = await chromium.launch({ channel: process.env.PW_CHANNEL ?? "msedge" });

async function signIn(context, phone) {
  const page = await context.newPage();
  await page.goto(`${base}/connexion`);
  await page.fill("input[type=tel]", phone);
  await page.locator("input[type=tel]").press("Enter");
  await page.waitForURL(/connexion\/code/, { timeout: 120000 });
  await page.fill("input[name=Code]", await codeFor(phone));
  await page.locator("input[name=Code]").press("Enter");
  await page.waitForURL((url) => !url.pathname.endsWith("/connexion/code"), { timeout: 120000 });
  // Staff accounts only confirm their name and the privacy policy.
  if (page.url().includes("/inscription")) {
    const name = page.locator("input[name=FullName]");
    if (!(await name.inputValue())) await name.fill("Fatou Sow");
    await page.check("input[name=AcceptsPrivacyPolicy]");
    await name.press("Enter");
    await page.waitForURL((url) => !url.pathname.startsWith("/inscription"), { timeout: 120000 });
  }
  return page;
}

async function signUp(context, profile, name, company) {
  const page = await context.newPage();
  const phone = randomPhone();
  await page.goto(`${base}/connexion`);
  await page.fill("input[type=tel]", phone);
  await page.locator("input[type=tel]").press("Enter");
  await page.waitForURL(/connexion\/code/, { timeout: 120000 });
  await page.fill("input[name=Code]", await codeFor(phone));
  await page.locator("input[name=Code]").press("Enter");
  await page.waitForURL((url) => !url.pathname.endsWith("/connexion/code"), { timeout: 120000 });
  if (!page.url().includes("/inscription")) await page.goto(`${base}/inscription`);
  if (!page.url().includes("/inscription")) throw new Error("Not on registration: " + page.url());
  await page.fill("input[name=FullName]", name);
  await page.check(`input[name=Profile][value=${profile}]`);
  if (company) await page.fill("input[name=CompanyName]", company);
  await page.check("input[name=AcceptsPrivacyPolicy]");
  await page.locator("input[name=FullName]").press("Enter");
  await page.waitForURL((url) => !url.pathname.startsWith("/inscription"), { timeout: 120000 });
  return page;
}

async function addPlace(page, kind, name, landmark) {
  await page.goto(`${base}/logements/nouveau`);
  const id = await page.evaluate(async () => {
    const html = await (await fetch("/quartiers/recherche?q=almadies&field=NeighborhoodId")).text();
    return html.match(/data-pick-id="([^"]+)"/)?.[1];
  });
  await page.check(`input[name=Kind][value=${kind}]`, { force: true });
  await page.fill("input[name=Name]", name);
  await page.evaluate((v) => (document.getElementById("NeighborhoodId").value = v), id);
  await page.fill("textarea[name=Landmark]", landmark);
  await page.click("form[enctype] button[type=submit]");
  await page.waitForLoadState("networkidle");
}

async function activate(page) {
  await page.goto(`${base}/abonnement`);
  const simulate = page.locator("form[action*='activer-simulation'] button");
  if (await simulate.count()) {
    await simulate.first().click();
    await page.waitForLoadState("networkidle");
  }
}

async function report(page, text) {
  await page.goto(`${base}/signaler`);
  await page.evaluate((description) => {
    const form = document.querySelector("form[data-report-wizard]");
    form.querySelector("input[name=Domain][value=Plumbing]").checked = true;
    form.querySelector("textarea[name=Description]").value = description;
    form.querySelector("input[name=Urgency][value=High]").checked = true;
    form.querySelectorAll("fieldset[data-step]").forEach((f) => (f.hidden = false));
    HTMLFormElement.prototype.submit.call(form);
  }, text);
  await page.waitForLoadState("networkidle");
}

// Accounts: an empty company (start-up checklist), a company with data, a household.
// Service workers off: the old version reloaded on the first SW activation and cancelled form posts.
const sw = { serviceWorkers: "block" };
const setup = await browser.newContext(sw);
const emptyCompany = await signUp(setup, "Business", "Omar Diop", "Diop Services");
const emptyState = await emptyCompany.context().storageState();

const companyContext = await browser.newContext(sw);
const company = await signUp(companyContext, "Business", "Aïssatou Ndiaye", "Teranga Logistique");
await addPlace(company, "Office", "Siège Plateau", "Face à la BICIS, 2e étage");
await addPlace(company, "Warehouse", "Dépôt Rufisque", "Derrière la station Total");
await company.goto(`${base}/entreprise/offre`);
await company.click("form:has(input[value=PROFESSIONNEL]) button[type=submit]");
await company.waitForLoadState("networkidle");
await activate(company);
await report(company, "Fuite d'eau sous le lavabo des toilettes du rez-de-chaussée");
await report(company, "Climatiseur de la salle de réunion en panne");
const companyState = await companyContext.storageState();

const homeContext = await browser.newContext(sw);
const home = await signUp(homeContext, "Household", "Mariama Ba", null);
await addPlace(home, "Villa", "Villa de Ngor", "Derrière la mosquée, portail vert");
await activate(home);
await report(home, "Le robinet de la cuisine goutte depuis hier");
const homeState = await homeContext.storageState();

// Links found on list pages (ids are generated at each run).
await company.goto(`${base}/interventions`);
const companyRequests = await company.evaluate(() => [...document.querySelectorAll("a[href]")].map((a) => a.getAttribute("href")));
await home.goto(`${base}/logements`);
const homeProperties = await home.evaluate(() => [...document.querySelectorAll("a[href]")].map((a) => a.getAttribute("href")));
function firstLink(links, pattern) { return links.find((l) => pattern.test(l)) ?? "/"; }

// A mission for the demo technician: slots proposed, then assigned (development simulator).
await company.goto(base + firstLink(companyRequests, /^\/interventions\/[0-9a-f-]{36}$/));
for (const step of ["ProposeSlots", "Assign"]) {
  await company.click(`form[action$='/simulation/${step}'] button`);
  await company.waitForLoadState("networkidle");
}

const technicianContext = await browser.newContext(sw);
const technician = await signIn(technicianContext, technicianPhone);
await technician.goto(`${base}/technicien`);
const missions = await technician.evaluate(() => [...document.querySelectorAll("a[href]")].map((a) => a.getAttribute("href")));
const technicianState = await technicianContext.storageState();

const adminContext = await browser.newContext(sw);
const admin = await signIn(adminContext, adminPhone);
await admin.goto(`${base}/admin/dispatch`);
const dispatchLinks = await admin.evaluate(() => [...document.querySelectorAll("a[href]")].map((a) => a.getAttribute("href")));
const adminState = await adminContext.storageState();

// ---- Captures ----------------------------------------------------------------------
const screens = [
  { name: "accueil-entreprise-vide", path: "/espace", state: emptyState },
  { name: "accueil-entreprise", path: "/espace", state: companyState },
  { name: "sites", path: "/entreprise/sites", state: companyState },
  { name: "equipe", path: "/entreprise/equipe", state: companyState },
  { name: "factures", path: "/abonnement", state: companyState },
  { name: "signaler", path: "/signaler", state: companyState },
  { name: "accueil-maison", path: "/espace", state: homeState },
  // Public home: the welcome screens were already seen on this device.
  { name: "accueil-public", path: "/", state: { cookies: [{ name: "sc_bienvenue", value: "1", url: base }], origins: [] } },
  { name: "connexion", path: "/connexion", state: undefined },
  { name: "onboarding", path: "/bienvenue", state: undefined },
  { name: "demande", path: firstLink(companyRequests, /^\/interventions\/[0-9a-f-]{36}$/), state: companyState },
  { name: "offre", path: "/entreprise/offre", state: companyState },
  { name: "diagnostics", path: "/entreprise/diagnostics", state: companyState },
  { name: "nouveau-site", path: "/logements/nouveau", state: companyState },
  { name: "compte", path: "/compte", state: homeState },
  { name: "logement", path: firstLink(homeProperties, /^\/logements\/[0-9a-f-]{36}$/), state: homeState },
  { name: "technicien", path: "/technicien", state: technicianState },
  { name: "technicien-mission", path: firstLink(missions, /^\/technicien\/missions\/[0-9a-f-]{36}$/), state: technicianState },
  { name: "admin", path: "/admin", state: adminState },
  { name: "admin-dispatch", path: "/admin/dispatch", state: adminState },
  { name: "admin-demande", path: firstLink(dispatchLinks, /^\/admin\/dispatch\/[0-9a-f-]{36}$/), state: adminState },
  { name: "admin-clients", path: "/admin/clients", state: adminState },
  { name: "admin-techniciens", path: "/admin/techniciens", state: adminState },
  { name: "admin-paiements", path: "/admin/paiements", state: adminState },
  { name: "admin-offres", path: "/admin/offres", state: adminState },
];
// --only=name1,name2 recaptures a subset.
const only = process.argv.find((a) => a.startsWith("--only="))?.slice(7).split(",");
const viewports = [
  { tag: "1440", width: 1440, height: 900 },
  { tag: "390", width: 390, height: 844, isMobile: true },
];

for (const scheme of ["light", "dark"]) {
  for (const viewport of viewports) {
    for (const screen of screens.filter((x) => !only || only.includes(x.name))) {
      const context = await browser.newContext({
        viewport: { width: viewport.width, height: viewport.height },
        isMobile: viewport.isMobile ?? false,
        deviceScaleFactor: 1,
        colorScheme: scheme,
        storageState: screen.state,
        ...sw,
      });
      // No guided tour, no install banner: we review the screen itself.
      await context.addInitScript(() => {
        try { localStorage.setItem("sc-install-later", String(Date.now())); } catch { /* ignore */ }
      });
      const page = await context.newPage();
      await page.goto(base + screen.path, { waitUntil: "networkidle" });
      // The guided tour overlay (z-60…62) would hide the screen.
      await page.evaluate(() => document.querySelectorAll("body > .fixed").forEach((e) => {
        if (/z-\[6[0-2]\]/.test(e.className)) e.remove();
      }));
      // Full-page shots do not scroll: load the lazy images first.
      await page.evaluate(async () => {
        for (let y = 0; y < document.body.scrollHeight; y += window.innerHeight) {
          window.scrollTo(0, y);
          await new Promise((r) => setTimeout(r, 120));
        }
        await Promise.all([...document.images].map((i) => (i.complete ? null : new Promise((r) => { i.onload = i.onerror = r; }))));
        window.scrollTo(0, 0);
      });
      await page.screenshot({ path: join(out, `${screen.name}-${viewport.tag}-${scheme}.png`), fullPage: true });
      await context.close();
    }
  }
}

await browser.close();
stop();
console.log(`Captures written to ${out}`);
process.exit(0);
