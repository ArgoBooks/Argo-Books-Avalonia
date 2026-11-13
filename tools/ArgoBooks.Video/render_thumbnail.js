// Saves a thumbnail's HTML as a PNG beside it, at twice the size of its page. Usage: node render_thumbnail.js THUMB.html WIDTH HEIGHT
const path = require("path");
const { pathToFileURL } = require("url");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";

(async () => {
  const [html, width, height] = process.argv.slice(2);
  // Edge refuses to start while an update of it is waiting to be installed, and Chrome draws the page the same way.
  const browser = await chromium.launch({ executablePath: EDGE, headless: true })
    .catch(() => chromium.launch({ executablePath: CHROME, headless: true }));
  const page = await (await browser.newContext({ viewport: { width: +width, height: +height }, deviceScaleFactor: 2 })).newPage();
  await page.goto(pathToFileURL(path.resolve(html)).href, { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: html.replace(/\.html$/, ".png") });
  await browser.close();
})();
