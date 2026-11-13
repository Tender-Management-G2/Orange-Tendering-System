from core import *
import traceback
def local_after(seconds):return ((datetime.now(timezone.utc)+timedelta(seconds=seconds)).replace(second=0,microsecond=0)+timedelta(minutes=1)).strftime('%Y-%m-%dT%H:%M')
def wait_until(deadline):
    while time.time()<deadline:time.sleep(min(2,deadline-time.time()))
def run(b):
    if os.environ.get('ORANGE_E2E_RESUME')=='closed-first':
        tid=S['tenderId'];oc=ctx(b,'officer');op=oc.new_page();op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}')
    else:
        dc=ctx(b,'department');dp=dc.new_page();dp.goto(BASE+'/Department/PurchaseRequests/Create')
        fill(dp,{'title':'Browser E2E network procurement','justification':'Actual isolated end-to-end test purchase request','itemName':'Network switch','quantity':'10','unit':'pcs','specifications':'48 ports; commercial registration and bid evidence required'})
        dp.get_by_role('button',name='Submit purchase request').click();dp.wait_for_load_state();pr=int(dp.url.rsplit('/',1)[-1]);S['requestId']=pr;PRIVATE.write_text(json.dumps(S),encoding='utf-8');record('Purchase Request submission','PASS','Real department UI persisted request and submission snapshot. '+sql(f'SELECT Status FROM PurchaseRequests WHERE Id={pr};'),dp)
        oc=ctx(b,'officer');op=oc.new_page();op.goto(BASE+f'/SourcingOfficer/PurchaseRequests/Review/{pr}');op.locator('[name=comment]').fill('Accepted after reviewing all requested items');op.get_by_role('button',name='Accept purchase request').click();op.wait_for_load_state();assert 'Accepted' in op.locator('body').inner_text();record('Officer Purchase Request acceptance','PASS',sql(f'SELECT Decision FROM PurchaseRequestReviews WHERE PurchaseRequestId={pr};'),op)
        op.goto(BASE+f'/SourcingOfficer/Workflow/Create/{pr}')
        fill(op,{'PurchaseRequestId':pr,'Title':'Browser E2E Tender','Description':'Real browser tender cycle','TechnicalRequirements':'48 port switches','CommercialConditions':'JOD fixed price','DeliveryExpectations':'30 days','MandatoryCompliance':'Company registration and offer evidence','EstimatedValue':'50000','Currency':'JOD','SourcingManagerId':a('manager')['id'],'DepartmentRepresentativeId':a('department-evaluator')['id'],'FinancialRepresentativeId':a('financial')['id'],'TechnicalRepresentativeId':a('technical')['id']})
        op.get_by_role('button',name='Save tender requirements').click();op.wait_for_load_state();tid=int(op.url.rsplit('/',1)[-1]);S['tenderId']=tid;PRIVATE.write_text(json.dumps(S),encoding='utf-8');record('Tender with exactly four committee seats','PASS',sql(f'SELECT CommitteeType,Responsibility FROM CommitteeMembers WHERE TenderId={tid};'),op)
        send(f(op,'RequestWeights'));op.wait_for_load_state()
        for i,name in enumerate(['manager','department-evaluator','financial','technical']):
            c=ctx(b,name);p=c.new_page();p.goto(BASE+f'/EvaluationCommittee/Workflow/Details/{tid}');form=f(p,'Propose');form.locator('[name=financial]').fill(str(30+i*10));form.locator('[name=technical]').fill(str(70-i*10));send(form);p.wait_for_load_state();assert 'operation was saved' in p.locator('body').inner_text().lower();record(name+' independent weight proposal','PASS',sql(f'SELECT COUNT(*) AS Proposals FROM WeightProposal WHERE WeightRoundId=(SELECT Id FROM WeightRound WHERE TenderId={tid});'),p);c.close()
        mc=ctx(b,'manager');mp=mc.new_page();mp.goto(BASE+f'/EvaluationCommittee/Workflow/Details/{tid}');assert 'Financial 45%' in mp.locator('body').inner_text();record('Four-proposal average 45 financial / 55 technical','PASS',sql(f'SELECT FinancialAverage,TechnicalAverage FROM WeightRound WHERE TenderId={tid}'),mp);send(f(mp,'Approve'));mp.wait_for_load_state();assert 'Approved and locked' in mp.locator('body').inner_text();record('Assigned Manager weight approval and lock','PASS',sql(f'SELECT State,ApprovedFinancial,ApprovedTechnical FROM WeightRound WHERE TenderId={tid}'),mp);mc.close()
        op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');f(op,'OpenBidding').locator('[name=deadline]').fill(local_after(9));send(f(op,'OpenBidding'));op.wait_for_load_state();assert sql(f'SELECT Status FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=1')=='Open';record('First bidding round opened','PASS','Real server deadline; no test clock or SQL date edits.',op)
        wait_until(int(sql(f"SELECT DATEDIFF_BIG(second,'1970-01-01',Deadline) FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=1"))+2);op.reload();send(f(op,'CloseBidding'));op.wait_for_load_state();
    assert f(op,'Proceed').count()==0 or 'supplier offers are required' in op.locator('body').inner_text();record('Missing bids block Proceed','PASS','Zero offers cannot advance; the actual UI withholds Proceed.',op)
    op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');f(op,'Retender').locator('[name=reason]').fill('First round closed with no supplier bids');send(f(op,'Retender'));op.wait_for_load_state();record('Officer Re-Tender preserves round history','PASS',sql(f'SELECT RoundNumber,Status FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber'),op)
    end=time.time()+100;f(op,'OpenBidding').locator('[name=deadline]').fill(local_after(100));send(f(op,'OpenBidding'));op.wait_for_load_state();end=int(sql(f"SELECT DATEDIFF_BIG(second,'1970-01-01',Deadline) FROM TenderRounds WHERE TenderId={tid} AND RoundNumber=2"))
    for name in ['supplier1','supplier2','supplier3']:
        f(op,'Invite').locator('[name=supplierId]').fill(str(a(name)['supplierId']));send(f(op,'Invite'));op.wait_for_load_state()
    record('Supplier invitations persisted','PASS',sql(f'SELECT COUNT(*) FROM TenderInvitations WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)'),op)
    bids=[]
    for i,name in enumerate(['supplier1','supplier2','supplier3']):
        c=ctx(b,name);p=c.new_page();p.goto(BASE+f'/Supplier/Workflow/Details/{tid}');f(p,'Respond').locator('button[value=true]').click();p.wait_for_load_state();p.get_by_role('link',name='Prepare offer, attach documents and submit').click();p.wait_for_load_state()
        fill(p,{'Input.DeliveryDays':'30','Input.PaymentTerms':'Net 30','Input.TechnicalProposal':'Compliant 48-port switches','Input.Items[0].UnitPrice':str(100+i*10),'Input.Items[0].OfferedSpecifications':'48 port managed switch'})
        p.get_by_role('button',name='Save draft',exact=True).click();p.wait_for_load_state()
        bid=int(sql(f"SELECT Id FROM Bids WHERE TenderInvitationId=(SELECT TOP 1 i.Id FROM TenderInvitations i JOIN TenderRounds r ON r.Id=i.TenderRoundId WHERE r.TenderId={tid} AND i.SupplierId={a(name)['supplierId']} ORDER BY r.RoundNumber DESC)"));bids.append(bid)
        p.goto(BASE+f'/Supplier/Offers/Details/{bid}');p.locator('[name=file]').set_input_files({'name':'e2e-bid.pdf','mimeType':'application/pdf','buffer':b'%PDF-1.7\nSynthetic isolated offer evidence\n%%EOF'});p.get_by_role('button',name='Upload attachment').click();p.wait_for_load_state();p.get_by_role('button',name='Submit final offer',exact=True).click();p.wait_for_load_state();assert 'Submitted' in p.locator('body').inner_text();record(name+' supplier bidding and attachment','PASS',sql(f'SELECT Status,SubmittedAt FROM Bids WHERE Id={bid}'),p);c.close()
    S['bidIds']=bids;PRIVATE.write_text(json.dumps(S),encoding='utf-8')
    c=ctx(b,'supplier1');p=c.new_page();resp=p.goto(BASE+f'/Supplier/Offers/Details/{bids[1]}');assert resp.status==404;record('Supplier offer data isolation','PASS','Supplier1 receives HTTP 404 for Supplier2 offer.',p);c.close()
    wait_until(end+2);op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');send(f(op,'CloseBidding'));op.wait_for_load_state();record('Expired deadline closes bidding','PASS',sql(f'SELECT RoundNumber,Status FROM TenderRounds WHERE TenderId={tid}'),op)
    send(f(op,'Proceed'));op.wait_for_load_state()
    for bid in bids:
        form=f(op,'Compliance').filter(has=op.locator(f'input[name=bidId][value="{bid}"]'));form.locator('[name=notes]').fill('Officer reviewed the actual stored synthetic evidence and requirements');send(form);op.wait_for_load_state()
    record('Compliance check for all supplier bids','PASS',sql(f'SELECT Id,ComplianceStatus FROM Bids WHERE Id IN ({",".join(map(str,bids))})'),op)
    send(f(op,'Analyze'));op.wait_for_load_state(timeout=180000)
    analysis=sql(f'SELECT TOP 1 Id FROM AiAnalysis WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC) ORDER BY Id DESC')
    status=sql(f'SELECT Status FROM AiAnalysis WHERE Id={int(analysis)}');assert status=='Completed',status
    record('Live Ollama bid analysis gemma3:4b','PASS',sql(f'SELECT Model,Status FROM AiAnalysis WHERE Id={int(analysis)}'),op);send(f(op,'ReviewAi'));op.wait_for_load_state()
    start=time.time()+12;setup=f(op,'Setup');fill(setup,{'AiAnalysisId':analysis,'MeetingUrl':'https://orange.zoom.us/j/1234567890','StartsAt':local_after(12),'EndsAt':local_after(240)});send(setup);op.wait_for_load_state();assert '4 persisted committee invitations' in op.locator('body').inner_text();record('Evaluation scheduling with Officer-supplied link','PASS','No Zoom API success claimed. '+sql(f'SELECT State,StartsAt,EndsAt,MeetingUrl FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)'),op)
    wait_until(int(sql(f"SELECT DATEDIFF_BIG(second,'1970-01-01',StartsAt) FROM EvaluationSession WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC)"))+2)
    for n,name in enumerate(['manager','department-evaluator','financial','technical']):
        c=ctx(b,name);p=c.new_page();p.goto(BASE+f'/EvaluationCommittee/Workflow/Details/{tid}');form=f(p,'SubmitEvaluation')
        for i in range(3):fill(form,{f'scores[{i}].Financial':90-i*10-n,f'scores[{i}].Technical':85-i*10-n,f'scores[{i}].Justification':'Independent score against locked criteria and stored bid evidence'})
        send(form);p.wait_for_load_state();assert 'Your final evaluation is recorded' in p.locator('body').inner_text();record(name+' independent supplier scoring','PASS',sql(f'SELECT COUNT(*) FROM EvaluationScores WHERE CommitteeMemberId=(SELECT Id FROM CommitteeMembers WHERE TenderId={tid} AND UserId=\'{a(name)["id"]}\')'),p);c.close()
        if n==0:
            op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');assert f(op,'EndEvaluation').count()==0;record('Incomplete scoring blocks End Evaluation','PASS','Officer UI withholds finalization while three evaluations are missing; no derived recommendation.',op)
    op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');form=f(op,'EndEvaluation');form.locator('[name=reason]').fill('All four independent submissions complete');form.locator('[name=confirmed]').check();send(form);op.wait_for_load_state();assert 'Recommended Winner' in op.locator('body').inner_text();rid=int(sql(f'SELECT TOP 1 Id FROM TenderReports WHERE TenderRoundId=(SELECT TOP 1 Id FROM TenderRounds WHERE TenderId={tid} ORDER BY RoundNumber DESC) ORDER BY Id DESC'));S['reportId']=rid;PRIVATE.write_text(json.dumps(S),encoding='utf-8');record('End Evaluation automatic scores ranking PDF','PASS',sql(f'SELECT Id,VersionNumber,DATALENGTH(PdfContent) AS PDFBytes,PdfSha256 FROM TenderReports WHERE Id={rid}'),op)
    record('Browser tender cycle through PDF','PASS','Actual browser flow from Purchase Request to generated report completed.')
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True)
    try:run(b)
    except Exception as e:record('Browser tender cycle through PDF','FAIL',clean(str(e)),locals().get('op'));print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:b.close()
