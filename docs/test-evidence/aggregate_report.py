"""Consolidate actual recorded browser output. Does not execute or invent test outcomes."""
import json,re,html,collections,hashlib
from pathlib import Path
from urllib.parse import quote
import os

OUT=Path(__file__).resolve().parent
PRIVATE=Path(os.environ['ORANGE_E2E_PRIVATE_STATE'])
BACKUP=PRIVATE.parent
state=json.loads(PRIVATE.read_text())
def sanitize(text):
    for a in [state['admin']]+state['accounts']:
        for k in ['password','otp','activationCode','resetCode']:
            value=a.get(k)
            if value:text=text.replace(value,'[REDACTED]').replace(quote(value,safe=''),'[REDACTED]')
    text=re.sub(r'E2e![0-9a-f]{36}[XR]*','[REDACTED TEST PASSWORD]',text)
    return re.sub(r'([?&](?:code|token)=)[^&\s]+',r'\1[REDACTED]',text,flags=re.I)

logs=OUT/'logs';logs.mkdir(exist_ok=True)
attempts=[]
for p in sorted(BACKUP.glob('*.log'),key=lambda p:p.stat().st_mtime):
    text=sanitize(p.read_text(encoding='utf-8-sig',errors='replace'))
    (logs/p.name).write_text(text,encoding='utf-8')
    if not p.name.startswith('browser-'):continue
    for line in text.splitlines():
        try:r=json.loads(line)
        except json.JSONDecodeError:continue
        if isinstance(r,dict) and all(k in r for k in ['name','status','detail']):
            if r['name']=='First bidding round opened' and p.name=='browser-cycle.log':
                r['detail'] += ' [RECORDED PASS INVALIDATED: native datetime validation prevented submission in this early attempt. Later rerun checked SQL Open state before recording PASS.]'
            if r['name']=='PDF predefined signature page native viewer' and p.name=='browser-final-verification.log':
                r['detail'] += ' [RECORDED PASS INVALIDATED BY VISUAL REVIEW: fragment navigation left viewer on page 1. Final rerun selected and asserted page 7 using the native control.]'
            if r['name']=='Generated PDF displayed through protected report UI' and p.name=='browser-approvals.log':
                r['detail'] += ' [INITIAL CAPTURE LIMITATION: headless shell fetched PDF but frame rendered blank. Full Chromium subsequently displayed the actual PDF; use the final screenshot.]'
            attempts.append({**r,'source':'logs/'+p.name})

latest={r['name']:r for r in attempts if not r['name'].startswith('Execution failure in ')}
counts=dict(collections.Counter(r['status'] for r in latest.values()))
for key in ['PASS','FAIL','BLOCKED']:counts.setdefault(key,0)
assert counts['FAIL']==0, 'A final named checkpoint is still failed; report it rather than claiming completion.'
for r in latest.values():
    slug=re.sub('[^a-z0-9]+','-',r['name'].lower())[:75]
    images=list((OUT/'screenshots').glob('*-'+slug+'.png'))
    # Phase-summary success was not itself necessarily screenshot-captured; do not
    # associate an earlier failed phase's screenshot with the successful summary.
    if images and 'phase' not in r['name'].lower() and r['name']!='Additional live UI checks':
        r['screenshot']='screenshots/'+max(images,key=lambda p:p.stat().st_mtime).name

summary={'browser_latest_named_checkpoints':counts,'scope':'Latest actual outcome per named checkpoint; aggregate phase checks included. Historical attempts retained separately.',
 'database':'OrangeAuthCheck_BrowserE2E_20261113','accounts':14,'primary_tender_id':state['tenderId'],'report_id':state['reportId'],
 'regression_auth_checkpoints':142,'regression_tests':35,
 'regression_limitation':'Existing regression harnesses use captured email/substituted external transports. They are not included in the real-browser totals.',
 'fixture_boundary':'Staff activation, supplier OTP and password-reset tokens were issued by real Identity services during isolated fixture setup and consumed through real browser pages. This does not prove registration-to-email-to-activation end-to-end delivery.',
 'blockers':[r['name'] for r in latest.values() if r['status']=='BLOCKED'],
 'screenshots':len(list((OUT/'screenshots').glob('*.png'))),'backup_directory':str(BACKUP),
 'tested_source_commit':'1cf129f0013f36ffe6d0be6e2995b45710a92d3c',
 'concurrent_working_tree_warning':'Unrelated biometric/Identity/DbContext/snapshot edits and a tenth AddBiometricSignIn migration appeared during finalization. They were not staged, applied or verified by this run. Results cover the tested nine-migration build, not those concurrent edits.'}
