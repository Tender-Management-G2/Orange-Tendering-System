"use strict";
// Floating AI Chat Assistant: loading state, error + retry, per-user history (SQL Server) and plain-text rendering (no HTML injection).
(() => {
  const root = document.querySelector('[data-ai-chat]');
  if (!root) return;
  const $ = s => root.querySelector(s);
  const fab = $('.ai-fab'), panel = $('.ai-panel'), box = $('.ai-messages'), form = $('.ai-form'), input = $('textarea');
  const send = $('.ai-send'), hist = $('.ai-history'), histList = $('.ai-history ul'), histEmpty = $('.ai-history-empty');
  const csrf = root.dataset.csrf;
  const welcome = $('.ai-welcome');
  let conversationId = null, busy = false, lastQuestion = '';

  const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  async function api(url, body) {
    const res = await fetch(url, body === undefined ? { credentials: 'same-origin', headers: { Accept: 'application/json' } }
      : { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(body) });
    let data = null; try { data = await res.json(); } catch { /* no body */ }
    if (!res.ok) throw new Error((data && data.error) || 'Request failed (' + res.status + ').');
    return data;
  }
  // **bold** becomes <strong>; everything stays text nodes (no HTML is ever parsed).
  function inline(node, text) {
    text.split(/(\*\*[^*]+\*\*)/).forEach(t => node.append(/^\*\*[^*]+\*\*$/.test(t) ? el('strong', null, t.slice(2, -2)) : document.createTextNode(t)));
  }
  // Text only: code fences become <pre>, everything else is paragraphs of text nodes.
  function render(container, text) {
    container.replaceChildren();
    text.split(/```/).forEach((part, i) => {
      if (i % 2) container.append(el('pre', null, part.replace(/^\w*\n/, '').trim()));
      else part.split(/\n{2,}/).filter(p => p.trim()).forEach(p => container.append(el('p', null, p.trim())));
    });
  }
  function bubble(role, text) {
    welcome.hidden = true;
    const b = el('div', 'ai-msg ai-msg--' + role); const body = el('div', 'ai-msg-body');
    render(body, text); b.append(body); box.append(b); box.scrollTop = box.scrollHeight; return b;
  }
  function typing() {
    const b = el('div', 'ai-msg ai-msg--assistant ai-typing'); b.setAttribute('aria-label', 'The assistant is typing');
    b.append(el('i'), el('i'), el('i')); box.append(b); box.scrollTop = box.scrollHeight; return b;
  }
  function errorRow(message) {
    const row = el('div', 'ai-error'); row.setAttribute('role', 'alert');
    row.append(el('span', null, message));
    const retry = el('button', null, 'Retry'); retry.type = 'button';
    retry.addEventListener('click', () => { row.remove(); ask(lastQuestion, true); });
    row.append(retry); box.append(row); box.scrollTop = box.scrollHeight;
  }
  function reset() { conversationId = null; box.querySelectorAll('.ai-msg,.ai-error').forEach(n => n.remove()); welcome.hidden = false; hist.hidden = true; }

  async function ask(text, isRetry) {
    if (busy || !text.trim()) return;
    busy = true; send.disabled = true; lastQuestion = text;
    if (!isRetry) bubble('user', text);
    const wait = typing();
    try {
      const reply = await api('/AiChat/Send', { conversationId, message: text });
      conversationId = reply.conversationId; wait.remove(); bubble('assistant', reply.reply);
    } catch (e) { wait.remove(); errorRow(e.message); }
    finally { busy = false; send.disabled = false; input.focus(); }
  }

  async function loadHistory() {
    hist.hidden = !hist.hidden; if (hist.hidden) return;
    histList.replaceChildren();
    try {
      const { items } = await api('/AiChat/Conversations');
      histEmpty.hidden = items.length > 0;
      items.forEach(c => {
        const li = el('li'); const open = el('button', 'ai-hist-open'); open.type = 'button';
        open.append(el('strong', null, c.title), el('small', null, new Date(c.UpdatedAt || c.updatedAt).toLocaleString()));
        open.addEventListener('click', () => openConversation(c.id));
        const del = el('button', 'ai-hist-del', '×'); del.type = 'button'; del.setAttribute('aria-label', 'Delete conversation');
        del.addEventListener('click', async () => { try { await api('/AiChat/Delete?id=' + c.id, {}); if (conversationId === c.id) reset(); li.remove(); } catch (e) { errorRow(e.message); } });
        li.append(open, del); histList.append(li);
      });
    } catch (e) { errorRow(e.message); }
  }
  async function openConversation(id) {
    try {
      const { items } = await api('/AiChat/Messages?id=' + id);
      reset(); conversationId = id; hist.hidden = true;
      items.forEach(m => bubble(m.role, m.content));
    } catch (e) { errorRow(e.message); }
  }

  const setOpen = open => { panel.hidden = !open; fab.setAttribute('aria-expanded', String(open)); root.classList.toggle('is-open', open); if (open) input.focus(); };
  fab.addEventListener('click', () => setOpen(panel.hidden));
  $('[data-ai-close]').addEventListener('click', () => { setOpen(false); fab.focus(); });
  $('[data-ai-new]').addEventListener('click', reset);
  $('[data-ai-history]').addEventListener('click', loadHistory);
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !panel.hidden) { setOpen(false); fab.focus(); } });
  form.addEventListener('submit', e => { e.preventDefault(); const t = input.value; input.value = ''; input.style.height = ''; ask(t); });
  input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); } });
  input.addEventListener('input', () => { input.style.height = 'auto'; input.style.height = Math.min(input.scrollHeight, 120) + 'px'; });
  root.querySelectorAll('.ai-suggest button').forEach(b => b.addEventListener('click', () => ask(b.textContent)));
})();
