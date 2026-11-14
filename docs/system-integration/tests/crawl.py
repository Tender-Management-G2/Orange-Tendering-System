import os, re, sys, json
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright
from ui_common import *

MARK = re.compile(r"not implemented|scaffold|UI SCAFFOLD|coming soon|TODO|not connected|no data connected|lorem", re.I)
areas = [a for a in ACCOUNTS if a != "SupplierTwo"]
proc = start_server()
report = []
try:
    with sync_playwright() as p:
        b = p.chromium.launch()
        for area in areas:
            ctx = b.new_context(ignore_https_errors=True, viewport={"width": 1500, "height": 900})
            page = ctx.new_page(); errs = []
            page.on("pageerror", lambda e: errs.append("js:" + str(e)[:100]))
            page.on("console", lambda m: errs.append("console:" + m.text[:100]) if m.type == "error" else None)
            login(page, area)
            seen, queue = set(), ["/" + ("Supplier" if area.startswith("Supplier") else area)]
            while queue and len(seen) < 90:
                path = queue.pop(0)
                if path in seen: continue
                seen.add(path); errs.clear()
                try:
                    r = page.goto(BASE + path, wait_until="domcontentloaded", timeout=20000); status = r.status if r else 0
                    page.wait_for_timeout(250)
                except Exception as e:
                    status = 0; errs.append(str(e)[:80])
                url = page.url.replace(BASE, "")
                text = page.inner_text("body") if status else ""
                marks = sorted(set(m.group(0).lower() for m in MARK.finditer(text)))
                empty_rows = len(re.findall(r"No .{3,60}(yet|found|available|loaded)", text))
                report.append(dict(role=area, path=path, final=url, status=status, marks=marks, errs=list(errs), empty=empty_rows))
                if status == 200 and "/Identity/" not in url:
                    hrefs = page.eval_on_selector_all("main a[href], .ws-primary-nav a[href], .ws-topnav-links a[href]", "els => els.map(e => e.getAttribute('href'))")
                    for h in hrefs:
                        if not h or h.startswith(("#", "mailto", "http", "javascript")) or "Logout" in h or "download" in h.lower() or "Document" in h and "Documents" not in h: continue
                        u = urlparse(h).path
                        if u not in seen and u not in queue: queue.append(u)
            ctx.close()
        b.close()
finally:
    stop_server(proc)

json.dump(report, open(os.path.join(SCR, "crawl.json"), "w"), indent=1)
print(f"{len(report)} pages crawled")
bad = [r for r in report if r["status"] not in (200,) or r["errs"] or r["marks"] or "AccessDenied" in r["final"]]
for r in bad:
    print(f"{r['role']:20} {r['status']} {r['path']:55} -> {r['final'][:40]:40} {r['marks']} {r['errs'][:1]}")
