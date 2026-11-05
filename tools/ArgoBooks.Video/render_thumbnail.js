// Saves a thumbnail's HTML as a PNG beside it, at twice the size of its page. Usage: node render_thumbnail.js THUMB.html WIDTH HEIGHT
const path = require("path");
const { pathToFileURL } = require("url");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";

(async () => {
  const [html, width, height] = process.argv.slice(2);
  const browser = await chromium.launch({ executablePath: EDGE, headless: true });
  const page = await (await browser.newContext({ viewport: { width: +width, height: +height }, deviceScaleFactor: 2 })).newPage();
  await page.goto(pathToFileURL(path.resolve(html)).href, { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: html.replace(/\.html$/, ".png") });
  await browser.close();
})();
