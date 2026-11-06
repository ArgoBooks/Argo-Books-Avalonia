// Captures the web pages a video shows: each page whole, at twice the size of a 1600x900 window, set up the way the
// script describes it (monthly prices, cookie banners closed, answers opened), with where each line it points at sits.
// Usage: node capture_pages.js OUT_FOLDER [page names...]   (run "npm install" in this folder first)
const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const MAC_SAFARI = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15";

// Each page: its address, what to click before the capture, and the lines of text to find on it. A target is the
// text to find, optionally with the card or row it has to sit inside, given as text that is also in that container.
const PAGES = {
  wave: {
    url: "https://www.waveapps.com/pricing",
    clicks: [{ role: "button", name: "Accept All Cookies" }, { mouse: "a.plan-tier-tab.starter" }],
    targets: {
      starter: { text: "STARTER" },
      starter_price: { text: "$0", near: "Just starting your business" },
      starter_unlimited: { text: "Create unlimited estimates, invoices, bills, and bookkeeping records" },
      pro: { text: "Want to look more polished" },
      pro_price: { text: "CAD/month" },
      pro_bank: { text: "Auto-import bank transactions", near: "Everything in Starter" },
      receipts_addon: { text: "$11/month OR $96/year" },
      receipts_row: { text: "Digitally capture unlimited receipts", near: "ADD-ON" },
      logo_row: { text: "Add your logo and brand colors" },
    },
  },
  zoho: {
    url: "https://www.zoho.com/ca/books/pricing/",
    clicks: [{ mouse: "button.price-toggle-switch" }],
    opens: ["For how long is the free plan free?", "How many invoices can I create in a year?", "How many bills/expenses can I create in a year?"],
    targets: {
      free_card: { text: "Get started with free accounting software" },
      free_price: { text: "C$0" },
      free_users: { text: "1 User + 1 Accountant" },
      standard_price: { text: "C$20", near: "Efficiently organize" },
      bank_feeds: { text: "Connect bank feeds" },
      faq_free: { text: "As long as your revenue" },
      faq_invoices: { text: "Up to 1,000 invoices can be created annually" },
      faq_expenses: { text: "create up to 1,000 expenses annually" },
    },
  },
  zipbooks: {
    url: "https://zipbooks.com/pricing",
    clicks: [],
    targets: {
      starter: { text: "Starter", near: "Send unlimited invoices" },
      starter_free: { text: "Free", near: "Send unlimited invoices" },
      unlimited_invoices: { text: "Send unlimited invoices" },
      unlimited_contacts: { text: "Manage unlimited vendors & customers" },
      payments: { text: "Accept digital payments (Square or PayPal)" },
      reports: { text: "View Basic Reports" },
      one_bank: { text: "Connect 1 bank account" },
      smarter_price: { text: "15", near: "Everything in Starter" },
      recurring: { text: "Schedule recurring invoices with auto-bill" },
      reminders: { text: "Automate reminders" },
      time: { text: "Track time" },
      team: { text: "Add 5 team members" },
    },
  },
  // The Mac video. Intuit's pages refuse a headless browser, so its facts are on cards instead.
  wave_us: {
    mac: true,
    url: "https://www.waveapps.com/pricing",
    clicks: [{ role: "button", name: "Accept All Cookies" }],
    targets: {
      starter_price: { text: "$0", near: "Just starting your business" },
      starter_unlimited: { text: "Create unlimited estimates, invoices, bills, and bookkeeping records" },
      pro_price: { text: "$250CAD/year" },
      pro_bank: { text: "Auto-import bank transactions", near: "Everything in Starter" },
      pro_receipts: { text: "Digitally capture unlimited receipts and track expenses" },
    },
  },
  zoho_mac: {
    mac: true,
    url: "https://apps.apple.com/us/app/zoho-books-accounting-app/id1476500403?mt=12",
    clicks: [],
    targets: { only_mac: { text: "Only for Mac" }, requires: { text: "Requires macOS 13.0 or later." } },
  },
  zoho_us: {
    mac: true,
    url: "https://www.zoho.com/us/books/pricing/",
    clicks: [],
    opens: ["For how long is the free plan free?", "How many invoices can I create in a year?"],
    targets: {
      free_card: { text: "Get started with free accounting software" },
      free_price: { text: "$0", near: "solopreneurs" },
      free_users: { text: "1 User + 1 Accountant" },
      standard_price: { text: "$20", near: "Everything in Free" },
      bank_feeds: { text: "Connect bank feeds" },
      faq_free: { text: "As long as your revenue" },
      faq_invoices: { text: "1,000 invoices" },
    },
  },
  gnucash_download: {
    mac: true,
    url: "https://www.gnucash.org/download.phtml",
    clicks: [],
    targets: { silicon: { text: "for Apple macOS Apple Silicon" }, intel: { text: "for Apple macOS Intel" } },
  },
  gnucash_home: {
    mac: true,
    url: "https://www.gnucash.org/",
    clicks: [],
    targets: { silicon: { text: "Apple macOS Apple Silicon" } },
  },
  gnucash_features: {
    mac: true,
    url: "https://www.gnucash.org/features.phtml",
    clicks: [],
    targets: {
      double_entry: { text: "Every transaction must debit one account and credit others" }, register: { text: "Checkbook-Style Register" },
      qif: { text: "QIF and OFX Import" }, quicken: { text: "GnuCash can import Intuit® Quicken® QIF files" },
    },
  },
  manager_download: {
    mac: true,
    url: "https://www.manager.io/download",
    clicks: [],
    targets: {
      free_forever: { text: "No internet connection required. Fully-featured and free forever." },
      mac: { text: "Requires Mac OS X 12 (with Apple Silicon) or newer." },
      intel: { text: "Alternative download for Intel-based Macs" },
      single_user: { text: "Desktop edition is single-user software." },
    },
  },
  manager_home: {
    mac: true,
    url: "https://www.manager.io/",
    clicks: [],
    targets: { free_forever: { text: "Free Forever" }, no_limits: { text: "There are no time limits" }, offline: { text: "Work Offline" } },
  },
  manager_cloud: {
    mac: true,
    url: "https://www.manager.cloud",
    clicks: [],
    targets: { price: { text: "59 USD" }, users: { text: "Unlimited Users + Unlimited Businesses" } },
  },
  // The QuickBooks Desktop alternatives video, on Windows. Sage's pages refuse a headless browser, so its facts are on cards.
  accountedge_pricing: {
    url: "https://www.accountedge.com/pricing/",
    clicks: [{ mouse: "div.cmplz-close" }],
    targets: {
      platforms: { text: "For Mac", exact: true },
      usd: { text: "(All pricing in USD)" },
      payroll: { text: "Payroll available as an optional service on all AccountEdge plans." },
      pro_price: { text: "$20/Month", near: "AccountEdge Pro" },
      single_user: { text: "Single User" },
      local: { text: "Local Desktop/On-Premise", near: "Single User" },
      network_price: { text: "$30/Month" },
      two_users: { text: "includes 2 users" },
    },
  },
  accountedge_qb: {
    url: "https://www.accountedge.com/convert-from-quickbooks/",
    clicks: [{ mouse: "div.cmplz-close" }],
    targets: {
      from_389: { text: "starting at $389" },
      iif: { text: "we have a free IIF import tool" },
      current_year: { text: "$389", exact: true },
      prior_years: { text: "$200 per year" },
    },
  },
  gnucash_download_win: {
    url: "https://www.gnucash.org/download.phtml",
    clicks: [],
    targets: {
      windows: { text: "for Microsoft Windows 8/10/11" }, silicon: { text: "for Apple macOS Apple Silicon" },
      linux: { text: "Most linux distributions" },
    },
  },
  manager_download_win: {
    url: "https://www.manager.io/download",
    clicks: [],
    targets: {
      free_forever: { text: "No internet connection required. Fully-featured and free forever." },
      windows: { text: "Requires Windows 10 Version 1709 (64-bit) or newer." },
      single_user: { text: "Desktop edition is single-user software." },
    },
  },
  argo_home: { url: "https://argorobots.com/", clicks: [], targets: {} },
  argo_downloads: {
    url: "https://argorobots.com/downloads/",
    clicks: [],
    targets: {
      no_account: { text: "Get started for free. No account required." }, windows: { text: "For Windows 10 and later" },
      mac: { text: "For macOS 14 Sonoma and later" }, linux: { text: "Download for Linux" },
    },
  },
  argo_downloads_mac: {
    mac: true,
    url: "https://argorobots.com/downloads/",
    clicks: [],
    targets: {
      no_account: { text: "Get started for free. No account required." },
      mac_card: { text: "For macOS 14 Sonoma and later" },
      silicon: { text: "Apple Silicon · 107.8 MB" },
      intel: { text: "Intel · 135 MB" },
      brew: { text: "brew install" },
    },
  },
  argo_pricing: {
    url: "https://argorobots.com/pricing/",
    clicks: [],
    targets: {
      free_card: { text: "Just starting out, or you only need the basics?" },
      free_invoices: { text: "Send 25 invoices a month" },
      free_scans: { text: "Scan 10 receipts a month, no typing" },
      free_bank: { text: "Import 25 bank statements a month" },
      free_imports: { text: "Import 100 spreadsheets a month" },
      premium_card: { text: "Want unlimited invoicing" },
      premium_price: { text: "CAD/month" },
    },
  },
};

