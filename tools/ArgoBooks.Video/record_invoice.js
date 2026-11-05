// Records the free invoice generator on argorobots.com as a take, the way Takes.cs records the app: a hand moves, clicks
// and types through the page while every change is saved. The take keeps its own clock, so it plays at an even speed
// however long the browser takes to draw each change. invoice_short.py turns it into the take format the edit reads.
// Usage: node record_invoice.js OUT_FOLDER   (run "npm install" in this folder first)
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const PAGE = "https://argorobots.com/invoice-generator/";
const SCALE = 2;
// Taller than the app's window, so the whole width of the invoice fits across the tall frame of a Short.
const VIEWPORT = { width: 1600, height: 1000 };

class Take {
  constructor(page, folder) {
    this.page = page;
    this.folder = folder;
    this.t = 0;
    this.shots = [];
    this.path = [{ t: 0, x: 1100, y: 300, down: 0 }];
    this.marks = {};
    this.events = [];
    this.places = {};
  }

  get cursor() {
    const last = this.path[this.path.length - 1];
    return { x: last.x, y: last.y };
  }

  async snap() {
    const file = String(this.shots.length).padStart(4, "0") + ".png";
    await this.page.screenshot({ path: path.join(this.folder, "frames", file) });
    this.shots.push({ t: round(this.t), file });
  }

  mark(name) {
    this.marks[name] = round(this.t);
  }

  hold(seconds) {
    this.t += seconds;
    this.path.push({ t: round(this.t), ...this.cursor, down: 0 });
  }

  async moveTo(x, y, seconds) {
    const from = this.cursor;
    const steps = Math.max(1, Math.round(seconds * 30));
    for (let i = 1; i <= steps; i++) {
      const p = i / steps;
      const q = p * p * (3 - 2 * p);
      this.path.push({ t: round(this.t + seconds * p), x: from.x + (x - from.x) * q, y: from.y + (y - from.y) * q, down: 0 });
    }
    this.t += seconds;
    await this.page.mouse.move(x, y);
    await this.snap();
  }

  // A select's list is drawn by the system, which a screenshot leaves out, so a template is chosen without opening it.
  async click(real = true) {
    const { x, y } = this.cursor;
    this.events.push({ t: round(this.t), kind: "click" });
    this.path.push({ t: round(this.t), x, y, down: 1 });
    if (real) await this.page.mouse.click(x, y);
    this.t += 0.12;
    this.path.push({ t: round(this.t), x, y, down: 0 });
    await this.snap();
  }

  async type(text, perKey) {
    for (const key of text) {
      if (key === "\n") await this.page.keyboard.press("Enter");
      else await this.page.keyboard.type(key);
      this.t += perKey;
      await this.snap();
    }
    this.hold(0);
  }

  // Replaces what a number box holds, as someone selecting the 0 already in it and typing over it would.
  async retype(text, perKey) {
    await this.page.keyboard.press("Control+A");
    await this.type(text, perKey);
  }

  async scrollTo(top, seconds) {
    const from = await this.page.evaluate(() => window.scrollY);
    const steps = Math.max(1, Math.round(seconds * 30));
    for (let i = 1; i <= steps; i++) {
      const p = i / steps;
      const q = p * p * (3 - 2 * p);
      await this.page.evaluate((y) => window.scrollTo({ top: y, behavior: "instant" }), from + (top - from) * q);
      this.t += seconds / steps;
      await this.snap();
    }
    this.hold(0);
  }

  // Where a part of the page is now, in the take's pixels, kept for the edit's camera.
  async place(name, selector, nth = 0) {
    const box = await this.page.locator(selector).nth(nth).boundingBox();
    this.places[name] = [box.x, box.y, box.width, box.height].map((v) => Math.round(v * SCALE));
    return box;
  }

  // A heading's box spans the whole column, so the words themselves are measured instead.
  async placeText(name, selector) {
    const box = await this.page.locator(selector).first().evaluate((element) => {
      const range = document.createRange();
      range.selectNodeContents(element);
      const r = range.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    });
    this.places[name] = [box.x, box.y, box.width, box.height].map((v) => Math.round(v * SCALE));
  }

  async moveInto(name, selector, nth = 0, seconds = 0.6, dx = null) {
    const box = await this.place(name, selector, nth);
    await this.moveTo(box.x + (dx ?? box.width / 2), box.y + box.height / 2, seconds);
    return box;
  }
}

function round(t) {
  return Math.round(t * 10000) / 10000;
}

