"""Full tender lifecycle through the REAL browser UI, ending in Top Committee voting + signatures, the Manager's acknowledgement,
Award and the Officer's Notify Suppliers. Zoom is exercised against a local fake Zoom OAuth/API server (the real ZoomMeetingService code,
only the HTTP endpoints are local). Ollama gemma3:4b is live."""
import json, os, re, subprocess, sys, threading, time, traceback, base64
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from playwright.sync_api import sync_playwright

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = r"C:\Users\user\Desktop\Orange-Tendering-System\Orange-Tendering-System"
OUT = r"C:\Users\user\Desktop\Orange-Tendering-System\docs\lifecycle-e2e\screenshots"
os.makedirs(OUT, exist_ok=True)
STATE = json.load(open(os.path.join(SCR, "lifecycle-state.json")))
DB = STATE["connection"].split("Database=")[1].split(";")[0]
BASE = "https://localhost:5099"
ACC = {a["name"]: a for a in STATE["accounts"]}
results = []
JITSI = os.environ.get("MEETING_MODE") == "jitsi"

# ---------------- fake Zoom (OAuth + meetings API) ----------------
zoom = {"token_ok": False, "calls": [], "meetings": {}}
class ZoomHandler(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def _send(self, code, obj):
        body = json.dumps(obj).encode(); self.send_response(code); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def do_POST(self):
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():
            body = b""
            while True:
                size = int(self.rfile.readline().strip() or b"0", 16)
                if size == 0: self.rfile.readline(); break
                body += self.rfile.read(size); self.rfile.readline()
        else:
            length = int(self.headers.get("Content-Length") or 0); body = self.rfile.read(length) if length else b""
        if self.path.startswith("/oauth/token"):
            zoom["calls"].append(("token", self.path, self.headers.get("Authorization")))
            if not zoom["token_ok"]: return self._send(401, {"reason": "Invalid client or secret"})
            return self._send(200, {"access_token": "fake-access-token", "token_type": "bearer", "expires_in": 3600})
        m = re.match(r"/v2/users/([^/]+)/meetings", self.path)
        if m and self.headers.get("Authorization") == "Bearer fake-access-token":
            data = json.loads(body); mid = str(81000000000 + len(zoom["meetings"]) + 1)
            zoom["meetings"][mid] = data; zoom["calls"].append(("create", m.group(1), data))
            return self._send(201, {"id": int(mid), "join_url": f"https://orange.zoom.us/j/{mid}", "topic": data.get("topic")})
        self._send(404, {})
    def do_DELETE(self): zoom["calls"].append(("delete", self.path, None)); self._send(204, {})
srv = HTTPServer(("127.0.0.1", 9911), ZoomHandler); threading.Thread(target=srv.serve_forever, daemon=True).start()

def record(ok, name, detail=""):
    results.append((bool(ok), name)); print(("PASS " if ok else "FAIL ") + name + (f"  [{str(detail)[:150]}]" if detail != "" else ""), flush=True)

def sql(q):
    r = subprocess.run(["sqlcmd", "-S", "localhost", "-E", "-C", "-b", "-d", DB, "-h", "-1", "-W", "-w", "65535", "-Q", "SET NOCOUNT ON; " + q], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode: raise RuntimeError(r.stdout + r.stderr)
    return r.stdout.strip()

def a(n): return ACC[n]
def local_after(seconds): return ((datetime.now(timezone.utc) + timedelta(seconds=seconds)).replace(second=0, microsecond=0) + timedelta(minutes=1)).strftime("%Y-%m-%dT%H:%M")
def wait_until(ts):
    while time.time() < ts: time.sleep(min(2, ts - time.time()))
def epoch(q): return int(sql(q))
def f(p, action): return p.locator('form[action*="/' + action + '/"]')
def fill(form, values):
    for k, v in values.items(): form.locator('[name="' + k + '"]').fill(str(v))
def send(form):
    for cb in form.locator("input[type=checkbox][required]").all(): cb.check()
    assert form.evaluate("(x)=>x.checkValidity()"), "Native browser form validation failed"
    form.locator("button[type=submit],button:not([type])").first.click()

def start_server():
    env = dict(os.environ); env.update({
        "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": BASE, "DemoData__Enabled": "false", "ConnectionStrings__Default Connection": STATE["connection"],
        "BootstrapAdmin__Email": a("admin")["email"], "BootstrapAdmin__Password": a("admin")["password"],
        })
    if not JITSI: env.update({"Zoom__AccountId": "acct-test", "Zoom__ClientId": "client-test", "Zoom__ClientSecret": "secret-test", "Zoom__HostUserId": "host@example.test",
        "Zoom__TokenUrl": "http://127.0.0.1:9911/oauth/token", "Zoom__ApiBaseUrl": "http://127.0.0.1:9911/v2/"})
    proc = subprocess.Popen(["dotnet", "bin/Debug/net10.0/Orange-Tendering-System.dll"], cwd=ROOT, env=env, stdout=open(os.path.join(SCR, "lifecycle-server.log"), "w"), stderr=subprocess.STDOUT)
    import ssl, urllib.request
    ctx = ssl._create_unverified_context()
    for _ in range(90):
        try: urllib.request.urlopen(BASE + "/Identity/Account/Login", context=ctx, timeout=3); return proc
        except Exception: time.sleep(1)
    raise RuntimeError("server did not start")

def run(b):
    def ctx(name):
        c = b.new_context(ignore_https_errors=True, viewport={"width": 1440, "height": 1000}, timezone_id="UTC")
        p = c.new_page(); p.goto(BASE + "/Identity/Account/Login"); p.fill('[name="Input.Email"]', a(name)["email"]); p.fill('[name="Input.Password"]', a(name)["password"]); p.click("#login-submit"); p.wait_for_load_state()
        return c, p
    def shot(p, name): p.screenshot(path=os.path.join(OUT, name + ".png"))

    # ---------------- 1. request -> tender ----------------
    RESUME = os.environ.get("RESUME") == "scheduling"
    if RESUME:
        tid = int(sql("SELECT TOP 1 Id FROM Tenders ORDER BY Id DESC")); analysis = sql("SELECT TOP 1 Id FROM AiAnalysis ORDER BY Id DESC")
        bids = [int(x) for x in sql(f"SELECT Id FROM Bids WHERE Status='Submitted' ORDER BY Id").split()]
        oc, op = ctx("officer"); op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}")
    else:
        dc, dp = ctx("department"); dp.goto(BASE + "/Department/PurchaseRequests/Create")
        dp.fill("#Title", "Lifecycle network procurement"); dp.fill("#NeededBy", "2027-04-01"); dp.fill("#Justification", "Complete end-to-end browser lifecycle test request.")
        row = dp.locator("#department-items tr").first
        row.locator("input").nth(0).fill("Network switch"); row.locator("textarea").fill("48 port managed switch"); row.locator("input[type=number]").fill("10"); row.locator("input").last.fill("pcs")
        dp.locator("button[value=submit]").click(); dp.wait_for_load_state(); pr = int(sql("SELECT TOP 1 Id FROM PurchaseRequests WHERE Title='Lifecycle network procurement'"))
        record(sql(f"SELECT Status FROM PurchaseRequests WHERE Id={pr}") == "Submitted", "Department submitted the purchase request", f"PR {pr}")
        oc, op = ctx("officer"); op.goto(BASE + f"/SourcingOfficer/PurchaseRequests/Review/{pr}"); op.fill("[name=comment]", "Accepted after reviewing all requested items")
        op.locator("button[name=decision][value=Accepted]").first.click(); op.wait_for_load_state()
        record(sql(f"SELECT Status FROM PurchaseRequests WHERE Id={pr}") == "Accepted", "Officer accepted the request")
        op.goto(BASE + f"/SourcingOfficer/Workflow/Create/{pr}")
        fill(op, {"Title": "Lifecycle Tender", "Description": "Real browser tender cycle", "TechnicalRequirements": "48 port switches", "CommercialConditions": "JOD fixed price", "DeliveryExpectations": "30 days",
                  "MandatoryCompliance": "Company registration and offer evidence", "EstimatedValue": "50000", "Currency": "JOD"})
        op.select_option("[name=PurchaseRequestId]", str(pr)); op.select_option("[name=SourcingManagerId]", a("manager")["id"]); op.select_option("[name=DepartmentRepresentativeId]", a("department-evaluator")["id"])
        op.select_option("[name=FinancialRepresentativeId]", a("financial")["id"]); op.select_option("[name=TechnicalRepresentativeId]", a("technical")["id"])
        op.get_by_role("button", name="Save tender requirements").click(); op.wait_for_load_state(); tid = int(op.url.rsplit("/", 1)[-1])
        record(sql(f"SELECT COUNT(*) FROM CommitteeMembers WHERE TenderId={tid} AND CommitteeType='Evaluation'") == "4", "Tender created with exactly four committee seats", f"tender {tid}")
        # weights
        send(f(op, "RequestWeights")); op.wait_for_load_state()
        for i, name in enumerate(["manager", "department-evaluator", "financial", "technical"]):
            c, p = ctx(name); p.goto(BASE + f"/EvaluationCommittee/Workflow/Details/{tid}"); form = f(p, "Propose")
            form.locator("[name=financial]").fill(str(30 + i * 10)); form.locator("[name=technical]").fill(str(70 - i * 10)); send(form); p.wait_for_load_state(); c.close()
        mc, mp = ctx("manager"); mp.goto(BASE + f"/EvaluationCommittee/Workflow/Details/{tid}")
        record("Financial 45%" in mp.inner_text("body"), "Four weight proposals averaged to 45 / 55")
        send(f(mp, "Approve")); mp.wait_for_load_state(); record("Approved and locked" in mp.inner_text("body"), "Assigned Manager approved and locked the weights"); mc.close()
        # bidding round 1 (no bids) -> retender
        op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); f(op, "OpenBidding").locator("[name=deadline]").fill(local_after(9)); send(f(op, "OpenBidding")); op.wait_for_load_state()
        wait_until(epoch(f"SELECT DATEDIFF_BIG(second,'1970-01-01',Deadline) FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=1") + 2)
        op.reload(); send(f(op, "CloseBidding")); op.wait_for_load_state()
        record(f(op, "Proceed").count() == 0 or "supplier offers are required" in op.inner_text("body"), "A round with no offers cannot proceed")
        op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); f(op, "Retender").locator("[name=reason]").fill("First round closed with no supplier bids"); send(f(op, "Retender")); op.wait_for_load_state()
        record(epoch(f"SELECT COUNT(*) FROM TenderRounds WHERE TenderId={tid}") == 2, "Re-tender created round 2 and kept round history")
        op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}")
        f(op, "OpenBidding").locator("[name=deadline]").fill(local_after(110)); send(f(op, "OpenBidding")); op.wait_for_load_state(); op.wait_for_timeout(800)
        if not sql(f"SELECT Deadline FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=2"):
            shot(op, "debug-round2"); print("ROUND2 PAGE:", op.inner_text("main")[:900], flush=True)
        end = epoch(f"SELECT DATEDIFF_BIG(second,'1970-01-01',Deadline) FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=2")
        for name in ["supplier1", "supplier2", "supplier3"]:
            f(op, "Invite").locator("[name=supplierId]").fill(str(a(name)["supplierId"])); send(f(op, "Invite")); op.wait_for_load_state()
        bids = []
        for i, name in enumerate(["supplier1", "supplier2", "supplier3"]):
            c, p = ctx(name); p.goto(BASE + f"/Supplier/Workflow/Details/{tid}"); f(p, "Respond").locator("button[value=true]").click(); p.wait_for_load_state()
            p.get_by_role("link", name="Prepare offer, attach documents and submit").click(); p.wait_for_load_state()
            fill(p, {"Input.DeliveryDays": "30", "Input.PaymentTerms": "Net 30", "Input.TechnicalProposal": "Compliant 48-port switches", "Input.Items[0].UnitPrice": str(100 + i * 10), "Input.Items[0].OfferedSpecifications": "48 port managed switch"})
            p.get_by_role("button", name="Save draft", exact=True).click(); p.wait_for_load_state()
            bid = int(sql(f"SELECT Id FROM Bids WHERE TenderInvitationId=(SELECT TOP 1 i.Id FROM TenderInvitations i JOIN TenderRounds r ON r.Id=i.TenderRoundId WHERE r.TenderId={tid} AND i.SupplierId={a(name)['supplierId']} ORDER BY r.RoundNumber DESC)")); bids.append(bid)
            p.goto(BASE + f"/Supplier/Offers/Details/{bid}"); p.locator("[name=file]").set_input_files({"name": "e2e-bid.pdf", "mimeType": "application/pdf", "buffer": b"%PDF-1.7\nSynthetic offer evidence\n%%EOF"})
            p.get_by_role("button", name="Upload attachment").click(); p.wait_for_load_state(); p.get_by_role("button", name="Submit final offer", exact=True).click(); p.wait_for_load_state()
            record("Submitted" in p.inner_text("body"), f"{name} submitted an offer with an attachment"); c.close()
        c, p = ctx("supplier1"); r = p.goto(BASE + f"/Supplier/Offers/Details/{bids[1]}"); record(r.status == 404, "Supplier 1 cannot open Supplier 2's offer (HTTP 404)"); c.close()
        wait_until(end + 2); op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); send(f(op, "CloseBidding")); op.wait_for_load_state(); send(f(op, "Proceed")); op.wait_for_load_state()
        for bid in bids:
            form = f(op, "Compliance").filter(has=op.locator(f'input[name=bidId][value="{bid}"]')); form.locator("[name=notes]").fill("Officer reviewed the stored evidence and requirements"); send(form); op.wait_for_load_state()
        record(sql(f"SELECT COUNT(*) FROM Bids WHERE Id IN ({','.join(map(str, bids))}) AND ComplianceStatus='Compliant'") == "3", "Compliance recorded for all three offers")
        send(f(op, "Analyze")); op.wait_for_load_state(timeout=240000)
        analysis = sql(f"SELECT TOP 1 Id FROM AiAnalysis WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC) ORDER BY Id DESC")
        record(sql(f"SELECT Status FROM AiAnalysis WHERE Id={int(analysis)}") == "Completed", "Live Ollama gemma3:4b bid analysis completed", sql(f"SELECT Model FROM AiAnalysis WHERE Id={int(analysis)}"))
        send(f(op, "ReviewAi")); op.wait_for_load_state()


    JITSI = os.environ.get('MEETING_MODE') == 'jitsi'
    # ---------------- 2. Zoom (fake Zoom API, real ZoomMeetingService) ----------------
    if JITSI:
        setup = f(op, "Setup")
        fill(setup, {"AiAnalysisId": analysis, "StartsAt": local_after(12), "EndsAt": local_after(240)}); send(setup); op.wait_for_load_state()
        row = sql(f"SELECT ISNULL(ZoomMeetingId,'none'),MeetingUrl FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)")
        record(f"meet.jit.si/OrangeTender{tid}-" in row and "none" in row, "Jitsi meeting created automatically (private unguessable room, no credentials)", row)
        record("4 persisted committee invitations" in op.inner_text("body"), "Four committee invitations persisted")
        shot(op, "Jitsi-meeting-scheduled")
        record(all(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{a(n)['id']}' AND Link='/Meetings/Join/{tid}'")) >= 1 for n in ["officer", "manager", "department-evaluator", "financial", "technical"]), "The Officer and all four evaluators received a notification with the join link")
        for who in ("officer", "manager", "financial"):
            c, p = ctx(who); p.goto(BASE + f"/Meetings/Join/{tid}"); p.wait_for_timeout(2500)
            record(p.locator("#meet").count() == 1 and p.locator("#meet").get_attribute("data-room").startswith(f"OrangeTender{tid}-"), f"{who} can join the meeting inside the system")
            if who == "officer":
                shot(p, "Jitsi-join-inside-system")
                record(p.locator("#meet iframe").count() == 1, "Jitsi video frame loaded in the page (needs internet access to meet.jit.si)")
            c.close()
        for who in ("department", "supplier1", "top1"):
            c, p = ctx(who); r = p.goto(BASE + f"/Meetings/Join/{tid}"); record(r.status == 404, f"{who} cannot open the meeting page (HTTP 404)"); c.close()
        wait_until(epoch(f"SELECT DATEDIFF_BIG(second,'1970-01-01',StartsAt) FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)") + 2)
    else:
        setup = f(op, "Setup"); zoom["token_ok"] = False
        fill(setup, {"AiAnalysisId": analysis, "StartsAt": local_after(12), "EndsAt": local_after(240)}); setup; send(setup); op.wait_for_load_state()
        body = op.inner_text("body")
        record("Zoom rejected the configured credentials" in body and sql(f"SELECT COUNT(*) FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)") == "0",
               "Zoom with rejected credentials: friendly error and nothing saved")
        shot(op, "Zoom-credentials-rejected")
        zoom["token_ok"] = True; op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); setup = f(op, "Setup")
        fill(setup, {"AiAnalysisId": analysis, "StartsAt": local_after(12), "EndsAt": local_after(240)}); setup; send(setup); op.wait_for_load_state()
        row = sql(f"SELECT ZoomMeetingId,MeetingUrl,State FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)")
        creates = [c for c in zoom["calls"] if c[0] == "create"]
        expected_auth = "Basic " + base64.b64encode(b"client-test:secret-test").decode()
        record(bool(creates) and "orange.zoom.us/j/" in row and "8100000000" in row, "Zoom meeting created through the Zoom API and its id + join link saved", row)
        record(any(c[0] == "token" and c[2] == expected_auth and "grant_type=account_credentials&account_id=acct-test" in c[1] for c in zoom["calls"]), "Server-to-Server OAuth request used the configured credentials")
        record(creates and creates[-1][1] == "host@example.test" and creates[-1][2]["type"] == 2 and creates[-1][2]["duration"] == 4, "Meeting created for the configured host as a scheduled meeting with the right duration", str(creates[-1][2]) if creates else "")
        record("4 persisted committee invitations" in op.inner_text("body"), "Four committee invitations persisted")
        shot(op, "Zoom-meeting-scheduled")
        record(all(int(sql(f"SELECT COUNT(*) FROM Notifications WHERE UserId='{a(n)['id']}' AND Link='/Meetings/Join/{tid}'")) >= 1 for n in ["officer", "manager", "department-evaluator", "financial", "technical"]), "The Officer and all four evaluators received a notification with the join link")
        c, p = ctx("technical"); resp = p.request.get(BASE + f"/Meetings/Join/{tid}", max_redirects=0); record(resp.status == 302 and "orange.zoom.us/j/" in resp.headers.get("location", ""), "Join link sends a participant to the Zoom meeting created by the system", resp.headers.get("location", "")); c.close()
        c, p = ctx("department"); record(p.goto(BASE + f"/Meetings/Join/{tid}").status == 404, "A non-participant cannot use the join link (HTTP 404)"); c.close()
        wait_until(epoch(f"SELECT DATEDIFF_BIG(second,'1970-01-01',StartsAt) FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)") + 2)

    # ---------------- 3. independent scoring and report ----------------
    for n, name in enumerate(["manager", "department-evaluator", "financial", "technical"]):
        c, p = ctx(name); p.goto(BASE + f"/EvaluationCommittee/Workflow/Details/{tid}"); form = f(p, "SubmitEvaluation")
        for i in range(3): fill(form, {f"scores[{i}].Financial": 90 - i * 10 - n, f"scores[{i}].Technical": 85 - i * 10 - n, f"scores[{i}].Justification": "Independent score against locked criteria and stored bid evidence"})
        send(form); p.wait_for_load_state(); record("Your final evaluation is recorded" in p.inner_text("body"), f"{name} submitted an independent evaluation"); c.close()
        if n == 0:
            op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); record(f(op, "EndEvaluation").count() == 0, "Incomplete scoring blocks End Evaluation")
    op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); form = f(op, "EndEvaluation"); form.locator("[name=reason]").fill("All four independent submissions complete"); form.locator("[name=confirmed]").check(); send(form); op.wait_for_load_state()
    record("Recommended Winner" in op.inner_text("body"), "End Evaluation produced the ranking and recommendation")
    rid = int(sql(f"SELECT TOP 1 Id FROM TenderReports WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC) ORDER BY Id DESC"))
    def report_page(p, area): p.goto(BASE + f"/{area}/Reports/Review/{rid}"); p.wait_for_timeout(1000)
    report_page(op, "SourcingOfficer"); record(op.locator("iframe").count() == 1 and int(sql(f"SELECT DATALENGTH(PdfContent) FROM TenderReports WHERE Id={rid}")) > 1000, "Generated PDF is displayed through the protected report page")

    # ---------------- 4. signatures ----------------
    for name in ["manager", "department-evaluator", "financial", "technical"]:
        c, p = ctx(name); report_page(p, "EvaluationCommittee"); form = f(p, "Sign"); form.locator("[name=comment]").fill("Reviewed current PDF and independently endorse committee findings"); form.locator("[name=confirmed]").check()
        action = form.get_attribute("action"); payload = form.evaluate("(x)=>Object.fromEntries(new FormData(x))"); send(form); p.wait_for_load_state()
        record(sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND UserId='{a(name)['id']}' AND Stage='Evaluation'") == "1", f"{name} signed the committee report")
        c.request.post(BASE + action, form=payload)
        record(sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND UserId='{a(name)['id']}' AND Stage='Evaluation'") == "1", f"{name}: replaying the signature request creates no second signature"); c.close()
    report_page(op, "SourcingOfficer"); send(f(op, "Forward")); op.wait_for_load_state()
    record(sql(f"SELECT Status FROM Tenders WHERE Id={tid}") == "ManagerReview", "Officer forwarded the fully signed report to the Manager", sql(f"SELECT Status FROM Tenders WHERE Id={tid}"))
    mc, mp = ctx("manager"); report_page(mp, "SourcingManager"); form = f(mp, "Decision"); form.locator("[name=comment]").fill("Separate Manager review approves the complete four-member signed report"); form.locator("[name=confirmed]").check(); send(form); mp.wait_for_load_state()
    record(sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND Stage='Manager'") == "1", "Manager approved and signed separately"); mc.close()

    # ---------------- 5. Top Committee: assignment, voting, signatures ----------------
    op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); form = f(op, "AssignTop")
    for i in range(4): form.locator("[name=memberIds]").nth(i).fill(a("top" + str(i + 1))["id"])
    send(form); op.wait_for_load_state(); record(sql(f"SELECT COUNT(*) FROM CommitteeMembers WHERE TenderId={tid} AND CommitteeType='Top'") == "4", "Four Top Committee members assigned")
    shot(op, "Top-committee-assigned")
    for i in range(1, 5):
        c, p = ctx("top" + str(i)); report_page(p, "TopCommittee"); form = f(p, "Vote")
        form.locator("[name=decision]").select_option("Reject" if i == 1 else "Approve"); form.locator("[name=comment]").fill("Independent final committee decision after reviewing report and evidence")
        if form.locator("[name=confirmed]").count(): form.locator("[name=confirmed]").check()
        if i == 2: shot(p, "Top-member-vote-and-sign")
        send(form); p.wait_for_load_state()
        record(sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND Stage='Top' AND UserId='{a('top' + str(i))['id']}'") == "1" and
               sql(f"SELECT COUNT(*) FROM CommitteeVotes v JOIN CommitteeMembers m ON m.Id=v.CommitteeMemberId WHERE m.TenderId={tid} AND m.UserId='{a('top' + str(i))['id']}'") == "1", f"Top member {i} voted {'Reject' if i == 1 else 'Approve'} and signed"); c.close()
        if i == 3:
            op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); send(f(op, "Award")); op.wait_for_load_state()
            record(sql(f"SELECT Status FROM Tenders WHERE Id={tid}") != "Awarded", "Two Top approvals cannot award the tender")
            record(sql(f"SELECT COUNT(*) FROM FinalDecisionNotices WHERE TenderId={tid}") == "0", "No final decision notice yet (3 of 4 approvals not reached)")

    # ---------------- 6. final decision relay ----------------
    record(sql(f"SELECT Status FROM FinalDecisionNotices WHERE TenderId={tid}") == "AwaitingManager" and sql(f"SELECT TopApprovals FROM FinalDecisionNotices WHERE TenderId={tid}") == "3", "Third approval created the final decision notice for the Manager (3 of 4)")
    op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); send(f(op, "Award")); op.wait_for_load_state()
    record(sql(f"SELECT Status FROM Tenders WHERE Id={tid}") != "Awarded", "Officer cannot award before the Manager acknowledges and forwards")
    results_before = int(sql("SELECT COUNT(*) FROM Notifications WHERE Title='Tender result'"))
    mc, mp = ctx("manager"); mp.goto(BASE + "/SourcingManager/FinalDecisions"); mp.locator("a.pg-btn:has-text('Acknowledge')").first.click(); mp.wait_for_selector("dialog.ui-dialog[open] form")
    shot(mp, "Manager-acknowledge-final-decision"); mp.fill("dialog textarea[name=comment]", "Top Committee decision reviewed; forwarding."); mp.click("dialog button[type=submit]"); mp.wait_for_load_state(); mp.wait_for_timeout(800); mc.close()
    record(sql(f"SELECT Status FROM FinalDecisionNotices WHERE TenderId={tid}") == "ForwardedToOfficer", "Manager acknowledged and forwarded the decision")
    op.goto(BASE + f"/SourcingOfficer/Workflow/Details/{tid}"); send(f(op, "Award")); op.wait_for_load_state()
    record(sql(f"SELECT Status FROM Tenders WHERE Id={tid}") == "Awarded", "Three of four Top approvals + Manager forwarding: Officer awarded and closed the tender", sql(f"SELECT Status,AwardedBidId FROM Tenders WHERE Id={tid}"))
    record(int(sql("SELECT COUNT(*) FROM Notifications WHERE Title='Tender result'")) == results_before, "Award itself sent NO supplier result notification")
    vp = oc.new_page(); v = vp.goto(BASE + f"/SourcingOfficer/Reports/Verify/{rid}").json()
    record(v["canonicalIntegrity"] and all(s["receiptIntegrity"] for s in v["signatures"]), f"Report hash and all {len(v['signatures'])} signature receipts verify")
    op.goto(BASE + "/SourcingOfficer/FinalDecisions"); op.locator("button:has-text('Notify Suppliers')").first.click(); op.wait_for_selector("dialog[open] [data-ok]"); shot(op, "Officer-notify-suppliers-confirm"); op.click("dialog[open] [data-ok]"); op.wait_for_load_state(); op.wait_for_timeout(1000)
    record(sql(f"SELECT COUNT(*) FROM SupplierResultDeliveries WHERE TenderId={tid} AND Outcome='Won'") == "1" and sql(f"SELECT COUNT(*) FROM SupplierResultDeliveries WHERE TenderId={tid} AND Outcome='Unsuccessful'") == "2", "Officer announced results: 1 winner, 2 unsuccessful suppliers")
    shot(op, "Officer-final-decisions-delivered")
    record(int(sql("SELECT COUNT(*) FROM Notifications WHERE Title='Tender result'")) == results_before + 3, "Three supplier result notifications exist (one per supplier)")
    winner = sql(f"SELECT TOP 1 u.Email FROM SupplierResultDeliveries d JOIN AspNetUsers u ON u.Id=d.RecipientUserId WHERE d.TenderId={tid} AND d.Outcome='Won'")
    c, p = ctx(next(n for n in ("supplier1", "supplier2", "supplier3") if a(n)["email"] == winner)); p.goto(BASE + "/Supplier"); p.wait_for_timeout(800); p.click(".ws-bell-button"); p.wait_for_selector(".ws-bell-item"); p.wait_for_timeout(500)
    record("selected for this tender" in p.inner_text(".ws-bell-panel"), "Winning supplier sees the winning message in the bell"); shot(p, "Winning-supplier-notification"); c.close()
    record(sql(f"SELECT COUNT(*) FROM AuditLogs WHERE EntityId='{tid}' AND Action IN ('TopCommitteeDecision','TenderAwardedAndClosed','SupplierResultsAnnounced','FinalDecisionAcknowledgedByManager')") != "" and int(sql(f"SELECT COUNT(*) FROM AuditLogs WHERE EntityId='{tid}' AND Action IN ('TopCommitteeDecision','TenderAwardedAndClosed','SupplierResultsAnnounced','FinalDecisionAcknowledgedByManager')")) >= 7, "Audit trail holds the votes, acknowledgement, award and announcement")

proc = start_server()
try:
    with sync_playwright() as pw:
        b = pw.chromium.launch(headless=True)
        try: run(b)
        except Exception:
            record(False, "Lifecycle aborted by an exception"); print(traceback.format_exc(), flush=True)
        finally: b.close()
finally:
    proc.terminate()
    try: proc.wait(10)
    except Exception: proc.kill()
bad = [n for ok, n in results if not ok]
print(f"\nRESULT: {len(results) - len(bad)}/{len(results)} passed")
for n in bad: print("FAILED:", n)
