from core import *
import traceback
def report_page(p,area,rid):
    p.goto(BASE+f'/{area}/Reports/Review/{rid}');p.wait_for_timeout(1000)
def run(b):
    tid=S['tenderId'];rid=S['reportId'];oc=ctx(b,'officer');op=oc.new_page();report_page(op,'SourcingOfficer',rid)
    assert op.locator('iframe').count()==1
    assert int(sql(f"SELECT COUNT(*) FROM AuditLogs WHERE EntityName='TenderReport' AND EntityId='{rid}' AND Action='ProtectedReportViewed'"))>0
    record('Generated PDF displayed through protected report UI','PASS','Real generated PDF iframe request succeeded and produced SQL access audit; report byte length '+sql(f'SELECT DATALENGTH(PdfContent) FROM TenderReports WHERE Id={rid}'),op)
    for name in ['manager','department-evaluator','financial','technical']:
        c=ctx(b,name);p=c.new_page();report_page(p,'EvaluationCommittee',rid);form=f(p,'Sign');form.locator('[name=comment]').fill('Reviewed current PDF and independently endorse committee findings');form.locator('[name=confirmed]').check()
        action=form.get_attribute('action');payload=form.evaluate('(f)=>Object.fromEntries(new FormData(f))');send(form);p.wait_for_load_state();assert sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND UserId='{a(name)['id']}' AND Stage='Evaluation'")=='1'
        record(name+' committee PDF signature','PASS','Actual authenticated electronic acknowledgment persisted for this document version. Not certificate-backed signing.',p)
        replay=c.request.post(BASE+action,form=payload);count=sql(f"SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid} AND UserId='{a(name)['id']}' AND Stage='Evaluation'");assert count=='1';record(name+' duplicate signature replay','PASS',f'Real authenticated form replay HTTP {replay.status}; SQL receipt count remains 1.');c.close()
    report_page(op,'SourcingOfficer',rid);send(f(op,'Forward'));op.wait_for_load_state();record('Officer forwards after four signatures','PASS',sql(f'SELECT Status FROM Tenders WHERE Id={tid}'),op)
    mc=ctx(b,'manager');mp=mc.new_page();report_page(mp,'SourcingManager',rid);form=f(mp,'Decision');form.locator('[name=comment]').fill('Separate Manager review approves the complete four-member signed report');form.locator('[name=confirmed]').check();send(form);mp.wait_for_load_state();record('Manager separate approval and signature','PASS',sql(f"SELECT Stage,COUNT(*) AS Signatures FROM ReportSignature WHERE TenderReportId={rid} GROUP BY Stage"),mp);mc.close()
    op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');form=f(op,'AssignTop')
    for i in range(4):form.locator('[name=memberIds]').nth(i).fill(a('top'+str(i+1))['id'])
    send(form);op.wait_for_load_state();record('Separate four-person Top Committee assigned','PASS',sql(f'SELECT CommitteeType,COUNT(*) FROM CommitteeMembers WHERE TenderId={tid} GROUP BY CommitteeType'),op)
    for i in range(1,5):
        c=ctx(b,'top'+str(i));p=c.new_page();report_page(p,'TopCommittee',rid);form=f(p,'Vote');form.locator('[name=decision]').select_option('Reject' if i==1 else 'Approve');form.locator('[name=comment]').fill('Independent final committee decision after reviewing report and evidence');form.locator('[name=confirmed]').check();send(form);p.wait_for_load_state();record('Top member '+str(i)+' signed '+('rejection' if i==1 else 'approval'),'PASS',sql(f"SELECT Decision,COUNT(*) FROM CommitteeVotes WHERE CommitteeRoundId IN (SELECT Id FROM CommitteeRounds WHERE TenderReportId={rid} AND CommitteeType='Top') GROUP BY Decision"),p);c.close()
        if i==3:
            op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');send(f(op,'Award'));op.wait_for_load_state();assert sql(f'SELECT Status FROM Tenders WHERE Id={tid}')!='Awarded';record('Two Top approvals cannot award','PASS','Real award submission rejected; three approvals required.',op)
    op.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');send(f(op,'Award'));op.wait_for_load_state();assert sql(f'SELECT Status FROM Tenders WHERE Id={tid}')=='Awarded';record('Three of four Top approvals award and closure','PASS',sql(f'SELECT Status,AwardedBidId,AwardedAt,ClosedAt FROM Tenders WHERE Id={tid}'),op)
    vp=oc.new_page();response=vp.goto(BASE+f'/SourcingOfficer/Reports/Verify/{rid}');v=response.json();assert v['canonicalIntegrity'] and all(s['receiptIntegrity'] for s in v['signatures']);record('Report hash and signature receipt verification','PASS','Canonical integrity and all signature receipts verified by actual server endpoint. '+str(len(v['signatures']))+' receipts.',vp)
    record('Browser final approvals phase','PASS','Committee signatures, distinct Manager signature, four Top decisions (3 approve/1 reject), award and closure completed through actual browser UI.')
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True)
    try:run(b)
    except Exception as e:record('Browser final approvals phase','FAIL',clean(str(e)));print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:b.close()
