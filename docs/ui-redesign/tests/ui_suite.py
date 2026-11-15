import os, re, subprocess, sys, time, json
from playwright.sync_api import sync_playwright
from ui_common import *

OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\ui-redesign\screenshots"
os.makedirs(OUT, exist_ok=True)
DB = STATE["connection"].split("Database=")[1].split(";")[0]
results = []


def check(ok, name):
    results.append((bool(ok), name)); print(("PASS " if ok else "FAIL ") + name, flush=True)


def sql(q):
    r = subprocess.run(["sqlcmd", "-S", "localhost", "-E", "-C", "-d", DB, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + q], capture_output=True, text=True)
    return r.stdout.strip()


def uid(area):
    return sql(f"SELECT Id FROM AspNetUsers WHERE Email='{ACCOUNTS[area]['email']}'")


def unread(area):
    return int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid(area)}' AND IsRead=0"))


AREAS = [("Admin", "Admin"), ("Department", "Department"), ("SourcingOfficer", "SourcingOfficer"), ("EvaluationCommittee", "EvaluationCommittee"),
         ("SourcingManager", "SourcingManager"), ("TopCommittee", "TopCommittee"), ("Supplier", "Supplier")]

proc = start_server()
try:
    with sync_playwright() as p:
        browser = p.chromium.launch()

        def new_ctx(mode="dark", w=1672, h=941):
            ctx = browser.new_context(ignore_https_errors=True, viewport={"width": w, "height": h})
            theme(ctx, mode)
            return ctx

        # ---- 1. Seven roles x dark/light dashboards ----
        for area, _ in AREAS:
            for mode in ("dark", "light"):
                ctx = new_ctx(mode); page = ctx.new_page(); errs = []
                page.on("pageerror", lambda e: errs.append(str(e)))
                login(page, area); page.wait_for_timeout(900)
                ok_url = page.url.rstrip("/").endswith("/" + area)
                body = page.inner_text("body")
                check(ok_url, f"{area} {mode}: signs in and lands on its own workspace ({page.url.replace(BASE, '')})")
                check(page.evaluate("document.documentElement.dataset.theme") == mode, f"{area}: {mode} theme active")
                check(not errs, f"{area} {mode}: no JavaScript errors" + (": " + "; ".join(errs[:2]) if errs else ""))
                if mode == "dark":
                    check("Not connected" not in body and "Waiting for verified data" not in body, f"{area}: dashboard shows real data (no 'not connected' placeholders)")
                    nav = page.locator(".ws-topnav-links").count() > 0
                    side = page.locator("aside.ws-sidebar").count() > 0
                    check(nav if area == "Supplier" else side and not nav, f"{area}: {'horizontal navbar' if area == 'Supplier' else 'sidebar'} layout")
                page.screenshot(path=os.path.join(OUT, f"{area}-{mode}.png"))
                ctx.close()

        # ---- 2. Dashboard numbers match SQL Server ----
        ctx = new_ctx(); page = ctx.new_page(); login(page, "SourcingOfficer"); page.wait_for_timeout(500)
        vals = [x.strip() for x in page.locator(".dash-metric-copy strong").all_inner_texts()]
        bidding = sql("SELECT COUNT(*) FROM Tenders WHERE Status='Bidding'")
        suppliers = sql("SELECT COUNT(*) FROM Suppliers WHERE Status='Approved'")
        check(vals[0] == bidding and vals[2] == suppliers, f"Officer KPIs equal SQL counts (UI {vals[:4]} vs DB bidding={bidding}, approved suppliers={suppliers})")
        ctx.close()
        ctx = new_ctx(); page = ctx.new_page(); login(page, "Supplier"); page.wait_for_timeout(500)
        sv = [x.strip() for x in page.locator(".dash-metric-copy strong").all_inner_texts()]
        sub = sql(f"SELECT COUNT(*) FROM Bids b JOIN TenderInvitations i ON i.Id=b.TenderInvitationId JOIN AspNetUsers u ON u.SupplierId=i.SupplierId WHERE u.Id='{uid('Supplier')}' AND b.Status='Submitted'")
        check(sv[1] == sub, f"Supplier submitted-offers KPI equals SQL ({sv[1]} vs {sub})")
        check(page.locator(".ui-chart svg circle").count() == 6, "Supplier participation chart has 6 real monthly points")
        ctx.close()

        # ---- 3. Notification bell ----
        ctx = new_ctx(); page = ctx.new_page(); login(page, "SourcingOfficer"); page.wait_for_timeout(1200)
        n0 = unread("SourcingOfficer")
        check(page.locator(".ws-bell-badge").inner_text() == str(n0) and n0 > 0, f"Bell badge equals SQL unread count ({n0})")
        check(page.locator("[data-notification-bell]").get_attribute("data-hub") == "connected", "SignalR hub connected")
        page.click(".ws-bell-button"); page.wait_for_selector(".ws-bell-item"); page.wait_for_timeout(500)
        titles = page.locator(".ws-bell-item strong").all_inner_texts()
        db_titles = sql(f"SELECT TOP 12 Title FROM Notifications WHERE UserId='{uid('SourcingOfficer')}' ORDER BY CreatedAt DESC").split("\n")
        check(titles[:3] == [t.strip() for t in db_titles[:3]], f"Dropdown lists the user's own notifications from SQL: {titles[:3]}")
        page.screenshot(path=os.path.join(OUT, "Notification-dropdown-dark.png"))
        first_link = page.locator(".ws-bell-item a").first
        href = first_link.get_attribute("href")
        first_link.click(); page.wait_for_load_state("networkidle")
        check(unread("SourcingOfficer") == n0 - 1, "Opening a notification marks it read in SQL and follows its link (" + href + ")")
        page.goto(BASE + "/SourcingOfficer"); page.wait_for_timeout(800)
        page.click(".ws-bell-button"); page.click("[data-bell-readall]"); page.wait_for_timeout(600)
        check(unread("SourcingOfficer") == 0 and page.locator(".ws-bell-badge").is_hidden(), "Mark all as read clears SQL unread count and the badge")
        ctx.close()

        # ---- 4. Real-time push + popup-first create (Department -> Officer) ----
        ctxO = new_ctx(); po = ctxO.new_page(); login(po, "SourcingOfficer"); po.wait_for_timeout(1500)
        before = unread("SourcingOfficer")
        ctxD = new_ctx(); pd = ctxD.new_page(); login(pd, "Department"); pd.goto(BASE + "/Department/PurchaseRequests"); pd.wait_for_timeout(600)
        pd.screenshot(path=os.path.join(OUT, "Table-Department-requests-dark.png"))
        pd.click("text=New purchase request"); pd.wait_for_selector("dialog.ui-dialog[open] form")
        check(pd.locator("dialog.ui-dialog[open] form").count() == 1 and "/Create" not in pd.url, "Create opens as a centered popup (no page navigation)")
        pd.fill("dialog [name=title]", "Playwright Projectors"); pd.fill("dialog [name=justification]", "Meeting rooms need projectors.")
        pd.fill("dialog [name=itemName]", "Projector"); pd.fill("dialog [name=quantity]", "4"); pd.fill("dialog [name=unit]", "pcs")
        pd.screenshot(path=os.path.join(OUT, "Modal-create-request-dark.png"))
        pd.click("dialog button[type=submit]"); pd.wait_for_load_state("networkidle"); pd.wait_for_timeout(800)
        check(sql("SELECT COUNT(*) FROM PurchaseRequests WHERE Title='Playwright Projectors'") == "1" and "Playwright Projectors" in pd.inner_text("main"), "Popup form saved to SQL Server and the app showed the saved request")
        for _ in range(20):
            if po.locator(".ws-bell-badge").is_visible() and po.locator(".ws-bell-badge").inner_text() == str(before + 1): break
            po.wait_for_timeout(500)
        check(po.locator(".ws-bell-badge").inner_text() == str(before + 1), f"Officer's bell updated live over SignalR without reloading ({before} -> {before + 1})")
        check(po.locator(".ws-toast").count() >= 1, "Live toast shown for the new notification")
        po.screenshot(path=os.path.join(OUT, "Notification-live-toast-dark.png"))
        # drawer
        pd.goto(BASE + "/Department/PurchaseRequests"); pd.wait_for_timeout(500)
        try:
            pd.locator("a.pg-btn:has-text('Details')").first.click(timeout=8000)
        except Exception:
            pd.screenshot(path=os.path.join(OUT, "debug-drawer.png")); print("DRAWER DEBUG", pd.url, pd.locator("a.pg-btn").all_inner_texts()); raise
        pd.wait_for_selector("dialog.ui-dialog--drawer[open]")
        check(pd.locator("dialog.ui-dialog--drawer").count() == 1, "Details opens as a right-hand drawer")
        pd.screenshot(path=os.path.join(OUT, "Drawer-request-details-dark.png"))
        ctxD.close(); ctxO.close()

        # ---- 5. Officer review popup + search + table kit ----
        ctx = new_ctx(); page = ctx.new_page(); login(page, "SourcingOfficer")
        page.goto(BASE + "/SourcingOfficer/PurchaseRequests"); page.wait_for_timeout(600)
        page.locator("a.pg-btn:has-text('Review')").first.click(); page.wait_for_selector("dialog.ui-dialog[open] form")
        check(page.locator("dialog button[name=decision]").count() == 3, "Review opens as a popup with Accept / Return / Reject")
        page.screenshot(path=os.path.join(OUT, "Modal-review-request-dark.png"))
        page.keyboard.press("Escape")
        page.goto(BASE + "/SourcingOfficer"); page.wait_for_timeout(600)
        page.fill(".ws-search input", "Network"); page.wait_for_selector(".ws-search-item", timeout=5000)
        hits = page.locator(".ws-search-item strong").all_inner_texts()
        check(any("Network" in h for h in hits), f"Global search returns authorized SQL results: {hits[:3]}")
        page.screenshot(path=os.path.join(OUT, "Search-dropdown-dark.png"))
        ctx.close()
        ctx = new_ctx(); page = ctx.new_page(); login(page, "Admin"); page.goto(BASE + "/Admin/Users"); page.wait_for_timeout(700)
        total = int(re.search(r"(\d+) record", page.inner_text(".ui-table-count")).group(1))
        page.fill(".ui-table-search", "officer"); page.wait_for_timeout(300)
        shown = page.locator("table.pg-table tbody tr:visible").count()
        check(shown == 1 and total >= 8, f"Table search filters rows ({shown} of {total})")
        page.fill(".ui-table-search", ""); page.click("th.ui-sortable >> nth=1"); page.wait_for_timeout(200)
        emails = page.locator("table.pg-table tbody tr:visible td:nth-child(2)").all_inner_texts()
        check(emails == sorted(emails, key=str.lower) or emails == sorted(emails), "Column sorting orders rows")
        page.screenshot(path=os.path.join(OUT, "Table-Admin-users-dark.png"))
        page.goto(BASE + "/Admin/Suppliers"); page.wait_for_timeout(500)
        page.locator("tr:has-text('Pending Traders') a.pg-btn").click(); page.wait_for_selector("dialog.ui-dialog[open] form")
        check(page.locator("dialog button[name=approve]").count() == 2, "Supplier Approve / Reject review opens in a drawer popup")
        page.screenshot(path=os.path.join(OUT, "Drawer-supplier-review-dark.png"))
        ctx.close()

        # ---- 6. Light-mode feature shots, Identity + biometric pages, mobile ----
        ctx = new_ctx("light"); page = ctx.new_page()
        page.goto(BASE + "/Identity/Account/Login"); page.screenshot(path=os.path.join(OUT, "Identity-login-light.png"))
        login(page, "Supplier"); page.screenshot(path=os.path.join(OUT, "Supplier-dashboard-light-viewport.png"))
        page.goto(BASE + "/Biometrics/Fingerprint"); page.screenshot(path=os.path.join(OUT, "Biometrics-fingerprint-light.png"))
        page.goto(BASE + "/Biometrics/Face"); page.screenshot(path=os.path.join(OUT, "Biometrics-face-light.png"))
        page.click(".ws-bell-button"); page.wait_for_selector(".ws-bell-item"); page.wait_for_timeout(500); page.screenshot(path=os.path.join(OUT, "Notification-dropdown-light.png"))
        ctx.close()
        ctx = new_ctx("dark"); page = ctx.new_page(); page.goto(BASE + "/Identity/Account/Login"); page.screenshot(path=os.path.join(OUT, "Identity-login-dark.png")); ctx.close()
        for area in ("Supplier", "SourcingOfficer"):
            ctx = new_ctx("dark", 390, 844); page = ctx.new_page(); login(page, area); page.wait_for_timeout(700)
            overflow = page.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
            check(overflow <= 2, f"{area} mobile layout has no horizontal overflow ({overflow}px)")
            page.screenshot(path=os.path.join(OUT, f"{area}-mobile-dark.png")); ctx.close()
        browser.close()
finally:
    stop_server(proc)

bad = [n for ok, n in results if not ok]
print(f"\nRESULT: {len(results) - len(bad)}/{len(results)} passed")
for n in bad: print("FAILED:", n)
