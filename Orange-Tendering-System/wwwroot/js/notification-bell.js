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
  let unread = 0, connected = false;

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
  const post = url => fetch(url, { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf } });

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
    open.addEventListener('click', async () => {
      if (!n.isRead) {
        n.isRead = true; li.classList.remove('is-unread');
        try { const r = await post('/Notifications/Read?id=' + n.id); if (r.ok) setUnread((await r.json()).unread); } catch { /* the link still opens */ }
      }
    });
    return li;
  }
  function render(items) {
    list.replaceChildren(...items.map(row));
    empty.hidden = items.length > 0;
  }
  async function load() {
    try {
      const r = await fetch('/Notifications/Feed?take=12', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (!r.ok) return;
      const data = await r.json();
      setUnread(data.unread); render(data.items);
    } catch { /* offline: keep the previous state */ }
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
    const r = await post('/Notifications/ReadAll');
    if (r.ok) { setUnread(0); list.querySelectorAll('.is-unread').forEach(x => x.classList.remove('is-unread')); }
  });

  load();
  if (window.signalR) {
    const conn = new signalR.HubConnectionBuilder().withUrl('/hubs/notifications').withAutomaticReconnect().build();
    conn.on('notification', n => {
      setUnread(unread + 1);
      if (!panel.hidden) { list.prepend(row(n)); empty.hidden = true; }
      toast(n);
    });
    conn.onreconnected(load);
    conn.onclose(() => { connected = false; });
    conn.start().then(() => { connected = true; root.dataset.hub = 'connected'; }).catch(() => { connected = false; });
  }
  setInterval(() => { if (!connected && !document.hidden) load(); }, 45000);
})();
