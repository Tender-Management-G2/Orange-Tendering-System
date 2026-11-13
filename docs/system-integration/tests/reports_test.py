import io, os, re, subprocess, zipfile
import xml.etree.ElementTree as ET
from playwright.sync_api import sync_playwright
from ui_common import *

OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\reports-analytics\screenshots"
os.makedirs(OUT, exist_ok=True)
DB = STATE["connection"].split("Database=")[1].split(";")[0]
res = []


def check(ok, name, d=""):
    res.append(bool(ok)); print(("PASS " if ok else "FAIL ") + name + (f"  [{d}]" if d else ""), flush=True)


def sql(q):
    r = subprocess.run(["sqlcmd", "-S", "localhost", "-E", "-C", "-d", DB, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + q], capture_output=True, text=True)
    return r.stdout.strip()


AUD = {"purchase-requests": ["Admin", "SourcingOfficer", "SourcingManager", "TopCommittee"], "tenders": ["Admin", "SourcingOfficer", "SourcingManager", "TopCommittee"],
       "suppliers": ["Admin", "SourcingOfficer", "SourcingManager", "TopCommittee"], "evaluations": ["SourcingOfficer", "SourcingManager", "TopCommittee"],
       "governance": ["Admin", "SourcingManager", "TopCommittee"], "management": ["TopCommittee"]}
PATH = {"purchase-requests": "PurchaseRequests", "tenders": "Tenders", "suppliers": "Suppliers", "evaluations": "Evaluations", "governance": "Governance", "management": "Management"}

