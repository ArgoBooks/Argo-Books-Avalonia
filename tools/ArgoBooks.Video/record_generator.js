// Records argorobots.com's free invoice or estimate generator as a take, the way Takes.cs records the app: a hand moves,
// clicks and types through the page while every change is saved. The take keeps its own clock, so it plays at an even
// speed however long the browser takes to draw each change. invoice_short.py turns it into the take format the edit reads.
// Usage: node record_generator.js invoice|estimate|taxcalc|mileage|hourly OUT_FOLDER   (run "npm install" in this folder first)
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PAGES = {
  invoice: "https://argorobots.com/invoice-generator/",
  estimate: "https://argorobots.com/estimate-generator/",
  taxcalc: "https://argorobots.com/self-employed-tax-calculator/",
  mileage: "https://argorobots.com/mileage-deduction-calculator/",
  hourly: "https://argorobots.com/hourly-rate-calculator/",
};
const SCALE = 2;
// Taller than the app's window, so the whole width of the invoice fits across the tall frame of a Short. The tax calculator
// goes into a 16:9 video beside the app's takes, so it is recorded at the app's own window size.
const VIEWPORTS = { invoice: { width: 1600, height: 1000 }, estimate: { width: 1600, height: 1000 }, taxcalc: { width: 1600, height: 900 }, mileage: { width: 1600, height: 900 }, hourly: { width: 1600, height: 1000 } };

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

// The page's title and its promise, held for as long as the Short's first line takes, then down to the document itself.
async function opening(take, linger = 1.7) {
  await take.snap();
  take.mark("top");
  await take.placeText("hero_title", ".site-hero-title");
  await take.placeText("tagline", ".site-hero-tagline");
  take.hold(0.8);
  await take.moveTo(930, 268, 1.8);
  take.hold(linger);
  take.mark("scroll");
  await take.scrollTo(400, 0.9);
  take.hold(0.3);
}

// The business's name, its address (typed off camera, so the PDF is complete), and the client's.
async function parties(take, business, from, client) {
  await take.place("logo", "#logo-slot");
  await take.moveInto("business", '[data-label="businessTitle"]', 0, 0.8, 40);
  take.mark("click_business");
  await take.click();
  await take.type(business, 0.07);
  take.mark("business_done");
  take.hold(0.6);

  await take.moveInto("from", "#field-from", 0, 0.5, 60);
  await take.click();
  await take.type(from, 0.03);
  take.mark("from_done");

  await take.moveInto("billto", "#field-billTo", 0, 0.7, 50);
  take.mark("click_billto");
  await take.click();
  await take.type(client, 0.03);
  take.mark("billto_done");
  take.hold(0.6);
}

async function firstLine(take, description, rate) {
  await take.scrollTo(640, 0.7);
  take.mark("items");
  await take.moveInto("desc", '[data-field="lineItem-description"]', 0, 0.6, 40);
  take.mark("click_desc");
  await take.click();
  await take.type(description, 0.05);
  take.hold(0.2);
  await take.moveInto("rate", '[data-field="lineItem-rate"]', 0, 0.5);
  take.mark("click_rate");
  await take.click();
  await take.retype(rate, 0.09);
  await take.place("amount", '[data-field="lineItem-amount"]');
  await take.place("total1", '[data-field="total"]');
  take.mark("rate_done");
  take.hold(0.8);
}

// A second line, whose quantity stays at 1 when none is given.
async function addLine(take, description, quantity, rate) {
  await take.moveInto("add", '[data-action="add-line-item"]', 0, 0.6);
  take.mark("click_add");
  await take.click();
  take.hold(0.25);
  await take.moveInto("desc2", '[data-field="lineItem-description"]', 1, 0.4, 40);
  await take.click();
  await take.type(description, 0.04);
  if (quantity) {
    await take.moveInto("qty2", '[data-field="lineItem-quantity"]', 1, 0.4);
    take.mark("click_qty2");
    await take.click();
    await take.retype(quantity, 0.1);
  }
  await take.moveInto("rate2", '[data-field="lineItem-rate"]', 1, 0.4);
  take.mark("click_rate2");
  await take.click();
  await take.retype(rate, 0.09);
  take.mark("second_done");
}

async function lookAtTotal(take) {
  await take.place("subtotal", '[data-field="subtotal"]');
  await take.place("totals", ".invoice-totals");
  const total = await take.place("total", '[data-field="total"]');
  await take.moveTo(total.x + total.width / 2 + 10, total.y + total.height / 2 + 20, 0.8);
  take.mark("look_total");
  take.hold(1.6);
}

