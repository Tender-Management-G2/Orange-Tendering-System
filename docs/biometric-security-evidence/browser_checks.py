import os,json,re,time,subprocess,html,traceback
from pathlib import Path
from urllib.parse import quote
from playwright.sync_api import sync_playwright

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[1]
PRIVATE=Path(os.environ['ORANGE_E2E_PRIVATE_STATE'])
S=json.loads(PRIVATE.read_text());BASE='https://localhost:7553';DB='OrangeAuthCheck_BrowserE2E_BioSecurity20261113'
ADMIN=S['admin'];USER=S['accounts'][0]
RESULTS=[]
(OUT/'screenshots').mkdir(exist_ok=True)
def scrub(text):
    text=str(text)
    for a in [ADMIN,USER]:
        for key in ['password','otp','activationCode']:
            if a.get(key):text=text.replace(a[key],'[REDACTED]')
    return re.sub(r'([?&](?:code|token)=)[^&\s]+',r'\1[REDACTED]',text,flags=re.I)
def result(name,status,detail,p=None):
    r={'name':name,'status':status,'detail':scrub(detail)}
    if p and not p.is_closed():
        file=f'{len(RESULTS)+1:03}-'+re.sub('[^a-z0-9]+','-',name.lower())[:75]+'.png'
        p.screenshot(path=str(OUT/'screenshots'/file),full_page=True,mask=[p.locator('input[type=password],input[name*="Code"]')]);r['screenshot']='screenshots/'+file
    RESULTS.append(r);(OUT/'results.json').write_text(json.dumps(RESULTS,indent=2),encoding='utf-8');print(json.dumps(r),flush=True)
def sql(query):
    proc=subprocess.run(['sqlcmd','-S','localhost','-E','-C','-b','-d',DB,'-h','-1','-W','-Q','SET NOCOUNT ON; '+query],capture_output=True,text=True,encoding='utf-8',errors='replace')
    if proc.returncode:raise RuntimeError(proc.stdout+proc.stderr)
    return proc.stdout.strip()
def fixture(action):
    env=os.environ.copy();env['ORANGE_E2E_GUARD_ACTION']=action
    with open(PRIVATE.parent/'fixture-transitions.log','ab') as log:
        p=subprocess.run(['dotnet',str(ROOT/'Orange-Tendering-System/Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll'),'--provision-browser'],env=env,stdout=log,stderr=subprocess.STDOUT)
    assert p.returncode==0,'Isolated fixture transition failed'
def login(p,a):
    p.goto(BASE+'/Identity/Account/Login');p.locator('[name="Input.Email"]').fill(a['email']);p.locator('[name="Input.Password"]').fill(a['password']);p.locator('#login-submit').click();p.wait_for_load_state()
def csrf(p):return p.locator('input[name=__RequestVerificationToken]').first.input_value()
def post(c,p,path,body):return c.request.post(BASE+path,data=body,headers={'RequestVerificationToken':csrf(p)})
def logout(p):
    p.goto(BASE+'/Biometrics/Fingerprint');p.locator('summary[aria-label="Account menu"]').click();p.get_by_role('button',name='Sign out',exact=True).click();p.wait_for_load_state();p.goto(BASE+'/Identity/Account/Login')
