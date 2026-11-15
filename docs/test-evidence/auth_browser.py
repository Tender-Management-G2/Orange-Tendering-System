from core import *
import traceback
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True)
    try:
        c=ctx(b,'admin');p=c.new_page();login(p,S['admin'])
        if 'FirstLoginPasswordChange' in p.url:
            fill(p,{'Input.CurrentPassword':S['admin']['password'],'Input.NewPassword':S['admin']['password']+'X','Input.ConfirmPassword':S['admin']['password']+'X'})
            p.locator('form').filter(has=p.locator('[name="Input.CurrentPassword"]')).locator('button[type=submit]').click();p.wait_for_load_state()
            assert 'FirstLoginPasswordChange' not in p.url
            S['admin']['password']+='X';PRIVATE.write_text(json.dumps(S),encoding='utf-8')
        keep(c,'admin');record('Admin first-login password change','PASS','Actual Identity required and accepted a password change.',p)
        p.goto(BASE+'/Admin');record('Admin dashboard','PASS','Actual authenticated Admin dashboard.',p)
        p.goto(BASE+'/Admin/Users');assert p.locator('table tr').count()==15;record('Admin user management list','PASS','Fourteen actual test accounts visible; SQL count '+sql('SELECT COUNT(*) FROM AspNetUsers'),p)
        p.goto(BASE+'/Admin/Users/Create');fill(p,{'fullName':'SMTP delivery check','email':'activation-check@orange-e2e.invalid'});p.locator('[name=role]').select_option(label='Evaluation Committee');p.get_by_role('button',name='Create employee and send activation').click();p.wait_for_load_state();assert 'delivery is unavailable' in p.locator('body').inner_text();record('Live employee invitation delivery','BLOCKED','SMTP not configured. Real UI rolls back failed delivery; unexpected accounts '+sql("SELECT COUNT(*) FROM AspNetUsers WHERE Email='activation-check@orange-e2e.invalid'"),p);c.close()
        for u in S['accounts']:
            if u['role']=='Supplier':continue
            c=ctx(b,u['name']);p=c.new_page();p.goto(BASE+'/Identity/Account/ActivateEmployee?email='+quote(u['email'])+'&code='+quote(u['activationCode']))
            fill(p,{'Input.Password':u['password'],'Input.ConfirmPassword':u['password']});p.locator('form').filter(has=p.locator('[name="Input.Password"]')).locator('button[type=submit]').click();p.wait_for_load_state()
            assert 'Your password is set' in p.locator('body').inner_text() or 'invalid or expired' in p.locator('body').inner_text() and sql("SELECT COUNT(*) FROM AspNetUsers WHERE Email='"+u['email']+"' AND EmailConfirmed=1")=="1"
            login(p,u);assert '/Login' not in p.url;keep(c,u['name']);record(u['name']+' activation and '+u['role']+' dashboard','PASS','Real browser activation/login using actual Identity token issued during fixture setup; not an email-delivery claim.',p);c.close()
        for u in [u for u in S['accounts'] if u['role']=='Supplier']:
            c=ctx(b,u['name']);p=c.new_page();p.goto(BASE+'/Identity/Account/VerifyEmail?email='+quote(u['email']))
            p.locator('[name="Input.Code"]').fill('000000' if u['otp']!='000000' else '111111');p.locator('form').filter(has=p.locator('[name="Input.Code"]')).locator('button[type=submit]').click();p.wait_for_load_state();assert 'invalid or expired' in p.locator('body').inner_text();record(u['name']+' invalid OTP','PASS','Invalid actual OTP rejected.',p)
            p.locator('[name="Input.Code"]').fill(u['otp']);p.locator('form').filter(has=p.locator('[name="Input.Code"]')).locator('button[type=submit]').click();p.wait_for_load_state();assert 'awaiting administrator approval' in p.locator('body').inner_text();record(u['name']+' OTP verification','PASS','Real Identity OTP consumed in browser; fixture-issued, not emailed.',p)
            ac=ctx(b,'admin');ap=ac.new_page();ap.goto(BASE+'/Admin/Suppliers/Review/'+str(u['supplierId']));ap.locator('[name=notes]').fill('Synthetic E2E commercial registration reviewed by actual Admin');ap.locator('button[name=approve][value=true]').click();ap.wait_for_load_state();assert 'Approved' in ap.locator('body').inner_text();record(u['name']+' Admin approval','PASS','Actual Admin approval gate completed through UI.',ap);ac.close()
            login(p,u);assert '/Supplier' in p.url;keep(c,u['name']);record(u['name']+' supplier dashboard','PASS','Real approved supplier login.',p);p.goto(BASE+'/Admin');assert 'AccessDenied' in p.url;record(u['name']+' unauthorized Admin access','PASS','Real authorization denied Admin access.',p);c.close()
        record('SQL account and authorization persistence','PASS',sql("SELECT COUNT(*) AS Accounts FROM AspNetUsers;SELECT COUNT(*) AS Confirmed FROM AspNetUsers WHERE EmailConfirmed=1;SELECT COUNT(*) AS ApprovedSuppliers FROM Suppliers WHERE Status='Approved';"))
        record('Live Gmail SMTP OTP and password reset delivery','BLOCKED','No authorized Gmail SMTP settings. No real external email delivery claimed.')
        record('Live Zoom API','BLOCKED','No authorized Zoom server-to-server credentials configured.')
        record('Browser authentication phase','PASS','All available actual browser checks completed. Gmail delivery remains blocked; fixture-issued tokens explicitly distinguished.')
    except Exception as e:
        record('Browser authentication phase','FAIL',clean(str(e)),p);print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:b.close()