// The lines an estimate's client signs to accept it, low in the window, so the mailing list form under them stays out of sight.
async function signature(take) {
  const button = '[data-action="toggle-signature"]';
  const box = await take.page.locator(button).boundingBox();
  await take.scrollTo(Math.round((await take.page.evaluate(() => window.scrollY)) + box.y - 820), 0.6);
  await take.moveInto("sign_button", button, 0, 0.6);
  take.mark("click_signature");
  await take.click();
  take.hold(0.3);
  await take.place("signature", "#signature-block");
  take.mark("signature_shown");
  take.hold(1.4);
}

async function templates(take, page, names) {
  await take.scrollTo(360, 0.7);
  take.mark("templates");
  await take.place("invoice", "main.invoice");
  await take.place("share", "#copy-share-link");
  await take.moveInto("template", 'select[data-field="template"]', 0, 0.7);
  for (const name of names) {
    take.mark("click_" + name);
    await take.click(false);
    await page.selectOption('select[data-field="template"]', name);
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(300);
    await take.snap();
    take.mark(name);
    take.hold(0.65);
  }
}

async function download(take, page, file) {
  await take.moveInto("pdf", "#download-pdf", 0, 0.6);
  take.mark("click_pdf");
  const saved = page.waitForEvent("download");
  await take.click();
  await (await saved).saveAs(path.join(take.folder, file));
  take.mark("downloaded");
  take.hold(1.5);
  take.mark("end");
}

const RECORDINGS = {
  async invoice(page, take) {
    await opening(take);
    await parties(take, "Cedar Ridge IT", "Cedar Ridge IT\n18 Elm Street\nPortland, ME 04102", "Harbor Dental\n42 Bay Street\nPortland, ME 04101");
    await firstLine(take, "Network setup", "450");
    await addLine(take, "Laptop repair", "2", "85");
    await lookAtTotal(take);
    await templates(take, page, ["elegant", "ribbon", "modern"]);
    await download(take, page, "invoice.pdf");
  },
  // The worked example of the video on setting money aside for tax: the US first, then Canada and Ontario.
  async taxcalc(page, take) {
    await take.scrollTo(250, 0.01);
    await take.snap();
    take.mark("top");
    await take.place("form", ".taxcalc-form");
    await take.place("results", "[data-tc-results]");
    take.hold(1.2);
    await take.moveInto("income", "#tc-income", 0, 0.9, 120);
    take.mark("click_income");
    await take.click();
    await take.type("90000", 0.11);
    take.mark("income_done");
    take.hold(0.5);
    await take.moveInto("expenses", "#tc-expenses", 0, 0.6, 120);
    take.mark("click_expenses");
    await take.click();
    await take.type("18000", 0.11);
    take.mark("expenses_done");
    await take.place("setaside", '[data-tc="setaside"]');
    await take.place("breakdown", ".taxcalc-breakdown");
    take.hold(0.6);
    const amount = await take.place("setaside", '[data-tc="setaside"]');
    await take.moveTo(amount.x + amount.width + 30, amount.y + amount.height / 2 + 20, 0.8);
    take.mark("look_us");
    take.hold(4.5);
    await take.moveInto("country", "#tc-country", 0, 0.8);
    take.mark("click_country");
    await take.click(false);
    await page.selectOption("#tc-country", "CA");
    await page.waitForTimeout(300);
    await take.snap();
    take.mark("canada");
    take.hold(0.8);
    await take.moveInto("province", "#tc-province", 0, 0.6);
    take.mark("click_province");
    await take.click(false);
    await page.selectOption("#tc-province", "ON");
    await page.waitForTimeout(300);
    await take.snap();
    take.mark("ontario");
    const canada = await take.place("setaside", '[data-tc="setaside"]');
    take.hold(0.4);
    await take.moveTo(canada.x + canada.width + 30, canada.y + canada.height / 2 + 20, 0.8);
    take.mark("look_ca");
    take.hold(5.0);
    take.mark("end");
  },
  // The tax deductions video's example: a year of US business driving either side of the IRS's July rate change, and what
  // the claim saves at a 30 percent rate.
  async mileage(page, take) {
    await take.scrollTo(250, 0.01);
    await take.snap();
    take.mark("top");
    await take.place("form", ".calc-form");
    await take.place("results", "[data-mc-results]");
    take.hold(1.2);
    await take.moveInto("first_half", "#mc-d0", 0, 0.9, 120);
    take.mark("click_first");
    await take.click();
    await take.type("4000", 0.12);
    take.mark("first_done");
    take.hold(0.6);
    await take.moveInto("second_half", "#mc-d1", 0, 0.6, 120);
    take.mark("click_second");
    await take.click();
    await take.type("4000", 0.12);
    take.mark("second_done");
    await take.place("bands", "[data-mc-bands]");
    take.hold(0.5);
    const deduction = await take.place("deduction", '[data-mc="deduction"]');
    await take.moveTo(deduction.x + deduction.width + 30, deduction.y + deduction.height / 2 + 20, 0.8);
    take.mark("look_deduction");
    take.hold(4.0);
    await take.moveInto("taxrate", "#mc-taxrate", 0, 0.8, 120);
    take.mark("click_rate");
    await take.click();
    await take.type("30", 0.14);
    take.mark("rate_done");
    await take.place("worth", "[data-mc-worth]");
    take.hold(0.5);
    const saved = await take.place("saved", '[data-mc="taxSaved"]');
    await take.moveTo(saved.x + saved.width + 30, saved.y + saved.height / 2 + 20, 0.8);
    take.mark("look_saved");
    take.hold(5.0);
    take.mark("end");
  },
  // The hourly rate Short's example: a $60,000 take-home with $12,000 of costs and 25 percent tax, at the page's own 40 hours,
  // 6 weeks off and 60 percent billable, then the quick sum the page compares it with.
  async hourly(page, take) {
    await take.scrollTo(120, 0.01);
    await take.snap();
    take.mark("top");
    await take.place("form", ".calc-form");
    await take.place("results", "[data-hr-results]");
    take.hold(1.0);
    await take.moveInto("income", "#hr-income", 0, 0.9, 120);
    take.mark("click_income");
    await take.click();
    await take.type("60000", 0.11);
    take.mark("income_done");
    take.hold(0.3);
    await take.moveInto("expenses", "#hr-expenses", 0, 0.5, 120);
    take.mark("click_expenses");
    await take.click();
    await take.type("12000", 0.11);
    take.hold(0.3);
    await take.moveInto("tax", "#hr-tax", 0, 0.5, 120);
    take.mark("click_tax");
    await take.click();
    await take.type("25", 0.14);
    take.mark("typed");
    take.hold(0.4);
    const rate = await take.place("rate", '[data-hr="rate"]');
    await take.moveTo(rate.x + rate.width + 30, rate.y + rate.height / 2 + 20, 0.8);
    take.mark("look_rate");
    take.hold(3.5);
    take.mark("end");
  },
  async estimate(page, take) {
    await opening(take, 3.0);
    await parties(take, "Northline Decks", "Northline Decks\n214 Harbor Road\nPortland, ME 04102", "Linda Park\n88 Maple Avenue\nPortland, ME 04103");
    await firstLine(take, "New cedar deck, labor", "2400");
    await addLine(take, "Cedar boards and hardware", null, "1850");
    await lookAtTotal(take);
    await signature(take);
    await templates(take, page, ["modern", "formal"]);
    await download(take, page, "estimate.pdf");
  },
};

