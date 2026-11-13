import os, subprocess, re
from playwright.sync_api import sync_playwright
from ui_common import *

OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\system-integration\screenshots"
os.makedirs(OUT, exist_ok=True)
DB = STATE["connection"].split("Database=")[1].split(";")[0]
res = []


def check(ok, name, d=""):
    res.append(bool(ok)); print(("PASS " if ok else "FAIL ") + name + (f"  [{d}]" if d else ""), flush=True)


def sql(q):
    r = subprocess.run(["sqlcmd", "-S", "localhost", "-E", "-C", "-d", DB, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + q], capture_output=True, text=True)
    return r.stdout.strip()


def uid(a): return sql(f"SELECT Id FROM AspNetUsers WHERE Email='{ACCOUNTS[a]['email']}'")


proc = start_server()
try:
    with sync_playwright() as p:
        b = p.chromium.launch()
        def ctx():
            c = b.new_context(ignore_https_errors=True, viewport={"width": 1672, "height": 941}); theme(c, "dark"); return c

        # 1. Department creates and submits a purchase request (Department module)
        cd = ctx(); pd = cd.new_page(); login(pd, "Department")
        pd.goto(BASE + "/Department/PurchaseRequests/Create"); pd.wait_for_timeout(500)
        pd.fill("#Title", "Integration Test Servers"); pd.fill("#NeededBy", "2027-03-01"); pd.fill("#Justification", "End-to-end integration test of the whole procurement chain.")
        if pd.locator("#department-items tr").count() == 0: pd.click("#department-add-item")
        pd.locator("#department-items tr").first.locator("input").nth(0).fill("Rack server")
        pd.locator("#department-items tr").first.locator("input[type=number]").fill("3")
        pd.locator("#department-items tr").first.locator("input").last.fill("pcs")
        co = ctx(); po = co.new_page(); login(po, "SourcingOfficer"); po.wait_for_timeout(1500)
        o_before = int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid('SourcingOfficer')}' AND IsRead=0"))
        pd.screenshot(path=os.path.join(OUT, "01-department-new-request-dark.png"))
        pd.click("button[value=submit]"); pd.wait_for_load_state("networkidle"); pd.wait_for_timeout(800)
        prid = sql("SELECT TOP 1 Id FROM PurchaseRequests WHERE Title='Integration Test Servers'")
        check(prid.isdigit() and sql(f"SELECT Status FROM PurchaseRequests WHERE Id={prid}") == "Submitted", "Department submitted a purchase request (status Submitted in SQL)", f"PR {prid}")
        for _ in range(16):
            if po.locator(".ws-bell-badge").is_visible() and po.locator(".ws-bell-badge").inner_text() == str(o_before + 1): break
            po.wait_for_timeout(500)
        check(po.locator(".ws-bell-badge").inner_text() == str(o_before + 1), "Sourcing Officer's bell shows the new request live")

        # 2. Officer reviews and accepts it (Sourcing Officer module)
        po.goto(BASE + f"/SourcingOfficer/PurchaseRequests/Review/{prid}"); po.wait_for_timeout(500)
        po.fill("[name=comment]", "Accepted: specifications are clear."); po.screenshot(path=os.path.join(OUT, "02-officer-review-request-dark.png"))
        po.locator("button[name=decision][value=Accepted]").first.click(); po.wait_for_load_state("networkidle"); po.wait_for_timeout(800)
        check(sql(f"SELECT Status FROM PurchaseRequests WHERE Id={prid}") == "Accepted", "Officer accepted the request (SQL status Accepted)")
        pd.goto(BASE + f"/Department/PurchaseRequests/Details/{prid}"); pd.wait_for_timeout(500)
        check("Accepted" in pd.inner_text("main"), "Department sees the request as Accepted")
        check(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid('Department')}' AND Link LIKE '%/PurchaseRequests/Details/{prid}%'")) >= 1, "Department was notified of the decision (link to the request)")
        check(sql(f"SELECT COUNT(*) FROM PurchaseRequestReviews WHERE PurchaseRequestId={prid}") == "1" and sql(f"SELECT Decision FROM PurchaseRequestReviews WHERE PurchaseRequestId={prid}") == "Accepted", "One review row, completed with the Officer decision (no orphan Pending row)")

        # 3. Officer prepares the tender and assigns the committee by name (pickers, not user ids)
        po.goto(BASE + f"/SourcingOfficer/Workflow/Create/{prid}"); po.wait_for_timeout(600)
        sel = po.locator("select#Input_SourcingManagerId, select[name=SourcingManagerId]")
        check(sel.count() == 1 and sel.locator("option").count() >= 2, "Tender form offers the Sourcing Manager as a picker", str(sel.locator("option").all_inner_texts()[:2]))
        def fillv(name, v): po.fill(f"[name={name}]", v)
        fillv("Title", "Integration Test Tender"); fillv("Description", "Servers for the data center."); fillv("TechnicalRequirements", "Rack mount, 64GB RAM.")
        fillv("CommercialConditions", "Fixed price in JOD."); fillv("DeliveryExpectations", "Within 45 days."); fillv("MandatoryCompliance", "Commercial registration and warranty.")
        fillv("EstimatedValue", "30000"); po.fill("[name=Currency]", "JOD")
        mgr = uid("SourcingManager"); e1, e2, e3 = uid("EvaluationCommittee"), uid("EvaluationCommittee2"), uid("EvaluationCommittee3")
        po.select_option("[name=SourcingManagerId]", mgr); po.select_option("[name=DepartmentRepresentativeId]", e1); po.select_option("[name=FinancialRepresentativeId]", e2); po.select_option("[name=TechnicalRepresentativeId]", e3)
        po.screenshot(path=os.path.join(OUT, "03-officer-prepare-tender-dark.png"))
        po.click("button:has-text('Save tender requirements')"); po.wait_for_load_state("networkidle"); po.wait_for_timeout(800)
        tid = sql("SELECT TOP 1 Id FROM Tenders WHERE Title='Integration Test Tender'")
        check(tid.isdigit(), "Officer created the tender from the accepted request", f"tender {tid}")
        check(sql(f"SELECT COUNT(*) FROM CommitteeMembers WHERE TenderId={tid} AND CommitteeType='Evaluation'") == "4", "Four Evaluation Committee seats assigned")
        check(sql(f"SELECT COUNT(*) FROM TenderItems WHERE TenderId={tid}") == "1", "Request items were copied into the tender")
        po.screenshot(path=os.path.join(OUT, "04-tender-created-dark.png"))

        po.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); po.wait_for_timeout(500)
        po.locator("form[action*=RequestWeights] button").first.click(); po.wait_for_load_state("networkidle"); po.wait_for_timeout(800)
        # 4. Committee members and the Manager see it
        for area in ("SourcingManager", "EvaluationCommittee2"):
            c = ctx(); pg = c.new_page(); login(pg, area); pg.goto(BASE + "/Workflow"); pg.wait_for_timeout(600)
            check("Integration Test Tender" in pg.inner_text("main"), f"{area} sees the new tender in Weighted Evaluation")
            check(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid(area)}' AND Link LIKE '%/Workflow/Details/{tid}'")) >= 1, f"{area} was notified (weight request) after the Officer started the tender workflow")
            c.close()
        # 5. Supplier isolation: suppliers see nothing of this tender or request
        cs = ctx(); ps = cs.new_page(); login(ps, "Supplier"); ps.goto(BASE + "/Supplier/Invitations"); ps.wait_for_timeout(500)
        check("Integration Test Tender" not in ps.inner_text("main"), "Suppliers cannot see the tender before they are invited")
        # 6. Reports reflect the new records
        ct = ctx(); pt = ct.new_page(); login(pt, "TopCommittee"); pt.goto(BASE + "/Analytics/PurchaseRequests"); pt.wait_for_timeout(500)
        facts = dict(zip(pt.locator("dl.ui-facts dt").all_text_contents(), pt.locator("dl.ui-facts dd").all_text_contents()))
        check(facts["Total requests"] == sql("SELECT COUNT(*) FROM PurchaseRequests"), "Reports pick up the new request immediately", str(facts.get("Total requests")))
        b.close()
finally:
    stop_server(proc)
print(f"RESULT: {sum(res)}/{len(res)} passed")
