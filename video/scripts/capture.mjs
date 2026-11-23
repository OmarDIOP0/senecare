// Captures the real app screens of an episode (Playwright, 390×844 at scale 3) with the boxes of the elements the
// video taps or zooms on.
//   npm run capture:ep -- 01
// Builds and starts SeneCare locally (Development, password sign-in) on a throwaway database « senecare_video »
// (dropped at the end), creates fictional demo people, plays the whole journey, and writes
// public/ep01/screens/*.png + public/ep01/screens.json. No real person, key or token ever appears.
import { spawn, execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { chromium } from "playwright";

const episode = process.argv[2] ?? "01";
const root = resolve(import.meta.dirname, "..", "..");
const webDir = join(root, "src", "SeneCare.Web");
const out = resolve(import.meta.dirname, "..", "public", `ep${episode}`, "screens");
mkdirSync(out, { recursive: true });

const port = 5320;
const base = `http://localhost:${port}`;
const database = "senecare_video";
const connection = `Server=localhost;Database=${database};Trusted_Connection=True;TrustServerCertificate=True`;
const sql = (query) => execFileSync("sqlcmd", ["-S", "localhost", "-E", "-C", "-I", "-b", "-Q", query], { stdio: "pipe" });

// ---- Build and start -------------------------------------------------------------------------------------------
const build = join(tmpdir(), "senecare-video-build");
console.log("Build…");
execFileSync("dotnet", ["build", join(webDir, "SeneCare.Web.csproj"), "--artifacts-path", build, "-v", "q", "-nologo"], { stdio: "inherit" });
sql(`IF DB_ID('${database}') IS NOT NULL BEGIN ALTER DATABASE [${database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${database}]; END`);

// Fictional people only (numbers in the 70 range are not handed to real customers in this demo).
const adminPhone = "700000090";
const adminPassword = "Demo-Admin-2026";
const customerPhone = "771234567";
const customerPassword = "Awa-Demo-2026";

const log = [];
const server = spawn("dotnet", [join(build, "bin", "SeneCare.Web", "debug", "SeneCare.Web.dll"), "--urls", base], {
  cwd: webDir,
  env: {
    ...process.env, ASPNETCORE_ENVIRONMENT: "Development", ASPNETCORE_CONTENTROOT: webDir,
    ConnectionStrings__Default: connection, Auth__Mode: "Password",
    Bootstrap__SuperAdminPhones: adminPhone, Bootstrap__SuperAdminPassword: adminPassword,
    RateLimits__Otp__PermitLimit: "1000",
  },
});
server.stdout.on("data", (d) => log.push(d.toString()));
server.stderr.on("data", (d) => log.push(d.toString()));
let finished = false;
const stop = () => {
  try { server.kill(); } catch { /* already gone */ }
};
process.on("exit", stop);
process.on("uncaughtException", (e) => {
  console.error(e);
  console.error(log.join("").split("\n").filter((l) => /WRN|ERR|FTL/.test(l)).slice(-15).join("\n"));
  stop();
  process.exit(1);
});

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
for (let i = 0; i < 300 && !log.join("").includes("Now listening"); i++) await sleep(1000);
if (!log.join("").includes("Now listening")) throw new Error("Server did not start:\n" + log.join("").slice(-2000));
console.log("Server up.");

// ---- Browser ----------------------------------------------------------------------------------------------------
const browser = await chromium.launch({ channel: process.env.PW_CHANNEL ?? "msedge" });
const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true, colorScheme: "light", locale: "fr-FR", serviceWorkers: "block" };
// Development helpers never on screen; no install banner, no caret blinking in the captures.
const hideDev = `section[aria-label="Simulateur"], [data-install], .sc-install { display: none !important; } * { caret-color: transparent !important; }`;

const submitForm = (page, selector, values) =>
  page.evaluate(({ selector, values }) => {
    const form = document.querySelector(selector);
    for (const [name, value] of Object.entries(values)) {
      const fields = form.querySelectorAll(`[name="${name}"]`);
      if (fields.length && (fields[0].type === "radio" || fields[0].type === "checkbox")) {
        fields.forEach((f) => (f.checked = String(f.value) === String(value)));
      } else if (fields.length) {
        fields[0].value = value;
      } else {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = value;
        form.append(input);
      }
    }
    HTMLFormElement.prototype.submit.call(form);
  }, { selector, values });

async function settle(page) {
  await page.waitForLoadState("networkidle");
  await page.addStyleTag({ content: hideDev });
  // The guided tour of the space: skipped.
  const skip = page.locator("[data-skip]");
  if (await skip.count() && await skip.first().isVisible()) {
    await skip.first().click();
    await sleep(300);
  }
  await page.evaluate(() => document.fonts.ready);
  await sleep(400);
}

