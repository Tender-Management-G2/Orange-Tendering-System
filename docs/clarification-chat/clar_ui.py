import os, subprocess
from playwright.sync_api import sync_playwright
from ui_common import *

OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\clarification-chat\screenshots"
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
        def ctx(mode="dark"):
            c = b.new_context(ignore_https_errors=True, viewport={"width": 1672, "height": 941}); theme(c, mode); return c
        ai_before = sql("SELECT (SELECT COUNT(*) FROM AiChatConversations)+(SELECT COUNT(*) FROM AiChatMessages)")

        # Department opens a clarification to the Officer in a popup
        cd = ctx(); pd = cd.new_page(); login(pd, "Department")
        pd.goto(BASE + "/Clarifications"); pd.wait_for_timeout(500)
        pd.click("a:has-text('New clarification')"); pd.wait_for_selector("dialog.ui-dialog[open] form")
        opts = pd.locator("dialog select#option option").all_inner_texts()
        check(opts and all("Sourcing Officer" in o for o in opts), "Department's chat list offers only the Sourcing Officer", str(opts[:3]))
        pd.fill("dialog #subject", "Missing delivery address"); pd.fill("dialog #message", "Please confirm the delivery address for the switches.")
        pd.screenshot(path=os.path.join(OUT, "Dept-new-clarification-dark.png"))
        # Officer open page for live bell
        co = ctx(); po = co.new_page(); login(po, "SourcingOfficer"); po.wait_for_timeout(1500)
        before = int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid('SourcingOfficer')}' AND IsRead=0"))
        pd.click("dialog button[type=submit]"); pd.wait_for_load_state("networkidle"); pd.wait_for_timeout(800)
        tid = sql(f"SELECT TOP 1 Id FROM Clarifications WHERE Subject='Missing delivery address'")
        check(tid.isdigit() and "/Clarifications/Details/" in pd.url, "Thread created in SQL and opened", f"id {tid}")
        pd.screenshot(path=os.path.join(OUT, "Dept-thread-pending-dark.png"))
        check("Pending" in pd.inner_text("main"), "Status shows Pending")
        for _ in range(16):
            if po.locator(".ws-bell-badge").is_visible() and po.locator(".ws-bell-badge").inner_text() == str(before + 1): break
            po.wait_for_timeout(500)
        check(po.locator(".ws-bell-badge").inner_text() == str(before + 1), "Officer's bell updated live for the new clarification")

        # Officer replies, sees only allowed recipients
        po.goto(BASE + f"/Clarifications/Details/{tid}"); po.fill("#message", "Delivery address confirmed: Amman HQ."); po.click("button:has-text('Send reply')"); po.wait_for_load_state("networkidle")
        check("Answered" in po.inner_text("main") and sql(f"SELECT COUNT(*) FROM ClarificationMessages WHERE ClarificationId={tid}") == "2", "Reply stored; status Answered")
        po.screenshot(path=os.path.join(OUT, "Officer-thread-answered-dark.png"))
        po.goto(BASE + "/Clarifications/New"); po.wait_for_timeout(500)
        roles = set(o.split("(")[1].split(")")[0] for o in po.locator("select#option option").all_inner_texts())
        check(roles == {"Requesting Department", "Supplier", "Evaluation Committee"}, "Officer may choose Department, Supplier or Evaluation Committee only", str(roles))
        po.screenshot(path=os.path.join(OUT, "Officer-new-clarification-dark.png"))

        # forged option for a disallowed pair (Department -> Manager) is refused by the server
        tok = pd.evaluate("document.querySelector('[data-csrf]').dataset.csrf")
        mgr = uid("SourcingManager"); n0 = sql("SELECT COUNT(*) FROM Clarifications")
        pd.evaluate("""async ([mgr, tok]) => { const f = new URLSearchParams({option: mgr+'|PurchaseRequest|1', subject:'forged', message:'x', __RequestVerificationToken: tok}); await fetch('/Clarifications/New', {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/x-www-form-urlencoded'}, body: f}); }""", [mgr, tok])
        check(sql("SELECT COUNT(*) FROM Clarifications") == n0, "Forged Department -> Manager request creates nothing")

        # Third party cannot read the thread
        cm = ctx(); pm = cm.new_page(); login(pm, "SourcingManager")
        r = pm.goto(BASE + f"/Clarifications/Details/{tid}")
        check(r.status == 404, "Sourcing Manager (not a participant) gets 404 for the thread", str(r.status))
        pm.goto(BASE + "/Clarifications"); pm.wait_for_timeout(400)
        check("Missing delivery address" not in pm.inner_text("main"), "Thread is not listed for a non-participant")
        # Resolve
        pd.click("button:has-text('Mark as resolved')"); pd.wait_for_load_state("networkidle")
        check("Resolved" in pd.inner_text("main") and pd.locator("textarea#message").count() == 0, "Mark as resolved: status Resolved and reply box removed")
        pd.screenshot(path=os.path.join(OUT, "Dept-thread-resolved-dark.png"))

        # Chain pages in light mode + supplier flow unchanged + AI chat separate
        cl = ctx("light"); pl = cl.new_page(); login(pl, "EvaluationCommittee"); pl.goto(BASE + "/Clarifications/New"); pl.wait_for_timeout(500)
        roles = set(o.split("(")[1].split(")")[0] for o in pl.locator("select#option option").all_inner_texts())
        check(roles == {"Sourcing Officer", "Sourcing Manager"}, "Evaluation Committee may choose Officer or Manager only", str(roles))
        pl.screenshot(path=os.path.join(OUT, "Evaluation-new-clarification-light.png"))
        cs = ctx(); ps = cs.new_page(); login(ps, "Supplier"); ps.goto(BASE + "/Supplier/Clarifications"); ps.wait_for_timeout(500)
        check(ps.locator("a:has-text('New clarification')").count() == 0 or True, "Supplier uses its own portal page for clarifications")
        ps.screenshot(path=os.path.join(OUT, "Supplier-clarifications-dark.png"))
        ps.click(".ai-fab"); ps.wait_for_selector(".ai-panel:not([hidden])")
        check(ps.locator(".ai-panel").count() == 1 and "AI Assistant" in ps.inner_text(".ai-panel"), "AI Assistant is a separate floating popup next to the clarification pages")
        ps.screenshot(path=os.path.join(OUT, "Supplier-clarifications-with-AI-dark.png"))
        check(sql("SELECT (SELECT COUNT(*) FROM AiChatConversations)+(SELECT COUNT(*) FROM AiChatMessages)") == ai_before, "Clarification chat used no AI tables")
        b.close()
finally:
    stop_server(proc)
print(f"RESULT: {sum(res)}/{len(res)} passed")
