import json,html,math,sys,re
from pathlib import Path
from playwright.sync_api import sync_playwright
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'output/theme-evidence';OUT.mkdir(parents=True,exist_ok=True)
STATE=json.loads((ROOT/'output/theme-private-accounts.json').read_text(encoding='utf-8-sig'))
BASE=(ROOT/'output/theme-host-address.txt').read_text().strip()
checks=[];errors=[];shots=[]
def check(ok,name):
 checks.append({'status':'PASS' if ok else 'FAIL','name':name});print(checks[-1]['status']+' '+name,flush=True)
def rgb(s):
 v=re.findall(r'[\d.]+',s);return [float(x)/255 for x in v[:3]] if len(v)>=3 else [0,0,0]
def luminance(s):
 v=rgb(s);v=[x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4 for x in v];return sum(a*b for a,b in zip(v,[.2126,.7152,.0722]))
def contrast(a,b):
 x,y=sorted([luminance(a),luminance(b)]);return (y+.05)/(x+.05)
def palette(page):
 return page.evaluate('''() => {const e=document.querySelector('.as-shell')||document.body;const s=getComputedStyle(e);return {color:s.color,background:s.backgroundColor,scheme:getComputedStyle(document.documentElement).colorScheme}}''')
def mode(page,target,label,screenshot=False):
 def click_toggle():
  button=page.locator('#theme-toggle,.ws-theme-toggle,[data-theme-toggle]').first
  if not button.is_visible():
   if page.locator('.ws-account>summary').count():page.locator('.ws-account>summary').click()
   elif page.locator('[aria-controls="hc-links"]').count():page.locator('[aria-controls="hc-links"]').click()
  button.click()
 if page.locator('html').get_attribute('data-theme')==target and page.evaluate('localStorage.getItem("orange-ui-theme")')!=target:click_toggle()
 if page.locator('html').get_attribute('data-theme')!=target:click_toggle()
 if page.locator('.ws-account[open]>summary').count():page.locator('.ws-account[open]>summary').click()
 if page.locator('[aria-controls="hc-links"][aria-expanded="true"]').count() and page.viewport_size['width']<1180:page.locator('[aria-controls="hc-links"]').click()
 page.wait_for_function('(t)=>document.documentElement.dataset.theme===t',arg=target)
 page.wait_for_timeout(400)
 check(page.evaluate('localStorage.getItem("orange-ui-theme")')==target,label+': saved '+target)
 p=palette(page);check(p['scheme']==target,label+': native '+target+' controls')
 check(contrast(p['color'],p['background'])>=4.5,label+': readable '+target+' primary text')
 if page.locator('.as-field input[type=email]').count():
  field=page.locator('.as-field input[type=email]').first.evaluate('(e)=>({color:getComputedStyle(e).color,bg:getComputedStyle(e).backgroundColor})')
  check(contrast(field['color'],field['bg'])>=4.5,label+': readable '+target+' login field')
 if screenshot:
  name=re.sub('[^A-Za-z0-9-]','-',label)+'-'+target+'.png';page.screenshot(path=str(OUT/name));shots.append(name)
 return p
def visit(page,path,label,screenshot=False):
 response=page.goto(BASE+path,wait_until='domcontentloaded');check(response is not None and response.status<400,label+': page available')
 page.wait_for_function('!!window.orangeTheme')
 light=mode(page,'light',label,screenshot);dark=mode(page,'dark',label,screenshot)
 check(light['background']!=dark['background'],label+': surface changes between modes')
def safe_error(e):
 text=str(e)
 for a in STATE['accounts']:text=text.replace(a['password'],'[redacted]')
 return text[:500]
