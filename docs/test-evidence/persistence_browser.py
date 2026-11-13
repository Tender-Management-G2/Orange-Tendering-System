from core import *
import traceback
def fixture(action):
    env=os.environ.copy();env['ORANGE_E2E_GUARD_ACTION']=action
    app=OUT.parents[1]/'Orange-Tendering-System/Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll'
    with open(PRIVATE.parent/'fixture-guard.log','ab') as log:
        p=subprocess.run(['dotnet',str(app),'--provision-browser'],env=env,stdout=log,stderr=subprocess.STDOUT)
    if p.returncode:raise RuntimeError('Isolated Identity fixture operation failed; see private execution log.')
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True);duplicate=False
    try:
        tid=S['tenderId'];rid=S['reportId'];c=ctx(b,'officer');p=c.new_page();p.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');assert 'Officially awarded bid' in p.locator('body').inner_text();record('Award and closure survive actual application restart','PASS',sql(f'SELECT Status,AwardedBidId,ClosedAt FROM Tenders WHERE Id={tid}'),p)
        p.goto(BASE+f'/SourcingOfficer/Reports/Review/{rid}');assert 'Current endorsement' in p.locator('body').inner_text();assert sql(f'SELECT COUNT(*) FROM ReportSignature WHERE TenderReportId={rid}')=='9';record('PDF signatures and version survive restart','PASS','All nine real signature receipts and current document remain available after stopping/restarting actual Kestrel.',p)
        response=p.goto(BASE+f'/SourcingOfficer/Reports/Verify/{rid}');v=response.json();assert v['canonicalIntegrity'] and all(s['receiptIntegrity'] for s in v['signatures']);record('Post-restart canonical hash verification','PASS','Actual verification endpoint still validates PDF and every receipt.',p)
        fixture('duplicate-on');duplicate=True;response=p.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');assert response.status==400;record('Duplicate active Officer fails closed','PASS','A second Officer role was assigned through real Identity in isolated fixture setup. Original Officer cannot access management while configuration is ambiguous.',p)
        fixture('duplicate-off');duplicate=False;p.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}');assert 'Officially awarded bid' in p.locator('body').inner_text();record('Single Officer management restored','PASS','Removing duplicate through Identity restores original Officer access.',p);c.close()
        fixture('reset-token');updated=json.loads(PRIVATE.read_text());u=next(x for x in updated['accounts'] if x['name']=='supplier1');c=ctx(b,'anonymous');p=c.new_page();p.goto(BASE+'/Identity/Account/ResetPassword?code='+quote(u['resetCode']));new=u['password']+'R';fill(p,{'Input.Email':u['email'],'Input.Password':new,'Input.ConfirmPassword':new});p.locator('form button[type=submit]').click();p.wait_for_load_state();assert 'ResetPasswordConfirmation' in p.url
        u['password']=new;PRIVATE.write_text(json.dumps(updated),encoding='utf-8');S['accounts']=updated['accounts'];record('Password reset token consumption in browser','PASS','Actual Identity reset accepted a genuine fixture-issued token. This does not establish Forgot Password email delivery.',p);login(p,u);assert '/Supplier' in p.url;record('Login with newly reset password','PASS','Real Identity accepts updated supplier password.',p);c.close()
    except Exception as e:record('Restart and negative browser phase','FAIL',clean(str(e)));print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:
        if duplicate:fixture('duplicate-off')
        b.close()