async function signIn(context, number, password) {
  const page = await context.newPage();
  await page.goto(`${base}/connexion`);
  await page.fill("input[type=tel]", number);
  await page.fill("input[name=Password]", password);
  await page.locator("input[name=Password]").press("Enter");
  await page.waitForLoadState("networkidle");
  return page;
}

// ---- Admin: first sign-in, its own password, name + consent, Wave link ------------------------------------------
const adminContext = await browser.newContext({ serviceWorkers: "block", locale: "fr-FR" });
const admin = await signIn(adminContext, adminPhone, adminPassword);
if (await admin.locator("input[name=NewPassword]").count()) {
  if (await admin.locator("input[name=CurrentPassword]").count()) await admin.fill("input[name=CurrentPassword]", adminPassword);
  await admin.fill("input[name=NewPassword]", "Demo-Admin-2027");
  await admin.fill("input[name=ConfirmPassword]", "Demo-Admin-2027");
  await admin.locator("input[name=ConfirmPassword]").press("Enter");
  await admin.waitForLoadState("networkidle");
}
console.log("  admin after sign-in:", new URL(admin.url()).pathname);
await admin.goto(`${base}/inscription`);
if (await admin.locator("form:has(input[name=FullName])").count()) {
  await submitForm(admin, "form:has(input[name=FullName])", { FullName: "Fatou Sow", AcceptsPrivacyPolicy: "true" });
  await admin.waitForLoadState("networkidle");
}
console.log("  admin after registration:", new URL(admin.url()).pathname);
await admin.goto(`${base}/admin/paiements`);
await admin.fill("input#link", "https://pay.wave.com/m/M_sn_demo/c/sn/?amount={montant}");
await admin.click("form[action*='lien-wave'] button[type=submit]");
await admin.waitForLoadState("networkidle");

// ---- Customer: Awa Ndiaye signs up, adds « Villa Ouakam », pays, the admin verifies ---------------------------
const customerContext = await browser.newContext(phone);
const awa = await customerContext.newPage();
await awa.goto(`${base}/inscription`);
await submitForm(awa, "form[data-wizard]", { Profile: "Household", Phone: customerPhone, Password: customerPassword, ConfirmPassword: customerPassword });
await awa.waitForURL((url) => url.pathname.startsWith("/inscription"), { timeout: 60000 });
await awa.waitForLoadState("networkidle");
await submitForm(awa, "form[data-wizard]", { FullName: "Awa Ndiaye", Profile: "Household", PlanCode: "HOME", Annual: "false", AcceptsPrivacyPolicy: "true", AcceptsWhatsApp: "true" });
await awa.waitForURL((url) => !url.pathname.startsWith("/inscription"), { timeout: 60000 });

await awa.goto(`${base}/logements/nouveau`);
const ouakam = await awa.evaluate(async () => {
  const html = await (await fetch("/quartiers/recherche?q=ouakam&field=NeighborhoodId")).text();
  return html.match(/data-pick-id="([^"]+)"/)?.[1];
});
await awa.check("input[name=Kind][value=Villa]", { force: true });
await awa.fill("input[name=Name]", "Villa Ouakam");
await awa.evaluate((v) => {
  document.getElementById("NeighborhoodId").value = v;
  document.getElementById("Latitude").value = "14.7225";
  document.getElementById("Longitude").value = "-17.4880";
}, ouakam);
await awa.click("form[enctype] button[type=submit]");
await awa.waitForLoadState("networkidle");

await awa.goto(`${base}/abonnement`);
await awa.locator("form[action*='/paiement'] input[name=reference]").first().fill("WAVE-DEMO-0412");
await awa.locator("form[action*='/paiement'] button[type=submit]").first().click();
await awa.waitForLoadState("networkidle");
for (let i = 0; i < 3; i++) {
  await admin.goto(`${base}/admin/paiements`);
  const verify = admin.locator("form[action*='/valider'] button");
  if (!(await verify.count())) break;
  await verify.first().click();
  await admin.waitForLoadState("networkidle");
}

// ---- Captures ---------------------------------------------------------------------------------------------------
const screens = {};
async function box(page, selector) {
  const element = page.locator(selector).first();
  const b = await element.boundingBox();
  if (!b) throw new Error("No box for " + selector);
  return { x: Math.round(b.x), y: Math.round(b.y), w: Math.round(b.width), h: Math.round(b.height) };
}
async function shot(page, name, targets = {}, scrollTo = null) {
  if (scrollTo) {
    await page.locator(scrollTo).first().evaluate((el) => el.scrollIntoView({ block: "center" }));
    await sleep(500);
  }
  const boxes = {};
  for (const [key, selector] of Object.entries(targets)) boxes[key] = await box(page, selector);
  await page.screenshot({ path: join(out, `${name}.png`) });
  screens[name] = { file: `ep${episode}/screens/${name}.png`, boxes };
  console.log("  ✓", name);
}

// 1. The space: « Signaler un problème ».
await awa.goto(`${base}/espace`);
await settle(awa);
// The orange « Signaler » of the bottom tab bar (last link to /signaler in the page).
await shot(awa, "espace", { report: "a[href^='/signaler'] >> visible=true >> nth=-1" });