(OUT/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
(OUT/'results.json').write_text(json.dumps({'summary':summary,'latest':list(latest.values()),'attempts':attempts},indent=2),encoding='utf-8')
rows=[]
for r in latest.values():
    image=f'<a href="{r["screenshot"]}"><img src="{r["screenshot"]}" loading="lazy"></a>' if r.get('screenshot') else ''
    rows.append(f'<article class="{r["status"]}"><h3>{html.escape(r["status"]+": "+r["name"])}</h3><p>{html.escape(r["detail"])}</p><a href="{r["source"]}">Actual execution log</a>{image}</article>')
history=''.join('<tr><td>'+html.escape(r['status'])+'</td><td>'+html.escape(r['name'])+'</td><td>'+html.escape(r['detail'])+'</td><td><a href="'+r['source']+'">log</a></td></tr>' for r in attempts)
links=''.join(f'<li><a href="logs/{p.name}">{html.escape(p.name)}</a></li>' for p in sorted(logs.glob('*.log')))
sql=(logs/'sql-verification.log').read_text(encoding='utf-8')
production=(logs/'production-readonly-verification.log').read_text(encoding='utf-8')
page='''<!doctype html><html lang="en"><meta charset="utf-8"><title>Orange real browser E2E report</title><style>
body{font:16px system-ui;background:#f5f5f5;color:#17253d;max-width:1320px;margin:36px auto;padding:0 24px}header,section,article{background:white;border-radius:10px;padding:24px;margin:18px 0}header{border-top:8px solid #ff7900}article{border-left:5px solid #18803a}article.BLOCKED{border-color:#d77800}article.FAIL{border-color:#b22}img{display:block;max-width:100%;max-height:720px;margin:16px auto}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{padding:10px;border:1px solid #ddd;vertical-align:top}a{color:#9b4500}h1{margin-top:0}</style>
<header><h1>Orange — real browser end-to-end execution</h1><p>13 November 2026 · Actual Chromium + Kestrel + Identity + SQL Server + live Ollama gemma3:4b</p><h2>'''+html.escape(str(counts))+'''</h2><p>Final result per named browser checkpoint (including aggregate phase assertions). Earlier failed attempts and corrected runner errors remain in the chronological log section below. They are not represented as unresolved failures.</p></header>
<section><h2>Result and boundaries</h2><p><strong>Concurrent-change warning:</strong> unrelated biometric/Identity/DbContext/snapshot edits and a tenth AddBiometricSignIn migration appeared in the shared working tree during finalization. They were left untouched and uncommitted. These results cover tested source commit <code>1cf129f</code> and its nine-migration build, not those concurrent edits.</p><p>The core browser cycle reached <strong>Awarded and Closed</strong>: Purchase Request, Officer acceptance, tender, four proposals averaging 45/55, locked weights, re-tender, three supplier bids, compliance, live Ollama analysis, scheduled evaluation, four independent submissions, End Evaluation, automatic scores/PDF, four committee signatures, separate Manager signature, and four Top decisions (three approve, one reject). Final scores are 85.75, 75.75 and 65.75.</p>
<p><strong>This is not an unconditional all-integrations PASS.</strong> Actual SMTP is unavailable: supplier self-registration/email delivery, employee invitation delivery, and the Forgot Password email path remain blocked. Zoom API credentials are absent. The schedule used a syntactically valid test Zoom link; no real meeting existence or attendance is claimed.</p>
<p>Fourteen isolated accounts were provisioned through the real Identity services. Employee activation, supplier OTP, and password reset consumed genuine fixture-issued Identity tokens through real browser pages. Those checks validate token consumption and authorization, not email issuance/delivery. Pending companies were approved through the real Admin UI. No application service was mocked in the browser deployment.</p>
<p>The existing supplementary suites passed 142 authentication checkpoints and 35 regression tests (SQL Server persistence plus pure guard/calculation checks). Those suites do use captured email or substituted external HTTP and are intentionally separate from the real-browser totals.</p>
<p>Electronic acknowledgments were tested; trusted certificate-backed digital signing was not. Face ID remains disabled/incomplete. Production business counts remain zero, as before the test. All test data and private files are outside the production database/storage.</p></section>
<section><h2>Defects fixed on testing branch</h2><ul><li>Wired previously disabled Purchase Request submission/review with ownership, snapshots, serializable review, audit and notifications. This exercises the single-line submission/acceptance path; broader editing/resubmission is not claimed.</li><li>Wired Admin listing, protected employee invitation creation, account details and activation/deactivation. Live invitation creation safely rolls back without SMTP. Broader role-editing administration was not tested.</li><li>Corrected boolean form rendering and rejected invalid account-state model binding; reran deactivation, cookie revocation and reactivation successfully.</li><li>Corrected literal Razor report-version text in all four report views; browser now shows v1.</li></ul><p>Runner corrections included actual button labels, valid minute-resolution dates, required confirmation checkboxes, checking SQL state before recording bidding success, and full Chromium for the native PDF viewer. A URL fragment did not move the PDF page; the final signature-page capture uses the native page-number control asserted as 7.</p></section>
<section><h2>SQL verification</h2><pre>'''+html.escape(sql)+'''</pre><h2>Production read-only verification and backup</h2><pre>'''+html.escape(production)+'''</pre><p>Backup: '''+html.escape(str(BACKUP))+''' . Project archive and manifest, Git bundle/local patch, COPY_ONLY/CHECKSUM database backup and private runtime state are retained there. Secrets and private documents are not copied into this report.</p></section>
<section><h2>Final named checkpoints</h2></section>'''+''.join(rows)+'''<section><h2>All recorded attempts</h2><p>Includes earlier failures, fixed UI defects, runner errors and reruns. Final totals above use the latest actual outcome for each named checkpoint. The initial generic "Execution failure in auth" is a runner event, not a separate functional test.</p><table><tr><th>Status</th><th>Checkpoint</th><th>Actual recorded detail</th><th>Source</th></tr>'''+history+'''</table></section><section><h2>Actual execution logs</h2><p>Copied from the execution output, with credential/token redaction only. Original private logs remain in the backup folder.</p><ul>'''+links+'''</ul></section></html>'''
(OUT/'report.html').write_text(page,encoding='utf-8')
print(json.dumps(summary,indent=2))
