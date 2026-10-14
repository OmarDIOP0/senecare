import { chromium } from "playwright";
const base = "http://localhost:5199";
const browser = await chromium.launch({ channel: "msedge" });
const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const bad = [];
ctx.on("response", (r) => { if (r.status() >= 400 && !r.url().includes("tile.openstreetmap")) bad.push(`${r.status()} ${r.request().method()} ${r.url()}`); });
const page = await ctx.newPage();
const step = async (name, fn) => { try { await fn(); console.log("ok   ", name, "→", page.url()); } catch (e) { console.log("FAIL ", name, e.message.split("\n")[0], "@", page.url()); } };
const phone = "77" + String(Math.floor(1_000_000 + Math.random() * 8_999_999));
const codeOnPage = async () => (await page.locator("p.border-dashed span.font-mono").innerText()).trim();

await step("home quick form → connexion", async () => {
  await ctx.addCookies([{ name: "sc_bienvenue", value: "1", url: base }]);
  await page.goto(base + "/");
  await page.fill("#qf-telephone", phone);
  await page.locator("#qf-telephone").press("Enter");
  await page.waitForURL(/connexion/);
});
await step("connexion → code", async () => {
  await page.click("form button[type=submit]");
  await page.waitForURL(/connexion\/code/);
});
await step("code → inscription", async () => {
  await page.keyboard.type(await codeOnPage());
  await page.waitForURL(/inscription/);
});
await step("back button then resubmit code (stale)", async () => {
  await page.goBack();
  await page.waitForLoadState();
});
await step("inscription full submit", async () => {
  await page.goto(base + "/inscription");
  await page.evaluate(() => {
    const f = document.querySelector("form[data-wizard]");
    f.querySelector("input[name=Profile][value=Household]").checked = true;
    f.querySelector("#FullName").value = "Test Flux";
    f.querySelector("#Property_Name").value = "Maison test";
    document.getElementById("Property_Latitude").value = "14.7471";
    document.getElementById("Property_Longitude").value = "-17.4983";
    f.querySelector("#AcceptsPrivacyPolicy").checked = true;
  });
  const id = await page.evaluate(async () => (await (await fetch("/quartiers/recherche?q=almadies&field=x")).text()).match(/data-pick-id="([^"]+)"/)[1]);
  await page.evaluate((v) => { document.getElementById("Property_NeighborhoodId").value = v; HTMLFormElement.prototype.submit.call(document.querySelector("form[data-wizard]")); }, id);
  await page.waitForURL(/abonnement/);
});
await step("declare payment", async () => {
  const form = page.locator("form[action*='/paiement']");
  if (await form.count()) { await form.locator("button[type=submit]").click(); await page.waitForLoadState(); }
});
await step("navigate space pages", async () => {
  for (const p of ["/", "/logements", "/interventions", "/notifications", "/compte", "/signaler", "/logements/nouveau"]) await page.goto(base + p);
});
await step("logout", async () => {
  await page.goto(base + "/compte");
  await page.locator("form[action='/deconnexion'] button").first().click({ force: true });
  await page.waitForLoadState();
});
await step("login again existing account", async () => {
  await page.goto(base + "/connexion");
  await page.fill("input[type=tel]", phone);
  await page.click("form button[type=submit]");
  await page.waitForURL(/code/);
  await page.keyboard.type(await codeOnPage());
  await page.waitForURL((u) => !u.pathname.includes("/code"));
});
await step("signup start with existing number", async () => {
  const p2 = await ctx.newPage();
  await p2.goto(base + "/inscription");
});
console.log("\nHTTP >= 400:\n" + (bad.join("\n") || "none"));
await browser.close();
