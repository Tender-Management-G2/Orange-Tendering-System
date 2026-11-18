"use strict";
// Live notification bell: loads the user's own notifications from SQL Server, shows the unread count, and receives
// new ones in real time over SignalR (falls back to polling if the hub is unreachable).
(() => {
  const root = document.querySelector('[data-notification-bell]');
  if (!root) return;
  const button = root.querySelector('.ws-bell-button');
  const badge = root.querySelector('.ws-bell-badge');
  const panel = root.querySelector('.ws-bell-panel');
  const list = root.querySelector('.ws-bell-list');
  const empty = root.querySelector('.ws-bell-empty');
  const toasts = root.querySelector('.ws-toast-region');
  const csrf = root.dataset.csrf;
  let unread = 0, loading = false;
  const seen = new Set();

  const ago = iso => {
    const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
    if (s < 60) return 'just now';
    if (s < 3600) return Math.floor(s / 60) + ' min ago';
    if (s < 86400) return Math.floor(s / 3600) + ' h ago';
    return new Date(iso).toLocaleDateString();
  };
  const setUnread = n => {
    unread = Math.max(0, n);
    badge.textContent = unread > 99 ? '99+' : String(unread);
    badge.hidden = unread === 0;
    button.setAttribute('aria-label', unread ? `Notifications, ${unread} unread` : 'Notifications');
  };
  const post = url => fetch(url, { method: 'POST', credentials: 'same-origin', keepalive: true, headers: { RequestVerificationToken: csrf } });

  function row(n) {
    const li = document.createElement('li');
    li.className = 'ws-bell-item' + (n.isRead ? '' : ' is-unread');
    li.dataset.id = n.id;
    const open = document.createElement(n.link ? 'a' : 'div');
    if (n.link) open.href = n.link;
    open.className = 'ws-bell-open';
    const dot = document.createElement('i'); dot.setAttribute('aria-hidden', 'true');
    const text = document.createElement('span');
    const t = document.createElement('strong'); t.textContent = n.title;
    const m = document.createElement('small'); m.textContent = n.message;
    const a = document.createElement('time'); a.dateTime = n.createdAt; a.textContent = ago(n.createdAt);
    text.append(t, m, a); open.append(dot, text); li.append(open);
    open.addEventListener('click', async ev => {
      if (n.link && !ev.ctrlKey && !ev.metaKey && !ev.shiftKey && ev.button === 0) ev.preventDefault();
      const navigate = n.link && ev.defaultPrevented;
      if (!n.isRead) {
        try {
          const r = await post(root.dataset.readUrl + '?id=' + n.id);
          if (r.ok) { n.isRead = true; li.classList.remove('is-unread'); setUnread((await r.json()).unread); }
        } catch { /* The related page remains accessible; polling reconciles the unread state. */ }
      }
      if (navigate) window.location.assign(n.link);
    });
    return li;
  }
  function render(items) {
    list.replaceChildren(...items.map(row));
    empty.hidden = items.length > 0;
  }
  async function load() {
    if (loading) return;
    loading = true;
    try {
      const r = await fetch(root.dataset.feedUrl + '?take=12', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (!r.ok || !r.headers.get('content-type')?.includes('application/json')) throw new Error('Notification feed unavailable');
      const data = await r.json();
      setUnread(data.unread); render(data.items);
      data.items.forEach(n => seen.add(n.id));
      root.dataset.state = 'ready';
      root.querySelector('[data-bell-error]').hidden = true;
    } catch { root.dataset.state = 'offline'; root.querySelector('[data-bell-error]').hidden = false; }
    finally { loading = false; }
  }
  function toast(n) {
    const el = document.createElement(n.link ? 'a' : 'div');
    el.className = 'ws-toast'; if (n.link) el.href = n.link;
    const t = document.createElement('strong'); t.textContent = n.title;
    const m = document.createElement('span'); m.textContent = n.message;
    el.append(t, m); toasts.append(el);
    setTimeout(() => el.classList.add('is-leaving'), 6000);
    setTimeout(() => el.remove(), 6600);
  }

  const setOpen = open => {
    panel.hidden = !open; button.setAttribute('aria-expanded', String(open));
    if (open) load();
  };
  button.addEventListener('click', ev => { ev.stopPropagation(); setOpen(panel.hidden); });
  document.addEventListener('click', ev => { if (!panel.hidden && !root.contains(ev.target)) setOpen(false); });
  document.addEventListener('keydown', ev => { if (ev.key === 'Escape' && !panel.hidden) { setOpen(false); button.focus(); } });
  root.querySelector('[data-bell-readall]').addEventListener('click', async () => {
    try {
      const r = await post(root.dataset.readAllUrl);
      if (r.ok) await load();
    } catch { root.dataset.state = 'offline'; }
  });

  load();
  if (window.signalR) {
    const conn = new signalR.HubConnectionBuilder().withUrl(root.dataset.hubUrl).withAutomaticReconnect().build();
    conn.on('notification', n => {
      if (!seen.has(n.id)) { seen.add(n.id); toast(n); }
      load(); // SQL Server is authoritative; never increment for a duplicate or replayed message.
    });
    conn.onreconnecting(() => { root.dataset.hub = 'reconnecting'; });
    conn.onreconnected(() => { root.dataset.hub = 'connected'; load(); });
    conn.onclose(() => { root.dataset.hub = 'offline'; });
    const start = () => conn.start().then(() => { root.dataset.hub = 'connected'; load(); }).catch(() => { root.dataset.hub = 'offline'; });
    start();
    setInterval(() => { if (!document.hidden && conn.state === signalR.HubConnectionState.Disconnected) start(); }, 30000);
  }
  document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
  window.addEventListener('online', load);
  setInterval(() => { if (!document.hidden) load(); }, 15000);
})();