(async () => {
  const [kind, folder] = process.argv.slice(2);
  if (!RECORDINGS[kind] || !folder) throw new Error("Usage: node record_generator.js invoice|estimate|taxcalc|mileage|hourly OUT_FOLDER");
  const viewport = VIEWPORTS[kind];
  fs.rmSync(path.join(folder, "frames"), { recursive: true, force: true });
  fs.mkdirSync(path.join(folder, "frames"), { recursive: true });
  // Edge refuses to start while an update of it is waiting to be installed, and Chrome draws the pages the same way.
  const browser = await chromium.launch({ executablePath: EDGE, headless: true })
    .catch(() => chromium.launch({ executablePath: CHROME, headless: true }));
  const context = await browser.newContext({
    viewport, deviceScaleFactor: SCALE, locale: "en-US", timezoneId: "America/New_York", acceptDownloads: true,
  });
  const page = await context.newPage();
  // The recording would otherwise count as visits and downloads in the site's own tool statistics.
  await page.route("**/api/invoice-generator/track.php", (route) => route.abort());
  await page.goto(PAGES[kind], { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);
  const take = new Take(page, folder);
  await RECORDINGS[kind](page, take);
  const session = { size: [viewport.width * SCALE, viewport.height * SCALE], scale: SCALE, shots: take.shots, path: take.path, marks: take.marks, events: take.events, places: take.places };
  fs.writeFileSync(path.join(folder, `${kind}_session.json`), JSON.stringify(session, null, 1));
  console.log(`${take.shots.length} pictures over ${take.t.toFixed(1)} s; marks: ${Object.entries(take.marks).map(([k, v]) => `${k} ${v}`).join(", ")}`);
  await browser.close();
})();

// jsPDF saves through a blank tab, and Playwright rejects that tab's first navigation once the browser closes.
process.on("unhandledRejection", (error) => {
  if (error.name !== "TargetClosedError") throw error;
});
