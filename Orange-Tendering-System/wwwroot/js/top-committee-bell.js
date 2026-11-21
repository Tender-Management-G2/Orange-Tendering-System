"use strict";
// Top Committee secure notification bell (Views/Shared/_TopCommitteeBell.cshtml). Officer bell markup, Top Committee feed:
// the preview never carries links; a notification opens only through the server's Open endpoint, which re-authorizes it.
(() => {
  const root = document.querySelector('[data-tc-bell]');
  if (!root) return;
  const button = root.querySelector('.ws-bell-button');
  const badge = root.querySelector('.ws-bell-badge');
  const panel = root.querySelector('.ws-bell-panel');
  const list = root.querySelector('.ws-bell-list');
  const empty = root.querySelector('.ws-bell-empty');
  const error = root.querySelector('[data-tc-bell-error]');
  const count = root.querySelector('[data-tc-bell-count]');
  const text = root.dataset;
  const locale = text.lang === 'ar' ? 'ar' : undefined;
  let loading = false;

  function setUnread(n) {
    badge.hidden = n < 1;
    badge.textContent = n > 99 ? '99+' : String(n);
    const unread = text.textUnread.replace('{0}', n);
    button.setAttribute('aria-label', n ? `${text.textLabel}, ${unread}` : text.textLabel);
    count.textContent = n ? unread : text.textCaughtUp;
  }

  function row(item) {
    const li = document.createElement('li');
    li.className = 'ws-bell-item' + (item.isRead ? '' : ' is-unread');
    const open = document.createElement(item.canOpen ? 'button' : 'div');
    open.className = 'ws-bell-open';
    if (item.canOpen) { open.type = 'button'; open.dataset.notificationId = item.id; }
    const dot = document.createElement('i'); dot.setAttribute('aria-hidden', 'true');
    const body = document.createElement('span');
    const title = document.createElement('strong'); title.textContent = item.title || item.message || text.textLabel;
    const message = document.createElement('small'); message.textContent = item.title ? (item.message || '') : '';
    const time = document.createElement('time'); time.dateTime = item.createdAt;
    const date = new Date(item.createdAt);
    const state = item.isRead ? text.textRead : text.textNew;
    time.textContent = (Number.isNaN(date.valueOf()) ? '' : new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date) + ' · ') + state;
    body.append(title, message, time); open.append(dot, body); li.append(open);
    return li;
  }

  async function load() {
    if (loading) return;
    loading = true;
    try {
      const response = await fetch(text.previewUrl, { credentials: 'same-origin', headers: { Accept: 'application/json' }, cache: 'no-store' });
      if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('feed');
      const data = await response.json();
      setUnread(data.unreadCount || 0);
      list.replaceChildren(...(data.items || []).map(row));
      empty.hidden = (data.items || []).length > 0;
      error.hidden = true;
    } catch {
      error.textContent = text.textError; error.hidden = false; empty.hidden = true;
    } finally { loading = false; }
  }

  function setOpen(open) { panel.hidden = !open; button.setAttribute('aria-expanded', String(open)); if (open) load(); }
  button.addEventListener('click', ev => { ev.stopPropagation(); setOpen(panel.hidden); });
  document.addEventListener('click', ev => { if (!panel.hidden && !root.contains(ev.target)) setOpen(false); });
  document.addEventListener('keydown', ev => { if (ev.key === 'Escape' && !panel.hidden) { setOpen(false); button.focus(); } });

  list.addEventListener('click', async ev => {
    const open = ev.target.closest('[data-notification-id]');
    if (!open) return;
    open.disabled = true;
    try {
      const body = new URLSearchParams({ id: open.dataset.notificationId, __RequestVerificationToken: text.csrf });
      const response = await fetch(text.openUrl, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' }, body, cache: 'no-store' });
      if (!response.ok) throw new Error('open');
      const result = await response.json();
      if (Number.isInteger(result.unreadCount)) setUnread(result.unreadCount);
      open.closest('.ws-bell-item')?.classList.remove('is-unread');
      if (result.url) location.assign(result.url); else await load();
    } catch { open.disabled = false; }
  });

  load();
})();