async function boxOf(page, target) {
  return page.evaluate(({ text, near, exact }) => {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
    const found = [];
    for (let el = walker.nextNode(); el; el = walker.nextNode()) {
      const own = [...el.childNodes].filter((n) => n.nodeType === 3).map((n) => n.textContent).join("").trim();
      const full = el.innerText ? el.innerText.trim() : "";
      const hit = exact ? full === text : (own.includes(text) || (full.includes(text) && full.length < text.length + 80));
      if (!hit) continue;
      const rect = el.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) continue;
      if (near) {
        let box = el.parentElement, ok = false;
        for (let up = 0; box && up < 8; up++, box = box.parentElement) {
          if (box.innerText && box.innerText.includes(near)) { ok = true; break; }
        }
        if (!ok) continue;
      }
      found.push([rect.left + scrollX, rect.top + scrollY, rect.width, rect.height]);
    }
    // The innermost match is the line itself rather than a card around it.
    found.sort((a, b) => a[2] * a[3] - b[2] * b[3]);
    return found[0] || null;
  }, target);
}

(async () => {
  const out = process.argv[2];
  const only = process.argv.slice(3);
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ executablePath: EDGE, headless: true });
  const settings = { viewport: { width: 1600, height: 900 }, deviceScaleFactor: 2, locale: "en-CA", timezoneId: "America/Toronto" };
  const context = await browser.newContext(settings);
  // Download pages offer what suits the visitor's computer, so a page the Mac video shows is opened as a Mac's Safari.
  const macContext = await browser.newContext({ ...settings, userAgent: MAC_SAFARI });
  for (const [name, spec] of Object.entries(PAGES)) {
    if (only.length && !only.includes(name)) continue;
    const page = await (spec.mac ? macContext : context).newPage();
    try {
      await page.goto(spec.url, { waitUntil: "domcontentloaded", timeout: 60000 });
    } catch (e) {
      console.log(`${name}: did not load (${e.message.split("\n")[0]})`);
      await page.close();
      continue;
    }
    await page.waitForLoadState("networkidle", { timeout: 15000 }).catch(() => {});
    await page.waitForTimeout(3000);
    for (const click of spec.clicks) {
      if (click.mouse) {
        const box = await page.locator(click.mouse).first().boundingBox();
        if (box) {
          await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 5 });
          await page.mouse.down();
          await page.mouse.up();
        } else {
          console.log(`${name}: ${click.mouse} is not on the page`);
        }
        await page.waitForTimeout(1500);
        continue;
      }
      if (click.js) {
        await page.evaluate(click.js).catch((e) => console.log(`${name}: ${click.js} failed: ${e.message.split("\n")[0]}`));
        await page.waitForTimeout(1200);
        continue;
      }
      const target = click.css ? page.locator(click.css)
        : click.role ? page.getByRole(click.role, { name: click.name })
        : page.getByText(click.text, { exact: !!click.exact });
      const first = target.first();
      if (await first.isVisible().catch(() => false)) {
        await first.click().catch((e) => console.log(`${name}: could not click ${JSON.stringify(click)}: ${e.message.split("\n")[0]}`));
        await page.waitForTimeout(1200);
      } else {
        console.log(`${name}: nothing to click for ${JSON.stringify(click)}`);
      }
    }
    for (const question of spec.opens || []) {
      const q = page.getByText(question, { exact: true }).first();
      await q.scrollIntoViewIfNeeded().catch(() => {});
      await q.click().catch((e) => console.log(`${name}: could not open "${question}": ${e.message.split("\n")[0]}`));
      await page.waitForTimeout(900);
    }
    // Scrolling through once makes lazy images and counters load before the full-page shot.
    const height = await page.evaluate(() => document.body.scrollHeight);
    for (let y = 0; y < height; y += 700) {
      await page.evaluate((top) => window.scrollTo(0, top), y);
      await page.waitForTimeout(200);
    }
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(1500);
    const boxes = {};
    for (const [key, target] of Object.entries(spec.targets)) {
      const box = await boxOf(page, target);
      if (box) boxes[key] = box.map((v) => Math.round(v * 2));
      else console.log(`${name}: did not find ${key} (${target.text})`);
    }
    await page.screenshot({ path: path.join(out, `${name}.png`), fullPage: true });
    fs.writeFileSync(path.join(out, `${name}.json`), JSON.stringify({ url: page.url(), boxes }, null, 1));
    fs.writeFileSync(path.join(out, `${name}.txt`), await page.evaluate(() => document.body.innerText));
    console.log(`${name}: ${Object.keys(boxes).length}/${Object.keys(spec.targets).length} found, page ${height}px tall`);
    await page.close();
  }
  await browser.close();
})();