proc = start_server()
try:
    with sync_playwright() as p:
        b = p.chromium.launch()
        def ctx(mode="dark"):
            c = b.new_context(ignore_https_errors=True, viewport={"width": 1672, "height": 941}); theme(c, mode); return c

        # ---- public home statistics are real ----
        c = ctx(); pg = c.new_page(); pg.goto(BASE + "/"); pg.wait_for_timeout(1500)
        body = pg.inner_text("body")
        active = sql("SELECT COUNT(*) FROM Tenders WHERE Status IN ('Bidding','Evaluation','OfficerReview','ManagerReview','TopCommitteeReview')")
        suppliers = sql("SELECT COUNT(*) FROM Suppliers WHERE Status='Approved'")
        check("NOT CONNECTED" not in body and "not connected" not in body.lower(), "Public home page no longer says statistics are not connected")
        vals = pg.locator(".metric strong").all_inner_texts()
        check(vals[:2] == [active, suppliers], f"Home metrics equal SQL (active tenders {active}, approved suppliers {suppliers})", str(vals))
        pg.locator("#insights, .dashboard-shell").first.scroll_into_view_if_needed(); pg.wait_for_timeout(1200)
        pg.locator(".dashboard-shell").first.screenshot(path=os.path.join(OUT, "Home-statistics-dark.png"))
        c.close()
        c = ctx("light"); pg = c.new_page(); pg.goto(BASE + "/"); pg.wait_for_timeout(1500); pg.locator(".dashboard-shell").first.scroll_into_view_if_needed(); pg.wait_for_timeout(1200)
        pg.locator(".dashboard-shell").first.screenshot(path=os.path.join(OUT, "Home-statistics-light.png")); c.close()

        # ---- reports per role ----
        for area in ("Admin", "SourcingOfficer", "SourcingManager", "TopCommittee"):
            c = ctx(); pg = c.new_page(); errs = []; pg.on("pageerror", lambda e: errs.append(str(e)))
            login(pg, area)
            pg.goto(BASE + "/Analytics"); pg.wait_for_timeout(400)
            listed = pg.locator(".pg-card span").all_inner_texts()
            expected = [k for k, roles in AUD.items() if area in roles]
            check(len(listed) == len(expected), f"{area}: report index lists exactly the {len(expected)} reports of its role", str(listed))
            for key, roles in AUD.items():
                r = pg.goto(BASE + "/Analytics/" + PATH[key]); pg.wait_for_timeout(250)
                if area in roles:
                    ok = r.status == 200 and "Download PDF" in pg.inner_text("main") and pg.locator("dl.ui-facts dd").count() >= 5
                    check(ok, f"{area}: {key} report opens with live KPIs and export buttons")
                else:
                    check("AccessDenied" in pg.url or r.status == 403, f"{area}: {key} report is refused", pg.url.replace(BASE, ""))
            check(not errs, f"{area}: no JavaScript errors on report pages")
            c.close()

        # ---- numbers match SQL; exports are valid files ----
        c = ctx(); pg = c.new_page(); login(pg, "TopCommittee")
        pg.goto(BASE + "/Analytics/PurchaseRequests"); pg.wait_for_timeout(400)
        facts = dict(zip(pg.locator("dl.ui-facts dt").all_text_contents(), pg.locator("dl.ui-facts dd").all_text_contents()))
        check(facts["Total requests"] == sql("SELECT COUNT(*) FROM PurchaseRequests"), "Purchase-request total equals SQL", str(facts))
        pg.screenshot(path=os.path.join(OUT, "Report-purchase-requests-dark.png"))
        pg.goto(BASE + "/Analytics/Tenders"); pg.wait_for_timeout(300)
        facts = dict(zip(pg.locator("dl.ui-facts dt").all_text_contents(), pg.locator("dl.ui-facts dd").all_text_contents()))
        check(facts["Total tenders"] == sql("SELECT COUNT(*) FROM Tenders") and facts["Awarded"] == sql("SELECT COUNT(*) FROM Tenders WHERE Status='Awarded'"), "Tender totals and awarded count equal SQL")
        pg.screenshot(path=os.path.join(OUT, "Report-tenders-dark.png"))
        pg.goto(BASE + "/TopCommittee/Statistics"); pg.wait_for_timeout(400)
        facts = dict(zip(pg.locator("dl.ui-facts dt").all_text_contents(), pg.locator("dl.ui-facts dd").all_text_contents()))
        check(facts["Registered suppliers"] == sql("SELECT COUNT(*) FROM Suppliers") and "Transactions with each supplier" in pg.inner_text("main"), "Top Committee statistics page: supplier counts and per-supplier transactions")
        pg.screenshot(path=os.path.join(OUT, "TopCommittee-statistics-dark.png"))
        for key in AUD:
            if "TopCommittee" not in AUD[key]: continue
            x = pg.request.get(BASE + f"/Analytics/Export?key={key}&format=xlsx")
            data = x.body()
            try:
                z = zipfile.ZipFile(io.BytesIO(data)); parts = z.namelist(); [ET.fromstring(z.read(n)) for n in parts if n.endswith(".xml") or n.endswith(".rels")]
                wb = ET.fromstring(z.read("xl/workbook.xml")); sheets = [s.attrib["name"] for s in wb.iter() if s.tag.endswith("sheet")]
                check(x.status == 200 and len(sheets) >= 2, f"Excel export {key}: valid workbook ({len(sheets)} sheets)", ", ".join(sheets[:3]))
            except Exception as e:
                check(False, f"Excel export {key} is a valid xlsx", str(e)[:80])
            y = pg.request.get(BASE + f"/Analytics/Export?key={key}&format=pdf")
            check(y.status == 200 and y.body()[:4] == b"%PDF" and len(y.body()) > 2500, f"PDF export {key}: valid PDF ({len(y.body())} bytes)")
        # the workbook really contains the live numbers
        x = pg.request.get(BASE + "/Analytics/Export?key=purchase-requests&format=xlsx"); z = zipfile.ZipFile(io.BytesIO(x.body()))
        s1 = z.read("xl/worksheets/sheet1.xml").decode()
        check(f">{sql('SELECT COUNT(*) FROM PurchaseRequests')}<" in s1 or f"<v>{sql('SELECT COUNT(*) FROM PurchaseRequests')}</v>" in s1, "Excel summary sheet contains the live request count")
        check(pg.request.get(BASE + "/Analytics/Export?key=purchase-requests&format=csv").status == 400, "Unknown export format refused")
        c.close()
        # Admin cannot export evaluations / management via direct URL
        c = ctx(); pa = c.new_page(); login(pa, "Admin")
        r1 = pa.request.get(BASE + "/Analytics/Export?key=evaluations&format=pdf", max_redirects=0); r2 = pa.request.get(BASE + "/Analytics/Export?key=management&format=xlsx", max_redirects=0)
        check(r1.status in (302, 403) and r2.status in (302, 403), "Admin cannot export evaluation or management reports by URL", f"{r1.status}/{r2.status}")
        pa.goto(BASE + "/Analytics"); pa.screenshot(path=os.path.join(OUT, "Reports-index-admin-dark.png")); c.close()
        # Supplier / Department have no access
        for area in ("Supplier", "Department"):
            c = ctx(); pg = c.new_page(); login(pg, area); r = pg.goto(BASE + "/Analytics/Tenders")
            check("AccessDenied" in pg.url or r.status in (302, 403), f"{area} cannot open reports", pg.url.replace(BASE, "")); c.close()
        # light mode
        c = ctx("light"); pg = c.new_page(); login(pg, "SourcingManager"); pg.goto(BASE + "/Analytics/Governance"); pg.wait_for_timeout(400); pg.screenshot(path=os.path.join(OUT, "Report-governance-light.png")); c.close()
        # Admin clarification page no longer errors
        c = ctx(); pa = c.new_page(); login(pa, "Admin"); r = pa.goto(BASE + "/Clarifications/New"); check(r.status == 200 and "Clarifications" in pa.title() or r.status == 200, "Admin opening /Clarifications/New no longer returns HTTP 500", str(r.status))
        c2 = ctx(); pt = c2.new_page(); login(pt, "TopCommittee"); r = pt.goto(BASE + "/TopCommittee/Decisions"); check(r.status == 200, "Top Committee /Decisions no longer 404", str(r.status))
        b.close()
finally:
    stop_server(proc)
print(f"RESULT: {sum(res)}/{len(res)} passed")
