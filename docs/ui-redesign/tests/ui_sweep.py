import os, sys
from playwright.sync_api import sync_playwright
from ui_common import *

OUT = os.path.join(SCR, "sweep"); os.makedirs(OUT, exist_ok=True)
modes = sys.argv[1].split(",") if len(sys.argv) > 1 else ["dark"]
areas = sys.argv[2].split(",") if len(sys.argv) > 2 else list(ACCOUNTS.keys())
proc = start_server()
rows = []
try:
    with sync_playwright() as p:
        b = p.chromium.launch()
        for area in areas:
            if area == "SupplierTwo": continue
            for mode in modes:
                ctx = b.new_context(ignore_https_errors=True, viewport={"width": 1672, "height": 941}); theme(ctx, mode)
                page = ctx.new_page(); errs = []
                page.on("pageerror", lambda e: errs.append(str(e)))
                page.on("console", lambda m: errs.append("console:" + m.text[:120]) if m.type == "error" else None)
                login(page, area)
                hrefs = page.eval_on_selector_all(".ws-primary-nav a, .ws-topnav-links a", "els => [...new Set(els.map(e => e.getAttribute('href')))]")
                for h in hrefs:
                    errs.clear()
                    try:
                        r = page.goto(BASE + h, wait_until="networkidle", timeout=20000)
                        status = r.status if r else 0
                    except Exception as e:
                        status = 0; errs.append(str(e)[:100])
                    page.wait_for_timeout(250)
                    overflow = page.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
                    name = (area + "-" + h.strip("/").replace("/", "_") + "-" + mode)[:90]
                    page.screenshot(path=os.path.join(OUT, name + ".png"))
                    rows.append((area, mode, h, status, overflow, list(errs)))
                    flag = "" if status == 200 and overflow <= 2 and not errs else "  <-- CHECK"
                    print(f"{area:20} {mode:5} {h:45} {status} overflow={overflow} errs={len(errs)}{flag}", flush=True)
                ctx.close()
        b.close()
finally:
    stop_server(proc)
bad = [r for r in rows if r[3] != 200 or r[4] > 2 or r[5]]
print(f"\n{len(rows)} pages, {len(bad)} to check")
for r in bad: print(r)
