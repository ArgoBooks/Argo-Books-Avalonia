// Draws the roundup video's cards from the comparison graphic's own HTML, so they share its type, colours and layout:
// the graphic with one row lit at a time, an opening version showing only who each one is for, and a title card for
// each of the four sections. Usage: node render_cards.js GRAPHIC.html OUT_FOLDER
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const NAMES = ["Wave", "Zoho Books", "Argo Books", "ZipBooks"];
const BEST = [
  "Best if you send a lot of invoices.",
  "Best if you are under C$50K a year.",
  "Best if you want your books on your own computer.",
  "Best if you just want to invoice and get paid.",
];

// One row lit and the others faded back, with the part being talked about in the brand blue.
const focus = (n, part) => `
  .row { transition:none; opacity:.22; }
  .row:nth-child(${n + 1}) { opacity:1; background:#f5f7ff; margin:0 -38px; padding-left:38px; padding-right:38px; border-radius:12px; }
  ${part === "best" ? `.row:nth-child(${n + 1}) .note { color:var(--cobalt); font-weight:700; }` : ""}
  ${part === "catch" ? `.row:nth-child(${n + 1}) .limit .what { text-decoration:underline; text-decoration-thickness:3px; text-underline-offset:6px; }` : ""}`;

// The opening shows who each one is for and holds back the catches, which the video goes through one at a time.
const opening = `
  .tag { display:none; }
  .limit, .pull { visibility:hidden; }
  h1 { font-size:66px; }`;

// Each product's own icon, put to the left of its name in every row.
const LOGO_STYLE = `
  .logo { flex:none; width:60px; height:60px; border-radius:14px; object-fit:contain; background:#fff;
          box-shadow:0 2px 8px rgba(15,23,42,.10); padding:6px; }`;

function logos(folder) {
  const data = (file, type) => `data:${type};base64,${fs.readFileSync(path.join(folder, file)).toString("base64")}`;
  // The ZipBooks mark is the first 30 units of its wordmark's drawing.
  const zip = fs.readFileSync(path.join(folder, "zipbooks.svg"), "utf-8")
    .replace(/width="148"/, 'width="30"').replace(/viewBox="0 0 148 30"/, 'viewBox="0 0 30 30"');
  return [data("wave.png", "image/png"), data("zoho.png", "image/png"), data("argo.png", "image/png"),
    `data:image/svg+xml;base64,${Buffer.from(zip).toString("base64")}`];
}

function sectionCard(source, n) {
  const head = source.slice(0, source.indexOf("</head>"));
  return `${head}
<style>
  .num{ position:absolute; left:96px; top:330px; font-family:var(--mono); font-weight:600; font-size:30px; letter-spacing:.14em; color:var(--cobalt); }
  .big{ position:absolute; left:90px; top:380px; font-weight:800; font-size:150px; line-height:1; letter-spacing:-.035em; color:var(--ink); }
  .best{ position:absolute; left:96px; top:580px; font-size:40px; font-weight:400; color:var(--body); }
  .bar{ position:absolute; left:96px; top:680px; width:120px; height:8px; background:var(--cobalt); border-radius:4px; }
</style>
</head>
<body>
  <div class="stage">
    <div class="mast">
      <div class="kicker">Argo Books <span class="dot">/</span> Free accounting software <span class="dot">/</span> 2026</div>
      <div class="filed">${n + 1} of 4</div>
    </div>
    <div class="rule"></div>
    <div class="num">${String(n + 1).padStart(2, "0")}</div>
    <div class="big">${NAMES[n]}</div>
    <div class="best">${BEST[n]}</div>
    <div class="bar"></div>
  </div>
</body>
</html>`;
}

// Labels laid over the pricing pages, for prices said one way and shown another, in the graphic's type.
const NOTES = {
  note_wave_addon: ["Receipt scanning add-on", "$11 USD a month, or $96 USD a year"],
  note_zoho_limit: ["Zoho Books Free", "Free only while revenue is under C$50K a year"],
  note_zip_smarter: ["ZipBooks Smarter plan", "$15 USD a month"],
};

function note(source, [label, text]) {
  const head = source.slice(0, source.indexOf("</head>"));
  return `${head}
<style>
  html, body { width:auto; height:auto; background:transparent; }
  .pill { display:inline-block; margin:30px; background:#fff; border-left:8px solid var(--red); border-radius:14px; padding:22px 34px 24px 30px;
          box-shadow:0 24px 50px -20px rgba(15,23,42,.35), 0 4px 14px rgba(15,23,42,.12); }
  .label { font-family:var(--mono); font-size:19px; font-weight:600; letter-spacing:.12em; text-transform:uppercase; color:var(--cobalt); }
  .text { font-size:38px; font-weight:800; letter-spacing:-.01em; color:var(--ink); margin-top:6px; }
</style>
</head>
<body><div class="pill"><div class="label">${label}</div><div class="text">${text}</div></div></body>
</html>`;
}

(async () => {
  const [graphic, out, logoFolder] = process.argv.slice(2);
  fs.mkdirSync(out, { recursive: true });
  const icons = logos(logoFolder);
  // The icons go in each row ahead of the name, the second element of the row.
  let row = 0;
  const source = fs.readFileSync(graphic, "utf-8").replace(/<span>\s*<div class="who">/g,
    (match) => `<img class="logo" src="${icons[row++ % 4]}">${match}`);
  const withStyle = (css) => source.replace("</head>", `<style>${LOGO_STYLE}${css}</style></head>`);
  const cards = { card_all: withStyle(""), card_opening: withStyle(opening) };
  for (let n = 0; n < 4; n++) {
    cards[`card_best_${n}`] = withStyle(focus(n, "best"));
    cards[`card_catch_${n}`] = withStyle(focus(n, "catch"));
    cards[`section_${n}`] = sectionCard(source, n);
  }
  const browser = await chromium.launch({ executablePath: EDGE, headless: true });
  const page = await (await browser.newContext({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 2 })).newPage();
  for (const [name, html] of Object.entries(cards)) {
    await page.setContent(html, { waitUntil: "networkidle" });
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: path.join(out, `${name}.png`) });
  }
  for (const [name, lines] of Object.entries(NOTES)) {
    await page.setContent(note(source, lines), { waitUntil: "networkidle" });
    await page.evaluate(() => document.fonts.ready);
    const box = await page.locator(".pill").boundingBox();
    await page.screenshot({ path: path.join(out, `${name}.png`), omitBackground: true,
      clip: { x: box.x - 30, y: box.y - 30, width: box.width + 60, height: box.height + 60 } });
  }
  await browser.close();
  console.log(`${Object.keys(cards).length} cards and ${Object.keys(NOTES).length} notes in ${out}`);
})();
