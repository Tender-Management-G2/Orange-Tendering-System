"use strict";
// Orange AI floating assistant: once-per-login welcome, persisted chat state across pages, safe Markdown rendering (DOM text nodes
// only, never innerHTML), server-confirmed file cards, document upload, quick actions, history and an expanded workspace.
(() => {
  const root = document.querySelector('[data-ai-chat]');
  if (!root) return;
  const $ = s => root.querySelector(s);
  const fab = $('.ai-fab'), panel = $('.ai-panel'), box = $('.ai-messages'), form = $('.ai-form'), input = $('textarea');
  const send = $('.ai-send'), hist = $('.ai-history'), histList = $('.ai-history ul'), histEmpty = $('.ai-history-empty');
  const welcome = $('.ai-welcome'), actionsBar = $('.ai-actions'), suggest = $('.ai-suggest'), fileInput = $('.ai-file'), drop = $('.ai-dropzone');
  const greeting = $('.ai-greeting'), dot = $('.ai-fab-dot');
  const csrf = root.dataset.csrf, ar = root.dataset.arabic === 'true', userKey = root.dataset.user || 'anon';
  const T = (en, a) => ar ? a : en;
  const STATE_KEY = 'orange-ai-state:' + userKey, WELCOME_KEY = 'orange-ai-welcomed:' + userKey;
  let conversationId = null, busy = false, welcomeData = null, loaded = false, voiceMode = false, voiceLang = ar ? 'ar' : 'en';

  const store = {
    get(k, s) { try { return JSON.parse((s || sessionStorage).getItem(k) || 'null'); } catch { return null; } },
    set(k, v, s) { try { (s || sessionStorage).setItem(k, JSON.stringify(v)); } catch { /* storage unavailable */ } }
  };
  const saveState = () => store.set(STATE_KEY, { conversationId, open: !panel.hidden, expanded: root.classList.contains('is-expanded'), voiceMode, voiceLang });

  const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  async function api(url, body, isForm) {
    const init = body === undefined ? { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } }
      : isForm ? { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf, Accept: 'application/json' }, body }
      : { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(body) };
    let res; try { res = await fetch(url, init); } catch { throw new Error(T('Connection problem. Check your network and try again.', 'مشكلة في الاتصال. تحقق من الشبكة وحاول مرة أخرى.')); }
    let data = null; try { data = await res.json(); } catch { /* no body */ }
    if (!res.ok || !data) {
      const msg = (data && data.error) || (res.status === 413 ? T('The file is too large (max 10 MB).', 'الملف كبير جدًا (الحد 10 ميغابايت).') : T('The assistant is unavailable. Check your connection or sign in again.', 'المساعد غير متاح. تحقق من الاتصال أو سجّل الدخول مرة أخرى.'));
      const error = new Error(msg); error.conversationId = data?.conversationId; throw error;
    }
    return data;
  }

  // ---------- Safe Markdown (subset): headings, lists, tables, code, bold/italic/code spans, internal links ----------
  const internalLink = /^\/[A-Za-z0-9][A-Za-z0-9/_-]*$/;
  function inline(node, text) {
    const re = /(\*\*[^*]+\*\*|`[^`]+`|_[^_\n]+_|(?<![\w/])\/[A-Za-z][A-Za-z0-9/_-]*)/g;
    let last = 0, m;
    while ((m = re.exec(text))) {
      if (m.index > last) node.append(document.createTextNode(text.slice(last, m.index)));
      const t = m[0];
      if (t.startsWith('**')) node.append(el('strong', null, t.slice(2, -2)));
      else if (t.startsWith('`')) node.append(el('code', null, t.slice(1, -1)));
      else if (t.startsWith('_')) node.append(el('em', null, t.slice(1, -1)));
      else if (internalLink.test(t)) { const a = el('a', 'ai-link', t); a.href = t; node.append(a); }
      else node.append(document.createTextNode(t));
      last = m.index + t.length;
    }
    if (last < text.length) node.append(document.createTextNode(text.slice(last)));
  }
  const isRow = l => /^\s*\|.*\|\s*$/.test(l);
  const cells = l => l.trim().replace(/^\||\|$/g, '').split('|').map(c => c.trim());
  function render(container, text) {
    container.replaceChildren();
    const parts = String(text).replace(/\r/g, '').split(/```/);
    parts.forEach((part, i) => {
      if (i % 2) { container.append(el('pre', null, part.replace(/^\w*\n/, '').trim())); return; }
      const lines = part.split('\n');
      for (let k = 0; k < lines.length; k++) {
        const line = lines[k];
        if (!line.trim()) continue;
        const h = /^(#{1,4})\s+(.*)$/.exec(line);
        if (h) { const n = el('h' + Math.min(6, h[1].length + 2), 'ai-h'); inline(n, h[2]); container.append(n); continue; }
        if (isRow(line) && k + 1 < lines.length && /^\s*\|?[\s:-]+\|[\s|:-]*$/.test(lines[k + 1])) {
          const wrap = el('div', 'ai-table-wrap'); const table = el('table', 'ai-table'); const thead = el('thead'); const tr = el('tr');
          cells(line).forEach(c => { const th = el('th'); inline(th, c); tr.append(th); }); thead.append(tr); table.append(thead);
          const tbody = el('tbody'); k += 2;
          for (; k < lines.length && isRow(lines[k]); k++) { const r = el('tr'); cells(lines[k]).forEach(c => { const td = el('td'); inline(td, c); r.append(td); }); tbody.append(r); }
          k--; table.append(tbody); wrap.append(table); container.append(wrap); continue;
        }
        if (/^\s*([-*•]|\d+[.)])\s+/.test(line)) {
          const ordered = /^\s*\d+[.)]/.test(line); const list = el(ordered ? 'ol' : 'ul', 'ai-list');
          for (; k < lines.length && /^\s*([-*•]|\d+[.)])\s+/.test(lines[k]); k++) { const li = el('li'); inline(li, lines[k].replace(/^\s*([-*•]|\d+[.)])\s+/, '')); list.append(li); }
          k--; container.append(list); continue;
        }
        const p = el('p'); inline(p, line.trim()); container.append(p);
      }
    });
  }

  // ---------- File cards (only for files the server confirmed it saved) ----------
  const fmtSize = b => b < 1024 ? b + ' B' : b < 1048576 ? (b / 1024).toFixed(1) + ' KB' : (b / 1048576).toFixed(1) + ' MB';
  const fmtTime = d => new Date(d.endsWith?.('Z') || /[+-]\d\d:\d\d$/.test(d) ? d : d + 'Z').toLocaleString(ar ? 'ar-JO' : 'en-GB', { timeZone: 'Asia/Amman', year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  const typeLabel = { xlsx: 'Excel', pdf: 'PDF', docx: 'Word', pptx: 'PowerPoint', csv: 'CSV', txt: T('Text', 'نص') };
  function fileCard(file) {
    const card = el('div', 'ai-file-card ai-file-card--' + file.format);
    const badge = el('span', 'ai-file-badge', (file.format || '').toUpperCase());
    const info = el('div', 'ai-file-info');
    info.append(el('strong', null, file.fileName), el('small', null, `${typeLabel[file.format] || file.format} · ${fmtSize(file.sizeBytes)} · ${fmtTime(file.createdAt)}`));
    card.append(badge, info);
    if (file.downloadUrl) {
      const expired = new Date(file.expiresAt.endsWith?.('Z') ? file.expiresAt : file.expiresAt + 'Z') < new Date();
      if (expired) card.append(el('span', 'ai-file-expired', T('Expired', 'منتهي')));
      else { const a = el('a', 'ai-btn ai-btn--primary ai-file-dl', T('Download', 'تنزيل')); a.href = file.downloadUrl; a.setAttribute('download', file.fileName); a.setAttribute('aria-label', T('Download ', 'تنزيل ') + file.fileName); card.append(a); }
    } else card.append(el('span', 'ai-file-tag', T('Uploaded', 'مرفوع')));
    return card;
  }

  // ---------- Persistent knowledge management (server authorization applies to every operation) ----------
  const knowledgePanel = $('[data-ai-knowledge-panel]'), knowledgeToggle = $('[data-ai-knowledge]');
  const knowledgeFile = $('.ai-knowledge-file'), knowledgeTitle = $('.ai-knowledge-title'), knowledgeScope = $('.ai-knowledge-scope');
  const knowledgePreviewBox = $('[data-ai-knowledge-preview-result]'), knowledgeItems = $('[data-ai-knowledge-items]');
  const knowledgePending = $('[data-ai-knowledge-pending]'), knowledgeResults = $('[data-ai-knowledge-results]'), knowledgeStatus = $('[data-ai-knowledge-status]');
  let activeKnowledgePreview = null, knowledgeReplaceId = null;
  const knowledgeAdmin = root.dataset.admin === 'true';
  const knowledgeStatusText = s => ({ Ready: T('Approved and indexed', 'معتمدة ومفهرسة'), PendingApproval: T('Awaiting Admin approval', 'بانتظار اعتماد المدير'), Rejected: T('Rejected', 'مرفوض'), Failed: T('Indexing failed', 'فشلت الفهرسة'), Deleted: T('Deleted', 'محذوفة') }[s] || s);
  function knowledgeMessage(text, bad = false) { knowledgeStatus.textContent = text || ''; knowledgeStatus.classList.toggle('is-bad', !!bad); }
  function knowledgeCard(item, pending = false) {
    const card = el('article', 'ai-knowledge-card');
    const title = el('strong', null, item.title); const meta = el('small', null, `${item.scope} · ${knowledgeStatusText(item.status)} · v${item.version} · ${item.chunkCount} ${T('chunks', 'مقاطع')}`);
    card.append(title, meta, el('p', null, `${item.sourceName}${item.ownerName ? ' · ' + item.ownerName : ''}`));
    if (pending) {
      if (item.reviewExcerpt) card.append(el('p', 'ai-knowledge-excerpt', `${T('Untrusted source excerpt:', 'مقتطف من مصدر غير موثوق:')} ${item.reviewExcerpt}`));
      for (const [approve, label] of [[true, T('Approve', 'اعتماد')], [false, T('Reject', 'رفض')]]) {
        const button = el('button', 'ai-btn', label); button.type = 'button';
        button.addEventListener('click', async () => { button.disabled = true; try { await api(root.dataset.knowledgeReviewUrl, { id: item.id, approve }); await loadKnowledge(); } catch (e) { knowledgeMessage(e.message, true); button.disabled = false; } }); card.append(button);
      }
      return card;
    }
    if (item.scope !== 'System' && (item.scope === 'Personal' || item.status === 'PendingApproval' || knowledgeAdmin)) {
      const replace = el('button', 'ai-btn', T('Replace', 'استبدال')); replace.type = 'button';
      replace.addEventListener('click', () => { knowledgeReplaceId = item.id; knowledgeScope.value = item.scope; knowledgeTitle.value = item.title; knowledgeFile.click(); }); card.append(replace);
    }
    if (item.scope === 'Personal' || item.status === 'PendingApproval' || knowledgeAdmin) {
      const reindex = el('button', 'ai-btn', T('Re-index', 'إعادة الفهرسة')); reindex.type = 'button';
      reindex.addEventListener('click', async () => { reindex.disabled = true; try { await api(root.dataset.knowledgeReindexUrl + `?id=${encodeURIComponent(item.id)}`, {}); knowledgeMessage(T('Knowledge re-indexed.', 'أُعيدت فهرسة المعرفة.')); await loadKnowledge(); } catch (e) { knowledgeMessage(e.message, true); reindex.disabled = false; } }); card.append(reindex);
    }
    if (item.scope !== 'System' && (item.scope === 'Personal' || item.status === 'PendingApproval' || knowledgeAdmin)) {
      const remove = el('button', 'ai-btn', T('Delete', 'حذف')); remove.type = 'button';
      remove.addEventListener('click', async () => { if (!window.confirm(T('Delete this knowledge item?', 'حذف عنصر المعرفة هذا؟'))) return; remove.disabled = true; try { await api(root.dataset.knowledgeDeleteUrl + `?id=${encodeURIComponent(item.id)}`, {}); await loadKnowledge(); } catch (e) { knowledgeMessage(e.message, true); remove.disabled = false; } }); card.append(remove);
    }
    return card;
  }
  async function loadKnowledge() {
    knowledgeStatus.textContent = T('Loading…', 'جارٍ التحميل…');
    try {
      const data = await api(root.dataset.knowledgeUrl);
      knowledgeItems.replaceChildren(...(data.items || []).map(x => knowledgeCard(x)));
      const pending = data.pending || []; const pendingBox = knowledgePending.querySelector('div');
      knowledgePending.hidden = pending.length === 0; pendingBox.replaceChildren(...pending.map(x => knowledgeCard(x, true)));
      knowledgeStatus.textContent = data.items?.length ? '' : T('No saved knowledge yet.', 'لا توجد معرفة محفوظة بعد.');
    } catch (e) { knowledgeMessage(e.message, true); }
  }
  async function previewKnowledge() {
    const file = knowledgeFile.files?.[0]; if (!file) { knowledgeMessage(T('Choose a supported file first.', 'اختر ملفًا مدعومًا أولًا.'), true); return; }
    const button = $('[data-ai-knowledge-preview]'); button.disabled = true; knowledgeMessage(T('Extracting and reviewing the document…', 'جارٍ استخراج محتوى الملف ومعاينته…'));
    try {
      const form = new FormData(); form.append('file', file);
      activeKnowledgePreview = await api(root.dataset.knowledgePreviewUrl, form, true);
      knowledgeTitle.value ||= file.name.replace(/\.[^.]+$/, '');
      const p = activeKnowledgePreview; knowledgePreviewBox.replaceChildren(); knowledgePreviewBox.hidden = false;
      knowledgePreviewBox.append(el('strong', null, p.fileName), el('small', null, `${p.characterCount} ${T('characters', 'حرف')} · ${p.chunkCount} ${T('chunks', 'مقاطع')} · ${p.format.toUpperCase()}`));
      const summary = el('p', null, p.summary); knowledgePreviewBox.append(summary);
      knowledgePreviewBox.append(el('p', null, knowledgeScope.value === 'Shared' ? T('Saving shared knowledge submits it for Admin approval; it will not be used before approval.', 'حفظ المعرفة كمشتركة يرسلها لاعتماد المدير، ولن تُستخدم قبل الاعتماد.') : T('Personal knowledge is visible only to your account.', 'المعرفة الشخصية متاحة لحسابك فقط.')));
      const save = el('button', 'ai-btn ai-btn--primary', T('Save this reference', 'احفظ هذا المرجع')); save.type = 'button';
      save.addEventListener('click', async () => {
        save.disabled = true;
        try {
          const result = await api(root.dataset.knowledgeSaveUrl, { previewId: p.id, scope: knowledgeScope.value, title: knowledgeTitle.value, replaceId: knowledgeReplaceId });
          activeKnowledgePreview = null; knowledgeReplaceId = null; knowledgePreviewBox.hidden = true; knowledgeFile.value = '';
          knowledgeMessage(result.item.status === 'PendingApproval' ? T('Saved and sent for Admin approval.', 'حُفظ وأُرسل لاعتماد المدير.') : T('Saved and indexed privately.', 'حُفظ وفُهرس بشكل خاص.'));
          await loadKnowledge();
        } catch (e) { knowledgeMessage(e.message, true); save.disabled = false; }
      });
      const cancel = el('button', 'ai-btn', T('Cancel', 'إلغاء')); cancel.type = 'button'; cancel.addEventListener('click', () => { activeKnowledgePreview = null; knowledgePreviewBox.hidden = true; });
      knowledgePreviewBox.append(save, cancel); knowledgeMessage(T('Review the summary before saving.', 'راجع الملخص قبل الحفظ.'));
    } catch (e) { knowledgeMessage(e.message, true); }
    finally { button.disabled = false; }
  }
  knowledgeToggle?.addEventListener('click', async () => { const open = knowledgePanel.hidden; knowledgePanel.hidden = !open; knowledgeToggle.setAttribute('aria-expanded', String(open)); if (open) await loadKnowledge(); });
  $('[data-ai-knowledge-close]')?.addEventListener('click', () => { knowledgePanel.hidden = true; knowledgeToggle.setAttribute('aria-expanded', 'false'); });
  $('[data-ai-knowledge-preview]')?.addEventListener('click', previewKnowledge);
  knowledgeFile?.addEventListener('change', () => { if (knowledgeFile.files?.length && knowledgeReplaceId) previewKnowledge(); });
  $('[data-ai-knowledge-search]')?.addEventListener('click', async () => {
    const q = $('.ai-knowledge-search input').value.trim(); if (q.length < 2) { knowledgeMessage(T('Enter at least two characters.', 'اكتب حرفين على الأقل.'), true); return; }
    try {
      const data = await api(root.dataset.knowledgeSearchUrl + `?q=${encodeURIComponent(q)}`); knowledgeResults.replaceChildren();
      for (const source of data.sources || []) { const card = el('article', 'ai-knowledge-card'); card.append(el('strong', null, `${source.title} v${source.version} · ${T('chunk', 'مقطع')} ${source.chunk}`), el('p', null, source.text), el('small', null, `${source.scope} · ${Math.round(source.score * 100)}%`)); knowledgeResults.append(card); }
      knowledgeMessage((data.sources || []).length ? (data.semanticSearch ? T('Hybrid semantic and keyword search.', 'بحث دلالي وكلمات مفتاحية.') : T('Keyword search fallback; embedding provider unavailable.', 'بحث بالكلمات فقط؛ مزود التضمين غير متاح.')) : T('No matching authorized sources.', 'لا توجد مراجع مصرح بها مطابقة.'));
    } catch (e) { knowledgeMessage(e.message, true); }
  });

  // ---------- Prepared workflow action (Executive Agent): review, then explicit identity confirmation ----------
  const opNames = {
    request_weights: T('Request weight proposals', 'طلب مقترحات الأوزان'), approve_weights: T('Approve weights', 'اعتماد الأوزان'),
    sign_evaluation_report: T('Sign evaluation report', 'توقيع تقرير التقييم'), approve_manager_report: T('Approve report (Manager)', 'اعتماد التقرير (المدير)'),
    top_vote: T('Top Committee vote', 'تصويت اللجنة العليا'), forward_report: T('Forward report', 'تحويل التقرير'),
    forward_decision: T('Forward decision', 'تحويل القرار'), notify_suppliers: T('Notify suppliers', 'إبلاغ الموردين')
  };
  const b64d = s => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(s.length / 4) * 4, '=')), c => c.charCodeAt(0)).buffer;
  const b64e = buf => btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  async function agentPost(action, body) {
    const r = await fetch('/ExecutiveAgent/' + action, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(body) });
    let data = null; try { data = await r.json(); } catch { /* not JSON */ }
    if (!r.ok || !data) throw new Error((data && data.error) || T('Sign in again or check the server error.', 'سجّل الدخول مرة أخرى أو تحقق من الخطأ.'));
    return data;
  }
  function actionCard(a) {
    const id = a.actionId || a.id;
    const card = el('div', 'ai-action-card');
    card.append(el('strong', null, `${opNames[a.operation] || a.operation} · T-${String(a.tenderId).padStart(4, '0')}`));
    if (a.decision) card.append(el('small', null, T('Decision: ', 'القرار: ') + a.decision));
    if (a.comment) card.append(el('p', null, a.comment));
    const pdf = a.pdfUrl || (a.reportId ? `/ExecutiveAgent/Pdf/${a.reportId}` : null);
    if (pdf && pdf.startsWith('/ExecutiveAgent/Pdf/')) { const l = el('a', 'ai-link', T('Review the exact report PDF first', 'راجع ملف التقرير قبل التوقيع')); l.href = pdf; l.target = '_blank'; l.rel = 'noopener'; card.append(l); }
    const state = el('p', 'ai-action-state', a.state === 'Pending' ? T('Prepared — not executed. Expires: ', 'مُجهّز ولم يُنفّذ. ينتهي: ') + fmtTime(String(a.expiresAt)) : a.state);
    card.append(state);
    if (a.state !== 'Pending' || new Date(String(a.expiresAt).endsWith('Z') ? a.expiresAt : a.expiresAt + 'Z') < new Date()) return card;
    const label = el('label', 'ai-action-check'); const check = document.createElement('input'); check.type = 'checkbox';
    label.append(check, document.createTextNode(' ' + T('I reviewed this exact action and confirm it.', 'راجعت هذا الإجراء بالتحديد وأؤكده.')));
    card.append(label);
    const passkey = el('button', 'ai-btn ai-btn--primary', T('Confirm with device passkey', 'تأكيد بمفتاح المرور')); passkey.type = 'button'; passkey.disabled = true;
    check.addEventListener('change', () => { passkey.disabled = !check.checked; if (pwBtn) pwBtn.disabled = !check.checked; });
    const done = r => { state.textContent = r.state === 'Succeeded' ? T('✅ Executed', '✅ تم التنفيذ') + (r.duplicate ? T(' (already executed)', ' (نُفّذ سابقًا)') : '') : r.state; label.remove(); passkey.remove(); pwWrap?.remove(); speak(r.state === 'Succeeded' ? T('Done. The action was executed.', 'تم تنفيذ الإجراء.') : r.state); };
    passkey.addEventListener('click', async () => {
      passkey.disabled = true;
      try {
        const challenge = await agentPost('Challenge', { actionId: id });
        let options = challenge.options;
        if (PublicKeyCredential.parseRequestOptionsFromJSON) options = PublicKeyCredential.parseRequestOptionsFromJSON(options);
        else { options.challenge = b64d(options.challenge); options.allowCredentials = (options.allowCredentials || []).map(c => ({ ...c, id: b64d(c.id) })); }
        const cred = await navigator.credentials.get({ publicKey: options });
        if (!cred) throw new Error(T('No device identity proof returned.', 'لم يتم إرجاع إثبات هوية من الجهاز.'));
        const json = cred.toJSON ? cred.toJSON() : { id: cred.id, rawId: b64e(cred.rawId), type: cred.type, response: { clientDataJSON: b64e(cred.response.clientDataJSON), authenticatorData: b64e(cred.response.authenticatorData), signature: b64e(cred.response.signature), userHandle: cred.response.userHandle ? b64e(cred.response.userHandle) : null }, clientExtensionResults: cred.getClientExtensionResults() };
        done(await agentPost('Confirm', { actionId: id, confirmed: true, credentialJson: JSON.stringify(json) }));
      } catch (e) { state.textContent = e.message; passkey.disabled = !check.checked; }
    });
    card.append(passkey);
    let pwBtn = null, pwWrap = null;
    if (a.identity === 'password-or-passkey') {
      pwWrap = el('div', 'ai-action-pw');
      const pw = document.createElement('input'); pw.type = 'password'; pw.autocomplete = 'current-password'; pw.placeholder = T('Your account password', 'كلمة مرور حسابك');
      pw.setAttribute('aria-label', T('Your account password', 'كلمة مرور حسابك'));
      pwBtn = el('button', 'ai-btn', T('Confirm with password', 'تأكيد بكلمة المرور')); pwBtn.type = 'button'; pwBtn.disabled = true;
      pwBtn.addEventListener('click', async () => {
        pwBtn.disabled = true; const password = pw.value; pw.value = '';
        try { done(await agentPost('Confirm', { actionId: id, confirmed: true, password })); } catch (e) { state.textContent = e.message; pwBtn.disabled = !check.checked; }
      });
      pwWrap.append(pw, pwBtn); card.append(pwWrap);
    }
    return card;
  }

  function bubble(role, text, opts = {}) {
    welcome.hidden = true;
    const b = el('div', 'ai-msg ai-msg--' + role); const body = el('div', 'ai-msg-body');
    if (/[\u0600-\u06FF]/.test(text)) b.dir = 'rtl'; else if (ar) b.dir = 'ltr';
    if (role === 'assistant') {
      const reviewed = opts.source === 'reviewed-guide' || text.startsWith('Verified system guide:') || text.startsWith('دليل النظام المعتمد:');
      const label = reviewed ? T('Verified system guide', 'دليل النظام المعتمد') : opts.kind === 'data' || opts.kind === 'file' && opts.source === 'data' ? T('Live system data', 'بيانات النظام المباشرة')
        : opts.kind === 'navigate' || opts.kind === 'agent-action' || String(opts.kind).startsWith('agent-') || String(opts.kind).startsWith('tender-') ? T('System action', 'إجراء النظام')
        : opts.kind === 'clarify' ? T('Clarification', 'توضيح') : opts.source === 'document' || opts.kind === 'document-analysis' ? T('Document analysis (AI)', 'تحليل مستند (ذكاء اصطناعي)') : opts.kind === 'welcome' ? 'Orange AI' : T('AI explanation', 'شرح بالذكاء الاصطناعي');
      b.append(el('small', 'ai-source', label));
    }
    render(body, text); b.append(body);
    if (opts.file) b.append(fileCard(opts.file));
    (opts.actions || []).forEach(a => b.append(actionCard(a)));
    if (opts.kind === 'agent-confirm' && opts.pendingAction) {
      const bar = el('div', 'ai-confirm-bar');
      const yes = el('button', 'ai-btn ai-btn--primary', T('Confirm and execute', 'تأكيد وتنفيذ')); yes.type = 'button';
      const no = el('button', 'ai-btn', T('Cancel', 'إلغاء')); no.type = 'button';
      yes.addEventListener('click', () => { bar.remove(); ask(T('confirm', 'أكّد'), false, false, opts.pendingAction); });
      no.addEventListener('click', () => { bar.remove(); ask(T('cancel', 'إلغاء'), false, false, opts.pendingAction); });
      bar.append(yes, no); b.append(bar);
    }
    box.append(b); requestAnimationFrame(() => { box.scrollTop = box.scrollHeight; }); return b;
  }
  function typing(label) {
    const b = el('div', 'ai-msg ai-msg--assistant ai-typing'); b.setAttribute('role', 'status'); b.setAttribute('aria-label', label || T('Orange AI is working', 'Orange AI يعمل'));
    b.append(el('i'), el('i'), el('i')); if (label) b.append(el('span', 'ai-typing-label', label)); box.append(b); box.scrollTop = box.scrollHeight; return b;
  }
  function errorRow(message, retry = null) {
    const row = el('div', 'ai-error'); row.setAttribute('role', 'alert');
    row.append(el('span', null, message));
    if (retry) { const b = el('button', null, T('Retry', 'إعادة المحاولة')); b.type = 'button'; b.addEventListener('click', () => { if (busy) return; row.remove(); retry(); }); row.append(b); }
    box.append(row); box.scrollTop = box.scrollHeight;
  }
  function reset() {
    if (busy) return; conversationId = null; loaded = true; saveState();
    box.querySelectorAll('.ai-msg,.ai-error').forEach(n => n.remove()); welcome.hidden = false; hist.hidden = true;
  }
  const lockables = '[data-ai-new],[data-ai-history],[data-ai-attach],.ai-hist-open,.ai-hist-del,.ai-suggest button,.ai-actions button';
  function setBusy(on) { busy = on; send.disabled = on; input.readOnly = on; root.querySelectorAll(lockables).forEach(b => { b.disabled = on; }); root.classList.toggle('is-busy', on); }

  function looksLikeReport(text) { return /excel|xlsx|pdf|word|docx|power ?point|pptx|slides|presentation|export|report|اكسل|إكسل|تقرير|تصدير|صدّر|صدر|وورد|بوربوينت|عرض تقديمي|شرائح|ملف/i.test(text); }
  async function ask(text, isRetry, spoken = false, actionId = null) {
    text = (text || '').trim();
    if (busy || !text) return;
    setBusy(true); hist.hidden = true;
    if (!isRetry) bubble('user', spoken ? '🎙️ ' + text : text);
    const wait = typing(looksLikeReport(text) ? T('Preparing your report…', 'جارٍ تجهيز التقرير…') : null);
    let reply = null;
    try {
      reply = await api(root.dataset.sendUrl, actionId ? { conversationId, message: text, page: root.dataset.page, actionId } : { conversationId, message: text, page: root.dataset.page });
      conversationId = reply.conversationId; saveState(); wait.remove();
      let file = null, actions = null, pendingAction = null; if (reply.payload) { try { const p = JSON.parse(reply.payload); file = p.file; actions = p.actions; pendingAction = p.state === 'Confirming' ? p.actionId : null; } catch { /* ignore */ } }
      bubble('assistant', reply.reply, { source: reply.source, kind: reply.kind, file, actions, pendingAction });
    } catch (e) {
      if (Number.isInteger(e.conversationId)) { conversationId = e.conversationId; saveState(); } wait.remove(); errorRow(e.message, () => ask(text, true, spoken));
      if (spoken || voiceMode) await speak(e.message);
    }
    finally { setBusy(false); if (!spoken) input.focus(); }
    if (reply) await afterReply(reply, spoken);
  }

  // ---------- Voice: speech in (local Whisper or on-device browser recognition), speech out (configurable TTS) ----------
  const micBtn = $('[data-ai-mic]'), modeBtn = $('[data-ai-voice-mode]'), stopBtn = $('[data-ai-stop-voice]'), langBtn = $('[data-ai-voice-lang]'), voiceStatus = $('.ai-voice-status');
  let listening = false, recognizer = null, recorder = null, micStream = null, capability, silenceTimer = null, maxTimer = null;
  const ttsEnabled = root.dataset.tts !== 'off' && 'speechSynthesis' in window;
  function status(msg, bad) { voiceStatus.hidden = !msg; voiceStatus.textContent = msg || ''; voiceStatus.classList.toggle('is-bad', !!bad); }
  const isSpeaking = () => ttsEnabled && (speechSynthesis.speaking || speechSynthesis.pending);
  function pickVoice(lang) {
    const voices = speechSynthesis.getVoices();
    return voices.find(v => v.lang?.toLowerCase().startsWith(lang === 'ar' ? 'ar-jo' : 'en-gb')) || voices.find(v => v.lang?.toLowerCase().startsWith(lang)) || null;
  }
  // Speaks a short reply. The microphone is never open while speaking, so playback cannot be transcribed as a command.
  function speak(text) {
    return new Promise(done => {
      if (!ttsEnabled || !text) { done(); return; }
      stopListening(true);
      const lang = /[؀-ۿ]/.test(text) ? 'ar' : 'en';
      const voice = pickVoice(lang);
      if (!voice) { status(lang === 'ar' ? T('No Arabic voice is installed in this browser/OS; the reply is shown as text.', 'لا يوجد صوت عربي مثبت في المتصفح/النظام؛ الرد معروض كنص.') : T('No English voice is installed; the reply is shown as text.', 'لا يوجد صوت إنجليزي مثبت؛ الرد معروض كنص.'), true); done(); return; }
      const u = new SpeechSynthesisUtterance(text.replace(/[*_#`|]/g, ''));
      u.voice = voice; u.lang = voice.lang; u.rate = 1;
      const finish = () => { stopBtn.hidden = true; root.classList.remove('is-speaking'); done(); };
      u.onend = finish; u.onerror = finish;
      stopBtn.hidden = false; root.classList.add('is-speaking');
      speechSynthesis.cancel(); speechSynthesis.speak(u);
    });
  }
  function stopSpeaking() { if (ttsEnabled) speechSynthesis.cancel(); stopBtn.hidden = true; root.classList.remove('is-speaking'); }

  async function afterReply(reply, spoken) {
    const url = reply.navigate;
    const safe = typeof url === 'string' && internalLink.test(url.split(/[?#]/)[0]);
    if (spoken || voiceMode) await speak(reply.speech);
    if (safe) {
      store.set(STATE_KEY, { conversationId, open: true, expanded: root.classList.contains('is-expanded'), voiceMode, voiceLang });
      setTimeout(() => location.assign(new URL(url, location.origin).href), spoken || voiceMode ? 150 : 1200);
      return;
    }
    if (voiceMode && !busy) listen();
  }

  async function detectCapability() {
    if (capability !== undefined) return capability;
    capability = null;
    try {
      const r = await fetch(root.dataset.speechStatusUrl, { credentials: 'same-origin', cache: 'no-store', redirect: 'manual' });
      if (r.ok && (await r.json()).available) { capability = 'server'; return capability; }
    } catch { /* fall through to the browser */ }
    const R = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (R?.available) {
      try { if (await Promise.race([R.available({ langs: [voiceLang === 'ar' ? 'ar-JO' : 'en-US'], processLocally: true }), new Promise(d => setTimeout(() => d('timeout'), 3000))]) === 'available') capability = 'browser'; }
      catch { /* unavailable */ }
    }
    return capability;
  }
  function setListening(on) { listening = on; micBtn.setAttribute('aria-pressed', String(on)); root.classList.toggle('is-listening', on); }
  function stopListening(discard) {
    clearTimeout(silenceTimer); clearTimeout(maxTimer);
    if (recognizer) { try { discard ? recognizer.abort() : recognizer.stop(); } catch { /* ended */ } recognizer = null; }
    if (recorder && recorder.state === 'recording') { recorder.discard = !!discard; recorder.stop(); }
    if (discard) { micStream?.getTracks().forEach(t => t.stop()); micStream = null; }
    setListening(false);
  }
  async function listen() {
    if (busy || listening) return;
    if (isSpeaking()) stopSpeaking();
    if (!window.isSecureContext) { status(T('The microphone needs HTTPS.', 'الميكروفون يحتاج HTTPS.'), true); return; }
    const cap = await detectCapability();
    if (!cap) { status(T('Local speech recognition is not available on this server or browser. Type your message instead (no cloud speech service is used).', 'التعرّف المحلي على الكلام غير متاح على هذا الخادم أو المتصفح. اكتب رسالتك بدلًا من ذلك (لا نستخدم خدمة كلام سحابية).'), true); voiceMode = false; syncMode(); return; }
    if (cap === 'browser') {
      const R = window.SpeechRecognition || window.webkitSpeechRecognition;
      recognizer = new R(); recognizer.processLocally = true; recognizer.lang = voiceLang === 'ar' ? 'ar-JO' : 'en-US'; recognizer.interimResults = false; recognizer.continuous = false;
      recognizer.onresult = e => { const text = e.results[0][0].transcript; stopListening(false); status(''); ask(text, false, true); };
      recognizer.onerror = e => { setListening(false); if (e.error !== 'aborted' && e.error !== 'no-speech') status(T('Speech recognition failed: ', 'فشل التعرّف على الكلام: ') + e.error, true); };
      recognizer.onend = () => setListening(false);
      try { recognizer.start(); setListening(true); status(T('Listening… speak now.', 'سامعك… احكي هلأ.')); } catch (e) { status(e.message, true); setListening(false); }
      return;
    }
    try {
      micStream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
      const chunks = []; recorder = new MediaRecorder(micStream);
      recorder.ondataavailable = e => { if (e.data.size) chunks.push(e.data); };
      recorder.onstop = async () => {
        const rec = recorder; micStream?.getTracks().forEach(t => t.stop()); micStream = null; setListening(false);
        if (rec?.discard || !chunks.length) return;
        status(T('Transcribing locally…', 'جارٍ تحويل الكلام إلى نص محليًا…'));
        const body = new FormData(); body.append('audio', new Blob(chunks, { type: rec.mimeType }), 'speech.webm'); body.append('language', voiceLang);
        try {
          const data = await api(root.dataset.transcribeUrl, body, true);
          status(''); if (data.text?.trim()) await ask(data.text, false, true);
        } catch (e) { status(e.message, true); if (voiceMode) { voiceMode = false; syncMode(); } }
      };
      recorder.start(); setListening(true); status(T('Listening… I will stop when you pause. Press the mic again to send.', 'سامعك… رح أوقف لما تسكت. اضغط المايك مرة ثانية للإرسال.'));
      watchSilence(micStream); maxTimer = setTimeout(() => stopListening(false), 30000);
    } catch (e) { stopListening(true); status(T('Microphone unavailable: ', 'الميكروفون غير متاح: ') + e.message, true); }
  }
  // Ends the recording after ~1.4 s of silence that follows speech.
  function watchSilence(stream) {
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)(); const src = ctx.createMediaStreamSource(stream); const an = ctx.createAnalyser(); an.fftSize = 1024; src.connect(an);
      const buf = new Uint8Array(an.fftSize); let heard = false, quietSince = performance.now();
      const tick = () => {
        if (!listening || !recorder || recorder.state !== 'recording') { ctx.close(); return; }
        an.getByteTimeDomainData(buf); let peak = 0; for (const v of buf) peak = Math.max(peak, Math.abs(v - 128));
        if (peak > 12) { heard = true; quietSince = performance.now(); }
        else if (heard && performance.now() - quietSince > 1400) { ctx.close(); stopListening(false); return; }
        requestAnimationFrame(tick);
      };
      tick();
    } catch { /* manual stop still works */ }
  }
  function syncMode() { modeBtn.setAttribute('aria-pressed', String(voiceMode)); root.classList.toggle('is-voice-mode', voiceMode); saveState(); }
  micBtn.addEventListener('click', () => { if (isSpeaking()) { stopSpeaking(); return; } if (listening) stopListening(false); else listen(); });
  modeBtn.addEventListener('click', () => {
    voiceMode = !voiceMode; syncMode();
    if (voiceMode) { status(T('Voice conversation on: I will listen again after each spoken reply.', 'المحادثة الصوتية مفعّلة: رح أسمعك مرة ثانية بعد كل رد.')); listen(); }
    else { stopListening(true); stopSpeaking(); status(''); }
  });
  stopBtn.addEventListener('click', () => { stopSpeaking(); if (voiceMode) { voiceMode = false; syncMode(); } stopListening(true); status(T('Voice stopped.', 'تم إيقاف الصوت.')); });
  langBtn.addEventListener('click', () => { voiceLang = voiceLang === 'ar' ? 'en' : 'ar'; langBtn.textContent = voiceLang === 'ar' ? 'ع' : 'EN'; capability = undefined; saveState(); });

  async function upload(file) {
    if (!file || busy) return;
    const ext = (file.name.split('.').pop() || '').toLowerCase();
    if (!['pdf', 'xlsx', 'docx', 'csv', 'txt'].includes(ext)) { errorRow(T('Supported files: PDF, Excel (.xlsx), Word (.docx), CSV and TXT.', 'الملفات المدعومة: PDF وExcel (.xlsx) وWord (.docx) وCSV وTXT.')); return; }
    if (file.size > 10 * 1024 * 1024) { errorRow(T('The file is too large (max 10 MB).', 'الملف كبير جدًا (الحد 10 ميغابايت).')); return; }
    setBusy(true); hist.hidden = true;
    const wait = typing(T('Reading ', 'جارٍ قراءة ') + file.name + '…');
    const data = new FormData(); data.append('file', file); if (conversationId) data.append('conversationId', conversationId);
    try {
      const reply = await api(root.dataset.uploadUrl, data, true);
      conversationId = reply.conversationId; saveState(); wait.remove();
      let card = null; try { card = JSON.parse(reply.payload).file; } catch { /* ignore */ }
      bubble('user', '📎 ' + file.name, { file: card });
      bubble('assistant', reply.reply, { kind: 'text', source: 'upload' });
    } catch (e) { if (Number.isInteger(e.conversationId)) { conversationId = e.conversationId; saveState(); } wait.remove(); errorRow(e.message); }
    finally { setBusy(false); fileInput.value = ''; input.focus(); }
  }

  async function loadHistory() {
    if (busy) return;
    hist.hidden = !hist.hidden; if (hist.hidden) return;
    histList.replaceChildren();
    try {
      const { items } = await api(root.dataset.historyUrl);
      histEmpty.hidden = items.length > 0;
      items.forEach(c => {
        const li = el('li'); const open = el('button', 'ai-hist-open'); open.type = 'button';
        open.append(el('strong', null, c.title), el('small', null, fmtTime(c.UpdatedAt || c.updatedAt)));
        open.addEventListener('click', () => openConversation(c.id));
        const del = el('button', 'ai-hist-del', '×'); del.type = 'button'; del.setAttribute('aria-label', T('Delete conversation', 'حذف المحادثة'));
        del.addEventListener('click', async () => { if (!confirm(T('Delete this conversation and its files?', 'حذف هذه المحادثة وملفاتها؟'))) return; try { await api(root.dataset.deleteUrl + '?id=' + c.id, {}); if (conversationId === c.id) reset(); li.remove(); } catch (e) { errorRow(e.message); } });
        li.append(open, del); histList.append(li);
      });
    } catch (e) { errorRow(e.message); }
  }
  async function openConversation(id, quiet) {
    if (busy) return;
    try {
      const { items } = await api(root.dataset.messagesUrl + '?id=' + id);
      box.querySelectorAll('.ai-msg,.ai-error').forEach(n => n.remove());
      conversationId = id; hist.hidden = true; loaded = true; saveState();
      if (!items.length) { welcome.hidden = false; return; }
      items.forEach(m => {
        let file = null, actions = null, pendingAction = null; if (m.payload) { try { const p = JSON.parse(m.payload); file = p.file; actions = p.actions; pendingAction = p.state === 'Confirming' && m === items[items.length - 1] ? p.actionId : null; } catch { /* ignore */ } }
        bubble(m.role, m.content, { kind: m.kind, file, actions, pendingAction, source: m.kind === 'data' || m.kind === 'clarify' ? 'data' : null });
      });
    } catch (e) { if (quiet) { conversationId = null; saveState(); welcome.hidden = false; } else errorRow(e.message); }
  }

  // ---------- Welcome (once per login) and quick actions ----------
  function renderActions(w) {
    if (!w) return;
    actionsBar.replaceChildren();
    (w.actions || []).forEach(a => {
      const b = el('button', 'ai-chip ai-chip--' + a.icon, a.label); b.type = 'button';
      b.addEventListener('click', () => { if (a.upload) fileInput.click(); else if (a.fill) { input.value = a.prompt; input.focus(); input.setSelectionRange(input.value.length, input.value.length); } else ask(a.prompt); });
      actionsBar.append(b);
    });
    if (w.suggestions?.length) { suggest.replaceChildren(); w.suggestions.forEach(q => { const b = el('button', null, q); b.type = 'button'; b.addEventListener('click', () => ask(q)); suggest.append(b); }); }
  }
  function showGreeting(w) {
    const body = $('.ai-greeting-body'); render(body, w.message);
    greeting.hidden = false; requestAnimationFrame(() => greeting.classList.add('is-in'));
    dot.hidden = false;
  }
  function hideGreeting() { greeting.classList.remove('is-in'); greeting.hidden = true; }
  async function loadWelcome() {
    try {
      const data = await api(root.dataset.welcomeUrl);
      welcomeData = data.welcome; renderActions(welcomeData);
      // The personalized welcome heads the empty chat, with the quick actions and suggestions under it (FAB or greeting card).
      const head = $('.ai-welcome-text'); head.classList.add('ai-welcome-msg'); render(head, welcomeData.message);
      const seen = store.get(WELCOME_KEY, localStorage);
      if (data.loginId && seen !== data.loginId) {
        store.set(WELCOME_KEY, data.loginId, localStorage);
        if (panel.hidden) setTimeout(() => showGreeting(welcomeData), 700);
      }
    } catch { /* the chat still works without the welcome */ }
  }

  // ---------- Open / close / expand ----------
  const setOpen = (open, focus = true) => {
    panel.hidden = !open; fab.setAttribute('aria-expanded', String(open)); root.classList.toggle('is-open', open);
    if (open) { hideGreeting(); dot.hidden = true; if (!loaded && conversationId) openConversation(conversationId, true); loaded = true; if (focus) input.focus(); }
    saveState();
  };
  const setExpanded = on => { root.classList.toggle('is-expanded', on); $('[data-ai-expand]').setAttribute('aria-pressed', String(on)); saveState(); };
  fab.addEventListener('click', () => setOpen(panel.hidden));
  $('[data-ai-close]').addEventListener('click', () => { setOpen(false); fab.focus(); });
  $('[data-ai-expand]').addEventListener('click', () => setExpanded(!root.classList.contains('is-expanded')));
  $('[data-ai-new]').addEventListener('click', reset);
  $('[data-ai-history]').addEventListener('click', loadHistory);
  $('[data-ai-attach]').addEventListener('click', () => { if (!busy) fileInput.click(); });
  fileInput.addEventListener('change', () => upload(fileInput.files[0]));
  root.querySelectorAll('[data-ai-greeting-dismiss]').forEach(b => b.addEventListener('click', () => { hideGreeting(); dot.hidden = true; }));
  $('[data-ai-greeting-open]').addEventListener('click', () => setOpen(true));
  document.addEventListener('keydown', e => { if (e.key === 'Escape') { if (!greeting.hidden) hideGreeting(); else if (!panel.hidden) { setOpen(false); fab.focus(); } } });
  form.addEventListener('submit', e => { e.preventDefault(); if (busy) return; const t = input.value; input.value = ''; input.style.height = ''; ask(t); });
  input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); form.requestSubmit(); } });
  input.addEventListener('input', () => { input.style.height = 'auto'; input.style.height = Math.min(input.scrollHeight, 140) + 'px'; });
  suggest.querySelectorAll('button').forEach(b => b.addEventListener('click', () => ask(b.textContent)));
  // Drag and drop a document onto the open panel.
  panel.addEventListener('dragover', e => { if ([...(e.dataTransfer?.types || [])].includes('Files')) { e.preventDefault(); drop.hidden = false; } });
  panel.addEventListener('dragleave', e => { if (!panel.contains(e.relatedTarget)) drop.hidden = true; });
  panel.addEventListener('drop', e => { e.preventDefault(); drop.hidden = true; if (e.dataTransfer.files[0]) upload(e.dataTransfer.files[0]); });

  // Restore the chat after navigating between pages (same tab).
  const saved = store.get(STATE_KEY);
  if (saved) {
    conversationId = Number.isInteger(saved.conversationId) ? saved.conversationId : null;
    if (saved.expanded) setExpanded(true);
    if (saved.open) setOpen(true, false);
    if (saved.voiceLang === 'ar' || saved.voiceLang === 'en') { voiceLang = saved.voiceLang; langBtn.textContent = voiceLang === 'ar' ? 'ع' : 'EN'; }
    // Continue a voice conversation after a voice navigation, only when microphone access was already granted (no surprise prompt).
    if (saved.voiceMode && saved.open) { voiceMode = true; syncMode(); navigator.permissions?.query({ name: 'microphone' }).then(p => { if (p.state === 'granted') setTimeout(listen, 600); else { voiceMode = false; syncMode(); } }).catch(() => { voiceMode = false; syncMode(); }); }
  }
  loadWelcome();
  // The workspace top-bar voice-command mic (executive-agent.js) hands spoken commands to this one assistant.
  window.OrangeAI = Object.freeze({
    open: () => setOpen(true),
    voice: lang => {
      if (lang) { voiceLang = String(lang).toLowerCase().startsWith('ar') ? 'ar' : 'en'; langBtn.textContent = voiceLang === 'ar' ? 'ع' : 'EN'; capability = undefined; }
      setOpen(true, false);
      if (listening) stopListening(false); else listen();
    }
  });
})();