def run(browser):
    ac=browser.new_context(ignore_https_errors=True,viewport={'width':1440,'height':1000});ap=ac.new_page();errors=[]
    ap.on('pageerror',lambda e:errors.append(str(e)))
    ap.goto(BASE+'/Identity/Account/Login');assert ap.evaluate('!!(window.jQuery && jQuery.validator && jQuery.validator.unobtrusive)')
    assert ap.locator('#btnFaceLogin').is_disabled();result('Login validation libraries load in order','PASS','Local jQuery, validation and unobtrusive validation exist; Face Login remains disabled.',ap)
    login(ap,ADMIN)
    if 'FirstLoginPasswordChange' in ap.url:
        ap.locator('[name="Input.CurrentPassword"]').fill(ADMIN['password']);new=ADMIN['password']+'X';ap.locator('[name="Input.NewPassword"]').fill(new);ap.locator('[name="Input.ConfirmPassword"]').fill(new);ap.locator('form').filter(has=ap.locator('[name="Input.CurrentPassword"]')).locator('button[type=submit]').click();ap.wait_for_load_state();ADMIN['password']=new;PRIVATE.write_text(json.dumps(S),encoding='utf-8')
    assert '/Admin' in ap.url;result('Existing Identity password login and Admin dashboard','PASS','Actual bootstrap Identity login/password change works; authorization retained.',ap)
    c=browser.new_context(ignore_https_errors=True,viewport={'width':1440,'height':1000});p=c.new_page();p.on('pageerror',lambda e:errors.append(str(e)))
    p.goto(BASE+'/Biometrics/Fingerprint');assert '/Login' in p.url;result('Anonymous enrollment page blocked','PASS','Actual protected enrollment page redirects anonymous users to Identity login.',p)
    p.goto(BASE+'/Identity/Account/Login');r=c.request.post(BASE+'/Biometrics/PasskeyLoginOptions',data={});assert r.status in (400,405);result('Missing antiforgery token rejected','PASS',f'Actual POST without CSRF token rejected HTTP {r.status}; empty-error status-page re-execution can yield 405.')
    p.goto(BASE+'/Identity/Account/VerifyEmail?email='+quote(USER['email']));p.locator('[name="Input.Code"]').fill(USER['otp']);p.locator('form').filter(has=p.locator('[name="Input.Code"]')).locator('button[type=submit]').click();p.wait_for_load_state();assert 'awaiting administrator approval' in p.locator('body').inner_text()
    ap.goto(BASE+'/Admin/Suppliers/Review/'+str(USER['supplierId']));ap.locator('[name=notes]').fill('Synthetic isolated biometric fixture reviewed');ap.locator('button[name=approve][value=true]').click();ap.wait_for_load_state()
    login(p,USER);assert '/Supplier' in p.url;result('Existing supplier Identity login after approval','PASS','Real Identity OTP consumption and actual Admin company approval completed for isolated fixture. No email delivery claimed.',p)
    p.goto(BASE+'/Biometrics/Fingerprint');cdp=c.new_cdp_session(p);cdp.send('WebAuthn.enable');authenticator=cdp.send('WebAuthn.addVirtualAuthenticator',{'options':{'protocol':'ctap2','transport':'internal','hasResidentKey':True,'hasUserVerification':True,'isUserVerified':True,'automaticPresenceSimulation':True}})['authenticatorId']
    bodies={}
    def capture(req):
        if req.method=='POST' and req.url.rsplit('/',1)[-1] in ['PasskeyRegister','PasskeyLogin']:bodies[req.url.rsplit('/',1)[-1]]=req.post_data_json
    p.on('request',capture)
    p.locator('#passkeyLabel').fill('Virtual WebAuthn authenticator - automated test');p.locator('#bioPassword').fill('incorrect-password')
    with p.expect_response(lambda r:r.url.endswith('/Biometrics/PasskeyRegisterOptions')) as response:p.locator('#btnPasskeyRegister').click()
    assert response.value.status==401;assert sql('SELECT COUNT(*) FROM UserPasskeyCredentials')=='0';result('Enrollment password recheck enforced','PASS','Incorrect current password cannot issue enrollment options or store a credential.',p)
    p.locator('#bioPassword').fill(USER['password'])
    with p.expect_response(lambda r:r.url.endswith('/Biometrics/PasskeyRegister')) as response:p.locator('#btnPasskeyRegister').click()
    assert response.value.status==200;p.wait_for_timeout(700);p.reload();assert p.get_by_text('Virtual WebAuthn authenticator - automated test',exact=True).count()==1
    assert sql('SELECT COUNT(*) FROM UserPasskeyCredentials')=='1';result('Passkey enrollment using real browser WebAuthn API','PASS','Chromium virtual CTAP2 authenticator executed navigator.credentials.create; real server stored its public key in SQL. Not a physical fingerprint test.',p)
    replay=post(c,p,'/Biometrics/PasskeyRegister',bodies['PasskeyRegister']);assert replay.status==400;assert sql('SELECT COUNT(*) FROM UserPasskeyCredentials')=='1';result('Enrollment replay rejected','PASS','Reusing the real browser enrollment response returns 400 and no duplicate credential.')
    credential=int(sql('SELECT Id FROM UserPasskeyCredentials'))
    ap.goto(BASE+'/Biometrics/Fingerprint');r=post(ac,ap,'/Biometrics/PasskeyRemove?id='+str(credential),{});assert r.status==404;result('Cross-account credential removal rejected','PASS','Even Admin cannot remove another account credential through the owner-only endpoint.')
    p.goto(BASE+'/Biometrics/Face');assert p.locator('#btnFaceEnroll').is_disabled();result('Camera face enrollment safely disabled','PASS','Legacy camera code retained; enrollment control disabled. Stored legacy records are not trusted.',p)
    r=post(c,p,'/Biometrics/FaceEnroll',{'token':'forged','verified':True,'live':True,'descriptors':[{'t':2000,'v':[0]*128}],'frames':[]});assert r.status==501;assert sql('SELECT COUNT(*) FROM FaceTemplates')=='0';result('Forged browser face enrollment rejected','PASS','Server rejects authenticated JS descriptors/liveness; no templates are stored.')
    logout(p)
    r=post(c,p,'/Biometrics/FaceLogin',{'email':USER['email'],'password':USER['password'],'verified':True,'live':True,'descriptors':[{'t':2000,'v':[0]*128}]});assert r.status==501
    p.goto(BASE+'/Supplier');assert '/Login' in p.url;result('Face login cannot authenticate with forged decisions','PASS','Returns 501 and grants no cookie, even with a correct password and FaceLoginRequiresPassword=false test configuration.',p)
    p.goto(BASE+'/Identity/Account/Login')
    with p.expect_response(lambda r:r.url.endswith('/Biometrics/PasskeyLogin')) as response:p.locator('#btnPasskeyLogin').click()
    assert response.value.status==200;p.wait_for_url('**/Supplier');result('Real browser passkey login','PASS','navigator.credentials.get produced a signed assertion; actual server issued the Identity cookie and loaded Supplier dashboard.',p)
    assertion=bodies['PasskeyLogin'];logout(p);r=post(c,p,'/Biometrics/PasskeyLogin',assertion);assert r.status==401;assert p.locator('#btnFaceLogin').is_disabled();result('Authentication assertion replay rejected','PASS','Captured signed assertion cannot be reused; no new cookie granted and Face Login stays disabled.',p)
    for action,restore,label in [('bio-inactive','bio-active','inactive account'),('bio-unconfirmed','bio-confirmed','unconfirmed email'),('bio-lock','bio-unlock','locked account'),('bio-supplier-pending','bio-supplier-approved','unapproved supplier'),('bio-mfa-on','bio-mfa-off','MFA-enabled account')]:
        fixture(action)
        try:
            p.goto(BASE+'/Identity/Account/Login')
            with p.expect_response(lambda r:r.url.endswith('/Biometrics/PasskeyLogin')) as response:p.locator('#btnPasskeyLogin').click()
            assert response.value.status==401;assert p.locator('#btnFaceLogin').is_disabled();p.goto(BASE+'/Supplier');assert '/Login' in p.url;result('Passkey restriction: '+label,'PASS','A fresh valid browser assertion is denied by the actual account policy; protected dashboard remains inaccessible.',p)
        finally:fixture(restore)
        p.goto(BASE+'/Identity/Account/Login')
        with p.expect_response(lambda r:r.url.endswith('/Biometrics/PasskeyLogin')) as response:p.locator('#btnPasskeyLogin').click()
        assert response.value.status==200;p.wait_for_url('**/Supplier');logout(p)
    p.goto(BASE+'/Identity/Account/Register');assert p.evaluate('!!(window.jQuery && jQuery.validator && jQuery.validator.unobtrusive)');p.goto(BASE+'/Identity/Account/ForgotPassword');assert p.evaluate('!!jQuery.validator.unobtrusive');assert not errors,errors
    result('Validation works across Identity pages without JS errors','PASS','Login, Register, Forgot Password, passkey and face management pages ran with no uncaught JavaScript errors.',p)
    assert int(sql('SELECT SignCount FROM UserPasskeyCredentials'))>0;result('Credential persistence and counter progression','PASS',sql('SELECT COUNT(*) AS Credentials,MAX(SignCount) AS SignCount FROM UserPasskeyCredentials;'))
    result('Physical fingerprint reader / Windows Hello','BLOCKED','Only Chromium virtual-authenticator protocol tests were executed; no physical fingerprint or human biometric claim.')
    result('Camera-based face authentication security','BLOCKED','No secure server-verifiable face proof is implemented or verified. Face Login remains disabled.')
    c.close();ac.close()
with sync_playwright() as pw:
    browser=pw.chromium.launch(channel='chromium',headless=True)
    try:run(browser)
    except Exception as e:result('Browser execution failure','FAIL',scrub(str(e)));print(scrub(traceback.format_exc()),flush=True);raise SystemExit(1)
    finally:browser.close()