// 2. The report form, step by step.
await awa.goto(`${base}/signaler`);
await settle(awa);
await awa.evaluate(() => localStorage.clear());
await shot(awa, "report-trade", { plumbing: "label:has(input[name=Domain][value=Plumbing])" });
// Ticked without the change event: the form would move on to step 2 at once.
await awa.evaluate(() => (document.querySelector("input[name=Domain][value=Plumbing]").checked = true));
await sleep(300);
await shot(awa, "report-trade-chosen", { plumbing: "label:has(input[name=Domain][value=Plumbing])", next: "[data-step-next]" });
await awa.click("[data-step-next]");
await sleep(500);
await shot(awa, "report-describe", { description: "textarea[name=Description]" }, "textarea[name=Description]");
await awa.fill("textarea[name=Description]", "Fuite sous l'évier de la cuisine, l'eau coule dans le placard.");
const photo = resolve(import.meta.dirname, "..", "assets", "fuite-evier.jpg");
if (!existsSync(photo)) execFileSync("python", ["-I", resolve(import.meta.dirname, "make_photo.py"), photo], { stdio: "inherit" });
await awa.locator("input[type=file]").first().setInputFiles(photo);
await sleep(2500); // client-side compression
await shot(awa, "report-described", { description: "textarea[name=Description]", photo: "input[type=file] >> xpath=ancestor::*[self::div or self::label][1]", next: "[data-step-next]" }, "textarea[name=Description]");
await awa.click("[data-step-next]");
await sleep(500);
await shot(awa, "report-urgency", { critical: "label:has(input[name=Urgency][value=Critical])" }, "input[name=Urgency][value=Critical]");
await awa.locator("label:has(input[name=Urgency][value=Critical])").click();
await sleep(300);
await shot(awa, "report-urgency-chosen", { critical: "label:has(input[name=Urgency][value=Critical])" }, "input[name=Urgency][value=Critical]");
await shot(awa, "report-send", { send: "[data-step-submit]" }, "[data-step-submit]");
await awa.click("[data-step-submit]");
await awa.waitForURL(/\/interventions\/[0-9a-f-]{36}/, { timeout: 60000 });
const requestUrl = awa.url();
await settle(awa);
await shot(awa, "sent");

// Development simulator, never shown: plays SENECARE's dispatcher and technician.
async function simulate(step) {
  await awa.goto(requestUrl);
  await awa.evaluate(async (step) => {
    const form = document.querySelector(`form[action$='/simulation/${step}']`);
    await fetch(form.action, { method: "POST", body: new FormData(form), credentials: "same-origin" });
  }, step);
}

// 3. Slots proposed: the customer picks the second one.
await simulate("ProposeSlots");
await awa.goto(requestUrl);
await settle(awa);
await shot(awa, "slots", { slot: "label:has(input[name=slot]) >> nth=1", confirm: "form:has(input[name=slot]) button[type=submit]" }, "#creneau-titre");
await awa.locator("label:has(input[name=slot]) >> nth=1").click();
await sleep(300);
await shot(awa, "slots-chosen", { slot: "label:has(input[name=slot]) >> nth=1", confirm: "form:has(input[name=slot]) button[type=submit]" }, "#creneau-titre");
await awa.click("form:has(input[name=slot]) button[type=submit]");
await awa.waitForLoadState("networkidle");

// 4. The technician is assigned and on the way (the demo technician gets a real-sounding fictional name).
await simulate("Assign");
sql(`UPDATE [${database}].dbo.users SET full_name = N'Ibrahima Ndoye' WHERE full_name LIKE N'Moussa Diallo%'`);
await simulate("OnTheWay");
await awa.goto(requestUrl);
await settle(awa);
await sleep(2500); // map tiles
await shot(awa, "on-the-way", { status: "[data-tracking]", technician: "#tech-titre" }, "[data-tracking]");
await shot(awa, "technician", { technician: "#tech-titre" }, "#tech-titre");

// 5. Work done: validate and rate.
await simulate("StartWork");
await simulate("Complete");
await awa.goto(requestUrl);
await settle(awa);
await shot(awa, "completed", { five: "label:has(input[name=score][value='5'])" }, "label:has(input[name=score][value='5'])");

// The space, for the outro.
await awa.goto(`${base}/espace`);
await settle(awa);
await shot(awa, "espace-after");

writeFileSync(resolve(out, "..", "screens.json"), JSON.stringify({ viewport: { width: 390, height: 844 }, screens }, null, 2));
await browser.close();
finished = true;
stop();
await sleep(1500);
sql(`IF DB_ID('${database}') IS NOT NULL BEGIN ALTER DATABASE [${database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${database}]; END`);
console.log(`Done: ${Object.keys(screens).length} screens in ${out}`);
process.exit(finished ? 0 : 1);
