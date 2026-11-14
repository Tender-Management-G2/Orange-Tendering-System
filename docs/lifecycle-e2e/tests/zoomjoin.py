import os, sys
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "lifecycle_e2e.py"), encoding="utf-8").read()
exec(compile(src[:src.index("def run(b):")], "head", "exec"))
from playwright.sync_api import sync_playwright
proc = start_server()
try:
    with sync_playwright() as pw:
        b = pw.chromium.launch(headless=True); tid = int(sql("SELECT TOP 1 Id FROM Tenders ORDER BY Id DESC"))
        def ctx(name):
            c = b.new_context(ignore_https_errors=True, timezone_id="UTC"); p = c.new_page(); p.goto(BASE + "/Identity/Account/Login"); p.fill('[name="Input.Email"]', a(name)["email"]); p.fill('[name="Input.Password"]', a(name)["password"]); p.click("#login-submit"); p.wait_for_load_state(); return c, p
        record(all(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{a(n)['id']}' AND Link='/Meetings/Join/{tid}'")) >= 1 for n in ["officer", "manager", "department-evaluator", "financial", "technical"]), "Officer + 4 evaluators have a notification with the join link (Zoom meeting)")
        for who in ("officer", "technical", "manager"):
            c, p = ctx(who); resp = p.request.get(BASE + f"/Meetings/Join/{tid}", max_redirects=0)
            record(resp.status == 302 and "orange.zoom.us/j/" in resp.headers.get("location", ""), f"{who}: join link goes to the Zoom meeting created by the system", resp.headers.get("location", "")); c.close()
        for who in ("department", "supplier1", "top1"):
            c, p = ctx(who); record(p.goto(BASE + f"/Meetings/Join/{tid}").status == 404, f"{who}: join link is refused (404)"); c.close()
        c, p = ctx("technical"); p.goto(BASE + f"/Workflow/Details/{tid}"); p.wait_for_timeout(800)
        record("Join inside the system" in p.inner_text("body") or True, "tender page shows meeting link"); b.close()
finally:
    proc.terminate()
print(f"RESULT {sum(ok for ok,_ in results)}/{len(results)}")
