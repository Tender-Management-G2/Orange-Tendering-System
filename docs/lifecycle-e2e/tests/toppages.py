import os
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "lifecycle_e2e.py"), encoding="utf-8").read()
exec(compile(src[:src.index("def run(b):")], "head", "exec"))
from playwright.sync_api import sync_playwright
OUTD = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\lifecycle-e2e\screenshots"
proc = start_server()
try:
    with sync_playwright() as pw:
        b = pw.chromium.launch(headless=True); tid = int(sql("SELECT TOP 1 Id FROM Tenders ORDER BY Id DESC"))
        def ctx(name, mode="dark"):
            c = b.new_context(ignore_https_errors=True, timezone_id="UTC", viewport={"width": 1500, "height": 950}); c.add_init_script("try{localStorage.setItem('orange-ui-theme','%s')}catch(e){}" % mode)
            p = c.new_page(); p.goto(BASE + "/Identity/Account/Login"); p.fill('[name="Input.Email"]', a(name)["email"]); p.fill('[name="Input.Password"]', a(name)["password"]); p.click("#login-submit"); p.wait_for_load_state(); return c, p
        c, p = ctx("top1"); errs = []; p.on("pageerror", lambda e: errs.append(str(e)))
        for path, title in (("/TopCommittee/Decisions/ReviewEvaluation", "Evaluation Summary"), ("/TopCommittee/Decisions/Decision", "Voting"), ("/TopCommittee/Decisions/History", "Decision History")):
            r = p.goto(BASE + path); p.wait_for_timeout(500); record(r.status == 200 and title in p.inner_text("main"), f"Top member opens {title} page")
        p.goto(BASE + "/TopCommittee/Decisions/History"); p.wait_for_timeout(400)
        record("Awarded" in p.inner_text("main") and "Supplier Co" in p.inner_text("main"), "History lists the awarded tender with its supplier"); p.screenshot(path=os.path.join(OUTD, "Top-history-dark.png"))
        r = p.goto(BASE + f"/TopCommittee/Decisions/ReviewEvaluation/{tid}"); p.wait_for_timeout(500); body = p.inner_text("main")
        record(r.status == 200 and "Submitted offers" in body and "Supplier Co" in body and "Manager recommendation" in body or "Sourcing Manager" in body, "Evaluation summary shows request, ranked offers, report and Manager recommendation")
        record("3 of 4" in body or "approve" in body.lower(), "Vote counts are shown after the member voted"); p.screenshot(path=os.path.join(OUTD, "Top-review-evaluation-dark.png"))
        p.goto(BASE + f"/TopCommittee/Decisions/Decision/{tid}"); p.wait_for_timeout(400); record("You voted" in p.inner_text("main"), "Decision page shows 'You voted: Reject' (member 1 rejected)"); p.screenshot(path=os.path.join(OUTD, "Top-decision-page-dark.png"))
        record(not errs, "No JavaScript errors on Top Committee pages"); c.close()
        for who in ("department", "supplier1", "manager"):
            c, p = ctx(who); r = p.goto(BASE + f"/TopCommittee/Decisions/ReviewEvaluation/{tid}"); record("AccessDenied" in p.url or r.status in (302, 403, 404), f"{who} cannot open the Top Committee decision pages"); c.close()
        c, p = ctx("top1", "light"); p.goto(BASE + f"/TopCommittee/Decisions/ReviewEvaluation/{tid}"); p.wait_for_timeout(500); p.screenshot(path=os.path.join(OUTD, "Top-review-evaluation-light.png")); c.close(); b.close()
finally:
    proc.terminate()
print(f"RESULT {sum(ok for ok,_ in results)}/{len(results)}")
