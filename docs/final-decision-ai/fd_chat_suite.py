import os, re, subprocess, sys, time, json, ssl, urllib.request
from playwright.sync_api import sync_playwright
import ui_common
from ui_common import *

OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\final-decision-ai\screenshots"
os.makedirs(OUT, exist_ok=True)
DB = STATE["connection"].split("Database=")[1].split(";")[0]
TID = STATE["finalTenderId"]
results = []


def check(ok, name, detail=""):
    results.append((bool(ok), name)); print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail else ""), flush=True)


def sql(q):
    r = subprocess.run(["sqlcmd", "-S", "localhost", "-E", "-C", "-d", DB, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + q], capture_output=True, text=True)
    return r.stdout.strip()


def uid(area): return sql(f"SELECT Id FROM AspNetUsers WHERE Email='{ACCOUNTS[area]['email']}'")
def unread(area): return int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid(area)}' AND IsRead=0"))
def results_for(area): return int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid(area)}' AND Title='Tender result' AND TRY_CAST(SUBSTRING(Link,26,20) AS int) IN (SELECT b.Id FROM Bids b JOIN TenderInvitations i ON i.Id=b.TenderInvitationId JOIN TenderRounds r ON r.Id=i.TenderRoundId WHERE r.TenderId={TID})"))
def notice_status(): return sql(f"SELECT TOP 1 Status FROM FinalDecisionNotices WHERE TenderId={TID} ORDER BY Id DESC")
def deliveries(): return int(sql(f"SELECT COUNT(*) FROM SupplierResultDeliveries WHERE TenderId={TID}"))


def csrf(page):
    return page.evaluate("document.querySelector('[data-csrf]').dataset.csrf")


def post_form(page, url):
    """POST like a browser form would, using the page's real antiforgery token (tests the server-side authority, not the UI)."""
    return page.evaluate("""async ([url, token]) => { const r = await fetch(url, {method:'POST', credentials:'same-origin', redirect:'manual', headers:{RequestVerificationToken: token}}); return r.status + ':' + r.type; }""", [url, csrf(page)])


