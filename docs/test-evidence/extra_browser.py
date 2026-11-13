from core import *
import traceback
with sync_playwright() as pw:
    b=pw.chromium.launch(headless=True)
    try:
        c=ctx(b,'anonymous');p=c.new_page();p.goto(BASE+'/Identity/Account/Register')
        fill(p,{'Input.FullName':'Real browser registration attempt','Input.Email':'registration-ui@orange-e2e.invalid','Input.Password':a('supplier1')['password'],'Input.ConfirmPassword':a('supplier1')['password'],'Input.CompanyName':'Isolated registration attempt','Input.RegistrationNumber':'BROWSER-REGISTRATION-ATTEMPT','Input.CompanyEmail':'registration-ui@orange-e2e.invalid','Input.CompanyPhone':'+962790000000','Input.CompanyAddress':'Synthetic isolated E2E address'})
        p.locator('input[name=CommercialRegistrationCertificate]').set_input_files({'name':'e2e-registration.pdf','mimeType':'application/pdf','buffer':b'%PDF-1.7\nIsolated registration upload test\n%%EOF'})
        p.locator('input[name=LegalDocuments]').set_input_files({'name':'e2e-license.pdf','mimeType':'application/pdf','buffer':b'%PDF-1.7\nSynthetic isolated license\n%%EOF'})
        p.locator('form').filter(has=p.locator('[name="Input.CompanyName"]')).locator('button[type=submit]').click();p.wait_for_load_state()
        assert 'Registration could not be completed' in p.locator('body').inner_text()
        assert sql("SELECT COUNT(*) FROM AspNetUsers WHERE Email='registration-ui@orange-e2e.invalid'")=='0'
        record('Supplier Registration through live UI','BLOCKED','Real registration fails and rolls back without configured SMTP. No emailed OTP or registration success is claimed.',p)
        p.goto(BASE+'/Identity/Account/ForgotPassword');p.locator('[name="Input.Email"]').fill(a('supplier1')['email']);p.locator('form button[type=submit]').click();p.wait_for_load_state();assert 'Email delivery is unavailable' in p.locator('body').inner_text();record('Forgot Password through live UI','BLOCKED','Actual browser request confirms email recovery is unavailable; no reset email fabricated.',p)
        p.goto(BASE+'/Identity/Account/ResetPassword?code=aW52YWxpZA');fill(p,{'Input.Email':a('supplier1')['email'],'Input.Password':a('supplier1')['password'],'Input.ConfirmPassword':a('supplier1')['password']});p.locator('form button[type=submit]').click();p.wait_for_load_state();assert 'Invalid token' in p.locator('body').inner_text();record('Invalid password reset token rejected','PASS','Real Identity reset rejects forged token.',p);c.close()
        ac=ctx(b,'admin');ap=ac.new_page();ap.goto(BASE+'/Admin/Users/Details/'+a('department')['id']);
        if ap.get_by_role('button',name='Deactivate account',exact=True).count():ap.get_by_role('button',name='Deactivate account',exact=True).click();ap.wait_for_load_state()
        assert sql("SELECT CAST(IsActive AS int) FROM AspNetUsers WHERE Id='"+a('department')['id']+"'")=='0'
        dc=ctx(b,'department');dp=dc.new_page();dp.goto(BASE+'/Department');assert '/Login' in dp.url;record('Admin deactivation revokes existing session','PASS','Actual Admin management deactivates account and existing browser cookie is rejected.',dp);dc.close()
        ap.get_by_role('button',name='Activate account',exact=True).click();ap.wait_for_load_state();assert sql("SELECT CAST(IsActive AS int) FROM AspNetUsers WHERE Id='"+a('department')['id']+"'")=='1';record('Admin account reactivation','PASS','Actual protected Admin UI restores account activity.',ap)
        dc=ctx(b,'department');dp=dc.new_page();login(dp,a('department'));keep(dc,'department');dc.close();ac.close()
        record('Additional live UI checks','PASS','Registration/recovery SMTP blockers verified; invalid reset token rejected and Admin account state changes exercised.')
    except Exception as e:record('Additional live UI checks','FAIL',clean(str(e)),p);print(clean(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:b.close()
