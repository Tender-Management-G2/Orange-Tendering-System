from core import *
import traceback
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True)
    try:
        c=ctx(b,'officer');p=c.new_page();tid=1;payload=None;action=None
        for r in range(1,5):
            p.goto(BASE+f'/SourcingOfficer/Workflow/Details/{tid}')
            status=sql(f'SELECT Status FROM TenderRounds WHERE TenderId={tid} AND RoundNumber={r}')
            if status=='Draft':
                deadline=(datetime.now(timezone.utc)+timedelta(seconds=3)).replace(second=0,microsecond=0)+timedelta(minutes=1)
                f(p,'OpenBidding').locator('[name=deadline]').fill(deadline.strftime('%Y-%m-%dT%H:%M'));send(f(p,'OpenBidding'));p.wait_for_load_state()
            end=int(sql(f"SELECT DATEDIFF_BIG(second,'1970-01-01',Deadline) FROM TenderRounds WHERE TenderId={tid} AND RoundNumber={r}"))
            while time.time()<end+1:time.sleep(min(2,end+1-time.time()))
            p.reload();send(f(p,'CloseBidding'));p.wait_for_load_state()
            assert sql(f'SELECT Status FROM TenderRounds WHERE TenderId={tid} AND RoundNumber={r}')=='Closed'
            if r<4:
                form=f(p,'Retender');form.locator('[name=reason]').fill('Isolated real-deadline round-limit negative test');form.locator('input[type=checkbox]').check();payload=form.evaluate('(f)=>Object.fromEntries(new FormData(f))');action=form.get_attribute('action');send(form);p.wait_for_load_state();record('Round-limit test completes round '+str(r),'PASS',sql(f'SELECT RoundNumber,Status FROM TenderRounds WHERE TenderId={tid}'),p)
            else:
                assert f(p,'Retender').count()==0
                response=c.request.post(BASE+action,form=payload);assert response.status==400;assert sql(f'SELECT COUNT(*) FROM TenderRounds WHERE TenderId={tid}')=='4'
                record('Re-Tender round 5 blocked','PASS','Four real-deadline rounds executed in UI; fifth-round replay rejected HTTP 400 and SQL remains exactly four rounds.',p)
    except Exception as e:record('Round-limit browser phase','FAIL',clean(str(e)));print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:b.close()