async function record(page, take) {
  await take.snap();
  take.mark("top");
  await take.placeText("hero_title", ".site-hero-title");
  await take.placeText("tagline", ".site-hero-tagline");
  take.hold(0.8);
  await take.moveTo(930, 268, 1.8);
  take.hold(1.7);
  take.mark("scroll");
  await take.scrollTo(400, 0.9);
  take.hold(0.3);

  await take.place("logo", "#logo-slot");
  await take.moveInto("business", '[data-label="businessTitle"]', 0, 0.8, 40);
  take.mark("click_business");
  await take.click();
  await take.type("Cedar Ridge IT", 0.07);
  take.mark("business_done");
  take.hold(0.6);

  await take.moveInto("from", "#field-from", 0, 0.5, 60);
  await take.click();
  await take.type("Cedar Ridge IT\n18 Elm Street\nPortland, ME 04102", 0.03);
  take.mark("from_done");

  await take.moveInto("billto", "#field-billTo", 0, 0.7, 50);
  take.mark("click_billto");
  await take.click();
  await take.type("Harbor Dental\n42 Bay Street\nPortland, ME 04101", 0.03);
  take.mark("billto_done");
  take.hold(0.6);

  await take.scrollTo(640, 0.7);
  take.mark("items");
  await take.moveInto("desc", '[data-field="lineItem-description"]', 0, 0.6, 40);
  take.mark("click_desc");
  await take.click();
  await take.type("Network setup", 0.05);
  take.hold(0.2);
  await take.moveInto("rate", '[data-field="lineItem-rate"]', 0, 0.5);
  take.mark("click_rate");
  await take.click();
  await take.retype("450", 0.09);
  await take.place("amount", '[data-field="lineItem-amount"]');
  await take.place("total1", '[data-field="total"]');
  take.mark("rate_done");
  take.hold(0.8);

  await take.moveInto("add", '[data-action="add-line-item"]', 0, 0.6);
  take.mark("click_add");
  await take.click();
  take.hold(0.25);
  await take.moveInto("desc2", '[data-field="lineItem-description"]', 1, 0.4, 40);
  await take.click();
  await take.type("Laptop repair", 0.04);
  await take.moveInto("qty2", '[data-field="lineItem-quantity"]', 1, 0.4);
  take.mark("click_qty2");
  await take.click();
  await take.retype("2", 0.1);
  await take.moveInto("rate2", '[data-field="lineItem-rate"]', 1, 0.4);
  await take.click();
  await take.retype("85", 0.09);
  take.mark("second_done");
  await take.place("subtotal", '[data-field="subtotal"]');
  await take.place("totals", ".invoice-totals");
  const total = await take.place("total", '[data-field="total"]');
  await take.moveTo(total.x + total.width / 2 + 10, total.y + total.height / 2 + 20, 0.8);
  take.mark("look_total");
  take.hold(1.6);

  await take.scrollTo(360, 0.7);
  take.mark("templates");
  await take.place("invoice", "main.invoice");
  await take.place("share", "#copy-share-link");
  await take.moveInto("template", 'select[data-field="template"]', 0, 0.7);
  for (const name of ["elegant", "ribbon", "modern"]) {
    take.mark("click_" + name);
    await take.click(false);
    await page.selectOption('select[data-field="template"]', name);
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(300);
    await take.snap();
    take.mark(name);
    take.hold(0.65);
  }

  await take.moveInto("pdf", "#download-pdf", 0, 0.6);
  take.mark("click_pdf");
  const download = page.waitForEvent("download");
  await take.click();
  await (await download).saveAs(path.join(take.folder, "invoice.pdf"));
  take.mark("downloaded");
  take.hold(1.5);
  take.mark("end");
}

(async () => {
  const folder = process.argv[2];
  if (!folder) throw new Error("Usage: node record_invoice.js OUT_FOLDER");
  fs.rmSync(path.join(folder, "frames"), { recursive: true, force: true });
  fs.mkdirSync(path.join(folder, "frames"), { recursive: true });
  const browser = await chromium.launch({ executablePath: EDGE, headless: true });
  const context = await browser.newContext({
    viewport: VIEWPORT, deviceScaleFactor: SCALE, locale: "en-US", timezoneId: "America/New_York", acceptDownloads: true,
  });
  const page = await context.newPage();
  // The recording would otherwise count as visits and downloads in the site's own tool statistics.
  await page.route("**/api/invoice-generator/track.php", (route) => route.abort());
  await page.goto(PAGE, { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);
  const take = new Take(page, folder);
  await record(page, take);
  const session = { size: [VIEWPORT.width * SCALE, VIEWPORT.height * SCALE], scale: SCALE, shots: take.shots, path: take.path, marks: take.marks, events: take.events, places: take.places };
  fs.writeFileSync(path.join(folder, "invoice_session.json"), JSON.stringify(session, null, 1));
  console.log(`${take.shots.length} pictures over ${take.t.toFixed(1)} s; marks: ${Object.entries(take.marks).map(([k, v]) => `${k} ${v}`).join(", ")}`);
  await browser.close();
})();

// jsPDF saves through a blank tab, and Playwright rejects that tab's first navigation once the browser closes.
process.on("unhandledRejection", (error) => {
  if (error.name !== "TargetClosedError") throw error;
});
