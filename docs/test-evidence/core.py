import os,json,re,html,subprocess,time
from pathlib import Path
from datetime import datetime,timezone,timedelta
from urllib.parse import quote
from playwright.sync_api import sync_playwright
OUT=Path(__file__).resolve().parent
PRIVATE=Path(os.environ['ORANGE_E2E_PRIVATE_STATE'])
S=json.loads(PRIVATE.read_text());BASE='https://localhost:7443'
def load_records():
    data=json.loads((OUT/'results.json').read_text()) if (OUT/'results.json').exists() else []
    return data.get('latest',[]) if isinstance(data,dict) else data
R=load_records()
def clean(s):
    s=str(s)
    for a in [S['admin']]+S['accounts']:
        for k in ['password','otp','activationCode']:
            if a.get(k):s=s.replace(a[k],'[REDACTED]')
    return re.sub(r'([?&](?:code|token)=)[^&\s]+',r'\1[REDACTED]',s,flags=re.I)
def render():
    (OUT/'results.json').write_text(json.dumps(R,indent=2),encoding='utf-8')
    latest={r['name']:r for r in R};totals={s:sum(r['status']==s for r in latest.values()) for s in ['PASS','FAIL','BLOCKED']}
    cards=[]
    for r in R:
        image=f'<a href="{r["screenshot"]}"><img src="{r["screenshot"]}" loading="lazy"></a>' if r.get('screenshot') else ''
        cards.append('<article><h2>'+html.escape(r['status']+': '+r['name'])+'</h2><pre>'+html.escape(r['detail'])+'</pre>'+image+'</article>')
    (OUT/'report.html').write_text('<!doctype html><meta charset="utf-8"><title>Orange real browser E2E</title><style>body{font:16px system-ui;margin:40px;background:#eee}article{background:white;border-top:5px solid #ff7900;margin:20px 0;padding:20px}pre{white-space:pre-wrap}img{max-width:100%;max-height:650px}</style><h1>Orange real browser E2E</h1><p>Latest result per named checkpoint: '+html.escape(str(totals))+'</p><p>Chronological attempts below. Actual Chromium / Kestrel / SQL Server; no substituted application services. Fixture-issued Identity tokens do not establish email delivery. Password/OTP fields are masked.</p>'+''.join(cards),encoding='utf-8')
def record(name,status,detail,page=None):
    global R
    R=load_records()
    r={'name':name,'status':status,'detail':clean(detail),'utc':datetime.now(timezone.utc).isoformat()}
    if page and not page.is_closed():
        f=f'{len(R)+1:03}-'+re.sub('[^a-z0-9]+','-',name.lower())[:75]+'.png'
        page.screenshot(path=str(OUT/'screenshots'/f),full_page=True,mask=[page.locator('input[type=password],input[name*="Code"],input[name*="code"]')]);r['screenshot']='screenshots/'+f
    with open(OUT/('events-'+str(os.getpid())+'.jsonl'),'a',encoding='utf-8') as log:log.write(json.dumps(r)+'\n')
    R.append(r);render();print(json.dumps({k:r[k] for k in ['name','status','detail']}),flush=True)
def sql(q):
    p=subprocess.run(['sqlcmd','-S','localhost','-E','-C','-b','-d','OrangeAuthCheck_BrowserE2E_20261113','-h','-1','-W','-w','65535','-Q','SET NOCOUNT ON; '+q],capture_output=True,text=True,encoding='utf-8',errors='replace')
    if p.returncode:raise RuntimeError(p.stdout+p.stderr)
    return p.stdout.strip()
def a(n):return S['admin'] if n=='admin' else next(x for x in S['accounts'] if x['name']==n)
def ctx(b,n):
    p=PRIVATE.parent/(n+'-browser.json')
    return b.new_context(ignore_https_errors=True,viewport={'width':1440,'height':1000},timezone_id='UTC',storage_state=str(p) if p.exists() else None)
def keep(c,n):c.storage_state(path=str(PRIVATE.parent/(n+'-browser.json')))
def login(p,u):
    p.goto(BASE+'/Identity/Account/Login');p.locator('[name="Input.Email"]').fill(u['email']);p.locator('[name="Input.Password"]').fill(u['password']);p.locator('#login-submit').click();p.wait_for_load_state()
def f(p,action):return p.locator('form[action*="/'+action+'/"]')
def fill(form,values):
    for k,v in values.items():form.locator('[name="'+k+'"]').fill(str(v))
def send(form):
    for checkbox in form.locator('input[type=checkbox][required]').all():checkbox.check()
    assert form.evaluate('(f)=>f.checkValidity()'), 'Native browser form validation failed'
    form.locator('button[type=submit],button:not([type])').first.click()