try:
 with sync_playwright() as p:
  browser=p.chromium.launch(headless=True)
  context=browser.new_context(ignore_https_errors=True,viewport={'width':1440,'height':960},reduced_motion='reduce',color_scheme='light')
  page=context.new_page();page.on('pageerror',lambda e:errors.append(str(e)))
  page.goto(BASE,wait_until='domcontentloaded');page.wait_for_function('!!window.orangeTheme')
  check(page.locator('html').get_attribute('data-theme')=='light','system preference applied before interaction')
  page.emulate_media(color_scheme='dark');page.wait_for_function('document.documentElement.dataset.theme==="dark"');check(True,'OS theme change followed without explicit preference')
  for path,label,shot in [('/','Home',True),('/about','About',False),('/team','Team',False),('/how-it-works','HowItWorks',False),('/contact','Contact',False),('/Identity/Account/Login','Login',True),('/Identity/Account/Register','Register',True),('/Identity/Account/ForgotPassword','ForgotPassword',False),('/Identity/Account/VerifyEmail','VerifyEmail',False)]:visit(page,path,label,shot)
  page.reload(wait_until='domcontentloaded');check(page.locator('html').get_attribute('data-theme')=='dark','saved theme survives reload')
  other=context.new_page();other.goto(BASE+'/about',wait_until='domcontentloaded');page.locator('#theme-toggle').click();other.wait_for_function('document.documentElement.dataset.theme==="light"');check(True,'theme synchronizes across real browser tabs');other.close();context.close()
  roles=[('admin','Admin'),('department','Department'),('officer','SourcingOfficer'),('manager','SourcingManager'),('department-evaluator','EvaluationCommittee'),('top1','TopCommittee'),('supplier1','Supplier')]
  extra={'Admin':['/Admin/Users','/Admin/Suppliers'],'Department':['/Department/PurchaseRequests'],'SourcingOfficer':['/SourcingOfficer/Tenders','/SourcingOfficer/Reports'],'SourcingManager':['/SourcingManager/Reports'],'EvaluationCommittee':['/EvaluationCommittee/Workflow'],'TopCommittee':['/TopCommittee/Decisions/History'],'Supplier':['/Supplier/Offers','/Supplier/Documents','/Supplier/Clarifications']}
  for name,area in roles:
   account=next(a for a in STATE['accounts'] if a['name']==name)
   c=browser.new_context(ignore_https_errors=True,viewport={'width':1440,'height':960},reduced_motion='reduce');c.add_init_script('localStorage.setItem("orange-ui-theme","dark")')
   tab=c.new_page();tab.on('pageerror',lambda e:errors.append(str(e)));tab.goto(BASE+'/Identity/Account/Login',wait_until='domcontentloaded')
   if name!='supplier1':tab.locator('input[name="Input.AccountType"][value="other"]').check()
   tab.locator('#Input_Email').fill(account['email']);tab.locator('#Input_Password').fill(account['password']);tab.locator('#login-submit').click()
   tab.wait_for_url(lambda u:'/Identity/Account/Login' not in str(u),timeout=45000)
   check('/'+area in tab.url,area+': real Identity dashboard login')
   visit(tab,'/'+area,area,True)
   for path in extra[area]:visit(tab,path,area+'-'+path.split('/')[-1])
   visit(tab,'/Notifications',area+'-Notifications');visit(tab,'/Identity/Account/Manage',area+'-Account')
   tab.set_viewport_size({'width':390,'height':844});visit(tab,'/'+area,area+'-Mobile',area=='Supplier')
   c.close()
  mobile=browser.new_context(ignore_https_errors=True,viewport={'width':390,'height':844},reduced_motion='reduce');mp=mobile.new_page();mp.goto(BASE+'/Identity/Account/Login',wait_until='domcontentloaded');mp.locator('[aria-controls="hc-links"]').click();mode(mp,'light','MobileLogin',True);mode(mp,'dark','MobileLogin',True);mobile.close()
  blocked=browser.new_context(ignore_https_errors=True,color_scheme='dark',reduced_motion='reduce');blocked.add_init_script("Object.defineProperty(window,'localStorage',{get(){throw new DOMException('Storage blocked','SecurityError')}})")
  b=blocked.new_page();b.goto(BASE+'/Identity/Account/Login',wait_until='domcontentloaded');b.wait_for_function('!!window.orangeTheme');check(b.locator('html').get_attribute('data-theme')=='dark','storage blocked: OS preference still works');b.locator('#theme-toggle').click();b.wait_for_function('document.documentElement.dataset.theme==="light"');check(True,'storage blocked: manual switching still works');blocked.close()
  minimal=browser.new_context(color_scheme='dark');m=minimal.new_page();m.set_content('<html><body>Theme null-safety check</body></html>');m.add_script_tag(path=str(ROOT/'Orange-Tendering-System/wwwroot/js/theme-init.js'));check(m.evaluate('window.orangeTheme.current')=='dark','controller works without a toggle or theme meta element');minimal.close()
  browser.close()
except Exception as e:
 check(False,'browser execution: '+type(e).__name__);print(safe_error(e),flush=True)
finally:
 check(not errors,'no uncaught page JavaScript errors')
 result={'pass':sum(x['status']=='PASS' for x in checks),'fail':sum(x['status']=='FAIL' for x in checks),'checks':checks,'javascript_errors':errors,'screenshots':shots}
 (OUT/'results.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 body=''.join('<li>'+html.escape(x['status']+' '+x['name'])+'</li>' for x in checks)
 figures=''.join('<figure><figcaption>'+html.escape(n)+'</figcaption><img style="width:100%;max-width:1000px" src="'+n+'"></figure>' for n in shots)
 (OUT/'report.html').write_text('<!doctype html><meta charset="utf-8"><title>WorkV3 theme verification</title><h1>Theme browser verification</h1><p>PASS '+str(result['pass'])+'; FAIL '+str(result['fail'])+'</p><ul>'+body+'</ul>'+figures,encoding='utf-8')
 print('RESULT PASS='+str(result['pass'])+' FAIL='+str(result['fail'])+' SCREENSHOTS='+str(len(shots)),flush=True)
 sys.exit(1 if result['fail'] else 0)