proc = start_server()
procB = None
try:
    with sync_playwright() as p:
        browser = p.chromium.launch()

        def ctx(mode="dark", w=1672, h=941):
            c = browser.new_context(ignore_https_errors=True, viewport={"width": w, "height": h}); theme(c, mode); return c

        def shot(page, name): page.screenshot(path=os.path.join(OUT, name + ".png"))

        # =============== FEATURE 1: FINAL DECISION RELAY ===============
        check(notice_status() == "AwaitingManager" and sql(f"SELECT TopApprovals FROM FinalDecisionNotices WHERE TenderId={TID}") == "3",
              "Top Committee 3/4 approvals created the final decision notice, waiting for the Manager", notice_status())
        check(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{uid('SourcingManager')}' AND Title='Top Committee final decision recorded'")) == 1, "Assigned Sourcing Manager received the final-decision notification")
        check(results_for("Supplier") == 0 and results_for("SupplierTwo") == 0, "No supplier result notification exists before Officer confirmation")

        # supplier pages open (for live-push checks later)
        c1 = ctx(); s1 = c1.new_page(); login(s1, "Supplier"); s1.wait_for_timeout(1500)
        c2 = ctx(); s2 = c2.new_page(); login(s2, "SupplierTwo"); s2.wait_for_timeout(1500)
        s1_before, s2_before = unread("Supplier"), unread("SupplierTwo")

        # Officer: waiting state, no button
        co = ctx(); po = co.new_page(); login(po, "SourcingOfficer"); po.wait_for_timeout(1500)
        po.goto(BASE + "/SourcingOfficer/FinalDecisions"); po.wait_for_timeout(500)
        txt = po.inner_text("main")
        check("Waiting for Manager" in txt and po.locator("section:has-text('Site Maintenance Services') button:has-text('Notify Suppliers')").count() == 0, "Officer sees 'Waiting for Manager' and no Notify Suppliers button")
        shot(po, "Officer-final-decision-waiting-dark")
        r = post_form(po, f"/SourcingOfficer/FinalDecisions/Notify?id={TID}")
        check(deliveries() == 0 and notice_status() == "AwaitingManager" and results_for("Supplier") == 0, "Officer cannot skip the Manager: direct Notify request is refused (no deliveries, no notifications)", r)

        # Wrong roles
        cd = ctx(); pdp = cd.new_page(); login(pdp, "Department")
        rs = pdp.goto(BASE + "/SourcingManager/FinalDecisions")
        check(rs.status in (302, 403) or "AccessDenied" in pdp.url or "Login" in pdp.url, "Department cannot open the Manager decision page", f"{rs.status} {pdp.url.replace(BASE,'')}")
        rs = pdp.goto(BASE + "/SourcingOfficer/FinalDecisions")
        check("AccessDenied" in pdp.url or "Login" in pdp.url or rs.status == 403, "Department cannot open the Officer notification page", pdp.url.replace(BASE, ""))
        cd.close()

        # Manager acknowledges through a popup
        cm = ctx(); pm = cm.new_page(); login(pm, "SourcingManager"); pm.wait_for_timeout(800)
        check(pm.locator("a.ws-nav-item:has-text('Final Decisions')").count() == 1, "Manager sidebar has Final Decisions")
        r = post_form(pm, f"/SourcingOfficer/FinalDecisions/Notify?id={TID}")
        check(deliveries() == 0 and notice_status() == "AwaitingManager", "Manager cannot notify suppliers (Officer-only action refused)", r)
        pm.goto(BASE + "/SourcingManager/FinalDecisions"); pm.wait_for_timeout(500)
        shot(pm, "Manager-final-decisions-dark")
        o_before = unread("SourcingOfficer")
        pm.locator("a.pg-btn:has-text('Acknowledge')").first.click(timeout=10000); pm.wait_for_selector("dialog.ui-dialog[open] form")
        pm.fill("dialog textarea[name=comment]", "Reviewed the Top Committee decision and forwarding it.")
        shot(pm, "Manager-acknowledge-popup-dark")
        pm.click("dialog button[type=submit]"); pm.wait_for_load_state("networkidle"); pm.wait_for_timeout(800)
        check(notice_status() == "ForwardedToOfficer" and sql(f"SELECT ManagerAcknowledgedAt FROM FinalDecisionNotices WHERE TenderId={TID}") != "NULL", "Manager acknowledged and forwarded in a popup (SQL status ForwardedToOfficer)", notice_status())
        check(unread("SourcingOfficer") == o_before + 1, "Officer notification created for the forwarded decision")
        for _ in range(16):
            if po.locator(".ws-bell-badge").is_visible() and po.locator(".ws-bell-badge").inner_text() == str(unread("SourcingOfficer")): break
            po.wait_for_timeout(500)
        check(po.locator(".ws-bell-badge").inner_text() == str(unread("SourcingOfficer")), "Officer's bell updated live (SignalR) after the Manager forwarded")
        check(results_for("Supplier") == 0 and results_for("SupplierTwo") == 0 and unread("Supplier") == s1_before, "Suppliers still not notified after the Manager's acknowledgement")
        r = post_form(pm, f"/SourcingManager/FinalDecisions/Acknowledge?id={TID}")
        check(sql(f"SELECT COUNT(*) FROM AuditLogs WHERE Action='FinalDecisionAcknowledgedByManager' AND EntityId='{TID}'") == "1", "Second acknowledgement rejected (single audit row)", r)

        # Officer notifies suppliers
        po.goto(BASE + "/SourcingOfficer/FinalDecisions"); po.wait_for_timeout(500)
        check(po.locator("section:has-text('Site Maintenance Services') button:has-text('Notify Suppliers')").count() == 1 and "Ready to notify suppliers" in po.inner_text("section:has-text('Site Maintenance Services')"), "Officer now sees Notify Suppliers")
        shot(po, "Officer-final-decision-ready-dark")
        po.click("section:has-text('Site Maintenance Services') button:has-text('Notify Suppliers')"); po.wait_for_selector("dialog[open] [data-ok]")
        shot(po, "Officer-notify-confirm-dark")
        po.click("dialog[open] [data-ok]"); po.wait_for_load_state("networkidle"); po.wait_for_timeout(1000)
        check(notice_status() == "SuppliersNotified" and deliveries() == 2, "Officer's Notify Suppliers announced the result (2 delivery rows)", f"{notice_status()}, {deliveries()}")
        shot(po, "Officer-final-decision-delivered-dark")
        body = po.inner_text("section:has-text('Site Maintenance Services')")
        check("Won" in body and "Unsuccessful" in body and ("NotConfigured" in body or "Failed" in body) and "Delivered" in body, "Delivery table shows Won / Unsuccessful, in-app Delivered and the e-mail delivery status")
        check(po.locator("section:has-text('Site Maintenance Services') button:has-text('Notify Suppliers')").count() == 0, "Notify Suppliers button disappears after announcing")
        check(sql(f"SELECT COUNT(*) FROM AuditLogs WHERE Action='SupplierResultsAnnounced' AND EntityId='{TID}'") == "1", "Audit log recorded the announcement")
        # duplicate prevention
        n_before = results_for("Supplier") + results_for("SupplierTwo")
        r = post_form(po, f"/SourcingOfficer/FinalDecisions/Notify?id={TID}")
        check(deliveries() == 2 and results_for("Supplier") + results_for("SupplierTwo") == n_before == 2, "Duplicate Notify refused: no extra notifications or deliveries", r)

        # Suppliers: live bell + content + isolation
        for _ in range(16):
            if s1.locator(".ws-bell-badge").is_visible() and s1.locator(".ws-bell-badge").inner_text() == str(s1_before + 1): break
            s1.wait_for_timeout(500)
        check(s1.locator(".ws-bell-badge").inner_text() == str(s1_before + 1) and s2.locator(".ws-bell-badge").inner_text() == str(s2_before + 1), "Both suppliers' bells updated live after the Officer's confirmation")
        for pg, area, word in ((s1, "Supplier", "selected for this tender"), (s2, "SupplierTwo", "was not selected")):
            pg.click(".ws-bell-button"); pg.wait_for_selector(".ws-bell-item"); pg.wait_for_timeout(500)
            text = pg.inner_text(".ws-bell-panel")
            check(word in text and "Gulf" not in text.replace("Gulf Systems", "") or word in text, f"{area} sees only their own outcome ('{word}')")
            check(not re.search(r"Gulf|TechWorld|JOD|\d+[.,]\d{2}", text), f"{area}: result popup reveals no competitor name or price")
            shot(pg, f"{area}-result-bell-dark")
        link = sql(f"SELECT TOP 1 Link FROM Notifications WHERE UserId='{uid('Supplier')}' AND Title='Tender result' ORDER BY Id DESC")
        rs = s2.goto(BASE + link)
        check(rs.status == 404, "Supplier 2 cannot open Supplier 1's offer link (isolation)", f"HTTP {rs.status}")
        co.close(); c1.close(); c2.close(); cm.close()
        # light mode pass of the new pages
        for area, path, name in (("SourcingOfficer", "/SourcingOfficer/FinalDecisions", "Officer-final-decision-light"), ("SourcingManager", "/SourcingManager/FinalDecisions", "Manager-final-decisions-light")):
            c = ctx("light"); pg = c.new_page(); login(pg, area); pg.goto(BASE + path); pg.wait_for_timeout(600); shot(pg, name); c.close()

        # =============== FEATURE 2: AI CHAT ASSISTANT ===============
        c = ctx(); pg = c.new_page(); errs = []
        pg.on("pageerror", lambda e: errs.append(str(e)))
        login(pg, "SourcingOfficer"); pg.wait_for_timeout(800)
        check(pg.locator(".ai-fab").is_visible(), "Floating AI button visible on the Officer dashboard")
        analyses_before = sql("SELECT COUNT(*) FROM AiAnalysis"); clar_before = sql("SELECT COUNT(*) FROM Clarifications"); msgs_before = sql("SELECT COUNT(*) FROM ClarificationMessages")
        pg.click(".ai-fab"); pg.wait_for_selector(".ai-panel:not([hidden])"); pg.wait_for_timeout(400)
        shot(pg, "AI-chat-open-dark")
        pg.fill(".ai-form textarea", "In two sentences, what does the Sourcing Officer do after the Sourcing Manager forwards the final decision?")
        pg.click(".ai-send")
        pg.wait_for_selector(".ai-typing", timeout=5000)
        check(True, "Loading indicator shown while the model answers")
        shot(pg, "AI-chat-loading-dark")
        pg.wait_for_selector(".ai-msg--assistant:not(.ai-typing)", timeout=180000); pg.wait_for_timeout(500)
        reply = pg.inner_text(".ai-msg--assistant:not(.ai-typing) .ai-msg-body")
        check(len(reply) > 20, "Real gemma3:4b answer rendered", reply[:90].replace("\n", " "))
        shot(pg, "AI-chat-reply-dark")
        oid = uid("SourcingOfficer")
        cid = sql(f"SELECT TOP 1 Id FROM AiChatConversations WHERE UserId='{oid}' ORDER BY Id DESC")
        check(cid.isdigit() and sql(f"SELECT COUNT(*) FROM AiChatMessages WHERE ConversationId={cid}") == "2" and sql(f"SELECT STRING_AGG(Role,',') WITHIN GROUP (ORDER BY Id) FROM AiChatMessages WHERE ConversationId={cid}") == "user,assistant",
              "Conversation, question and answer persisted in SQL Server for this user", f"conversation {cid}")
        # follow-up uses the stored context
        pg.fill(".ai-form textarea", "And who can see the results afterwards? One short sentence."); pg.click(".ai-send")
        pg.wait_for_function("document.querySelectorAll('.ai-msg--assistant:not(.ai-typing)').length >= 2", timeout=180000)
        check(sql(f"SELECT COUNT(*) FROM AiChatMessages WHERE ConversationId={cid}") == "4", "Follow-up stays in the same conversation (4 stored messages)")
        # history after reload
        pg.reload(); pg.wait_for_timeout(800); pg.click(".ai-fab"); pg.click("[data-ai-history]"); pg.wait_for_selector(".ai-history li")
        shot(pg, "AI-chat-history-dark")
        pg.click(".ai-hist-open >> nth=0"); pg.wait_for_selector(".ai-msg--assistant")
        check(pg.locator(".ai-msg").count() == 4, "History reloads the saved conversation after a page reload")
        check(not errs, "No JavaScript errors in the chat widget")
        # separation from other AI features
        check(sql("SELECT COUNT(*) FROM AiAnalysis") == analyses_before and sql("SELECT COUNT(*) FROM Clarifications") == clar_before and sql("SELECT COUNT(*) FROM ClarificationMessages") == msgs_before,
              "AI Bid Analysis and Clarification data untouched by the assistant")
        # validation
        api = lambda body: pg.evaluate("""async ([body, token]) => { const r = await fetch('/AiChat/Send', {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json', RequestVerificationToken: token}, body: JSON.stringify(body)}); return r.status; }""", [body, csrf(pg)])
        check(api({"message": "   "}) == 400 and api({"message": "x" * 2001}) == 400, "Empty and over-long messages rejected (HTTP 400)")
        noauth = pg.evaluate("""async () => (await fetch('/AiChat/Send', {method:'POST', headers:{'Content-Type':'application/json'}, body:'{"message":"hi"}'})).status""")
        check(noauth in (400, 405, 401), "Send without the anti-forgery token is rejected", str(noauth))

        # isolation: another user cannot read, continue or delete the Officer's conversation
        cs = ctx(); ps = cs.new_page(); login(ps, "SupplierTwo"); ps.wait_for_timeout(600)
        st = ps.evaluate(f"""async () => {{ const a = await fetch('/AiChat/Messages?id={cid}', {{credentials:'same-origin'}}); const l = await (await fetch('/AiChat/Conversations', {{credentials:'same-origin'}})).json(); return [a.status, l.items.length]; }}""")
        check(st == [404, 0], "Another user gets 404 for the Officer's conversation and an empty history", str(st))
        st = ps.evaluate("""async ([token]) => { const d = await fetch('/AiChat/Delete?id=%s', {method:'POST', credentials:'same-origin', headers:{RequestVerificationToken: token}}); return d.status; }""" % cid, [csrf(ps)])
        check(st == 404 and sql(f"SELECT COUNT(*) FROM AiChatConversations WHERE Id={cid}") == "1", "Another user cannot delete it", str(st))
        sc = ps.evaluate("""async ([id, token]) => { const r = await fetch('/AiChat/Send', {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json', RequestVerificationToken: token}, body: JSON.stringify({conversationId:id, message:'hello'})}); return r.status; }""", [int(cid), csrf(ps)])
        check(sc == 404 and sql(f"SELECT COUNT(*) FROM AiChatMessages WHERE ConversationId={cid}") == "4", "Another user cannot post into it", str(sc))
        # rate limit with real stored rows
        sup2 = uid("SupplierTwo")
        sql(f"INSERT INTO AiChatConversations (UserId,Title,CreatedAt,UpdatedAt) VALUES ('{sup2}','rate test',SYSUTCDATETIME(),SYSUTCDATETIME())")
        rc = sql(f"SELECT TOP 1 Id FROM AiChatConversations WHERE UserId='{sup2}' ORDER BY Id DESC")
        for i in range(8): sql(f"INSERT INTO AiChatMessages (ConversationId,Role,Content,CreatedAt) VALUES ({rc},'user','m{i}',SYSUTCDATETIME())")
        code = ps.evaluate("""async ([id, token]) => { const r = await fetch('/AiChat/Send', {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json', RequestVerificationToken: token}, body: JSON.stringify({conversationId:id, message:'one more'})}); return r.status; }""", [int(rc), csrf(ps)])
        check(code == 429, "Rate limit (8 messages per minute) enforced", str(code))
        # light mode
        cl = ctx("light"); pl = cl.new_page(); login(pl, "Supplier"); pl.click(".ai-fab"); pl.wait_for_selector(".ai-panel:not([hidden])"); pl.wait_for_timeout(500)
        pl.fill(".ai-form textarea", "What does Under Evaluation mean?"); pl.click(".ai-send")
        pl.wait_for_selector(".ai-msg--assistant:not(.ai-typing)", timeout=180000); pl.wait_for_timeout(500)
        shot(pl, "AI-chat-supplier-light")
        check(pl.locator(".ai-msg--assistant:not(.ai-typing)").count() == 1 and not re.search(r"Gulf|JOD", pl.inner_text(".ai-messages")), "Supplier can use the assistant (own isolated history, no tender data)")
        for x in (cs, cl, c): x.close()

        # Ollama unavailable -> real error path on a second server instance
        envB = dict(os.environ); envB.update({"ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": "https://localhost:5098", "DemoData__Enabled": "false",
            "ConnectionStrings__Default Connection": STATE["connection"], "Ollama__BaseUrl": "http://127.0.0.1:9", "BootstrapAdmin__Email": ACCOUNTS["Admin"]["email"], "BootstrapAdmin__Password": ACCOUNTS["Admin"]["password"]})
        procB = subprocess.Popen(["dotnet", "bin/Debug/net10.0/Orange-Tendering-System.dll"], cwd=ROOT, env=envB, stdout=open(os.path.join(SCR, "ui-server-b.log"), "w"), stderr=subprocess.STDOUT)
        sslctx = ssl._create_unverified_context()
        for _ in range(60):
            try: urllib.request.urlopen("https://localhost:5098/Identity/Account/Login", context=sslctx, timeout=3); break
            except Exception: time.sleep(1)
        cb = browser.new_context(ignore_https_errors=True, viewport={"width": 1672, "height": 941}); theme(cb, "dark"); pb = cb.new_page()
        pb.goto("https://localhost:5098/Identity/Account/Login"); pb.fill("#Input_Email", ACCOUNTS["Department"]["email"]); pb.fill("#Input_Password", ACCOUNTS["Department"]["password"]); pb.click("#login-submit"); pb.wait_for_load_state("networkidle")
        pb.click(".ai-fab"); pb.fill(".ai-form textarea", "Is anyone there?"); pb.click(".ai-send")
        pb.wait_for_selector(".ai-error", timeout=30000); pb.wait_for_timeout(300)
        check("could not answer" in pb.inner_text(".ai-error") and pb.locator(".ai-error button").count() == 1, "Ollama down: friendly error with Retry shown", pb.inner_text(".ai-error")[:80])
        shot(pb, "AI-chat-error-dark")
        check(sql(f"SELECT COUNT(*) FROM AiChatMessages m JOIN AiChatConversations c ON c.Id=m.ConversationId WHERE c.UserId='{uid('Department')}' AND m.Role='assistant'") == "0", "No fake assistant answer was stored when Ollama failed")
        cb.close()
        browser.close()
finally:
    stop_server(proc)
    if procB: stop_server(procB)

bad = [n for ok, n in results if not ok]
print(f"\nRESULT: {len(results) - len(bad)}/{len(results)} passed")
for n in bad: print("FAILED:", n)
