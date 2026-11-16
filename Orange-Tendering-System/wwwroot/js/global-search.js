"use strict";
// One search/microphone controller for all seven authenticated workspaces.
(() => {
  const root = document.querySelector('[data-global-search]');
  if (!root) return;
  const input = root.querySelector('input');
  const results = root.querySelector('.ws-search-results');
  const voice = document.querySelector('[data-voice-search]');
  const language = document.querySelector('[data-voice-language]');
  const status = document.querySelector('[data-search-feedback]');
  input.maxLength = 100;
  let timer, request, sequence = 0, active = -1, recognition, listening = false;
  const say = message => { if (status) status.textContent = message; };
  const close = () => { results.hidden = true; active = -1; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); };
  const node = (tag, cls, text) => { const n = document.createElement(tag); n.className = cls; n.textContent = text; return n; };
  const show = (items, message = '') => {
    results.replaceChildren(); active = -1; input.removeAttribute('aria-activedescendant');
    if (!items.length) results.append(node('p', 'ws-search-empty', message || 'No matching records you can access.'));
    let group = '';
    items.forEach((item, i) => {
      const url = new URL(item.url, location.href);
      if (url.origin !== location.origin) return;
      if (group !== item.type) { group = item.type; results.append(node('span', 'ws-search-group', group)); }
      const link = node('a', 'ws-search-item', ''); link.href = url.href;
      link.id = 'global-search-option-' + i; link.setAttribute('role', 'option'); link.setAttribute('aria-selected', 'false');
      link.append(node('strong', '', item.title), node('small', '', item.subtitle)); results.append(link);
    });
    results.hidden = false; input.setAttribute('aria-expanded', 'true');
    say(message || (items.length ? `${items.length} search results.` : 'No matching records you can access.'));
  };
  const search = () => {
    clearTimeout(timer); request?.abort(); const mine = ++sequence;
    const q = input.value.trim();
    if (q.length < 2) { close(); say('Type at least two characters.'); return; }
    show([], 'Searching…');
    timer = setTimeout(async () => {
      request = new AbortController();
      try {
        const url = new URL(root.dataset.searchUrl, location.href); url.searchParams.set('q', q);
        const response = await fetch(url, { signal: request.signal, credentials: 'same-origin', headers: { Accept: 'application/json' }, cache: 'no-store' });
        if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Search unavailable');
        const data = await response.json();
        if (mine === sequence) show(data.items);
      } catch (error) {
        if (error.name !== 'AbortError' && mine === sequence) show([], 'Search is unavailable. Try again or sign in again.');
      }
    }, 220);
  };
  input.addEventListener('input', search);
  input.addEventListener('search', search);
  input.addEventListener('keydown', event => {
    if (event.key === 'Escape') { ++sequence; clearTimeout(timer); request?.abort(); close(); return; }
    if (results.hidden) return;
    const links = [...results.querySelectorAll('.ws-search-item')];
    if (event.key === 'Enter' && links.length) { event.preventDefault(); links[Math.max(active, 0)].click(); }
    if (!links.length || !['ArrowDown', 'ArrowUp'].includes(event.key)) return;
    event.preventDefault();
    active = active < 0 ? (event.key === 'ArrowDown' ? 0 : links.length - 1) : (active + (event.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
    links.forEach((link, i) => { link.classList.toggle('is-active', i === active); link.setAttribute('aria-selected', String(i === active)); });
    input.setAttribute('aria-activedescendant', links[active].id); links[active].scrollIntoView({ block: 'nearest' });
  });
  document.addEventListener('click', event => { if (!root.contains(event.target) && event.target !== voice && event.target !== language) { ++sequence; clearTimeout(timer); request?.abort(); close(); } });
  if (!voice) return;
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (language) {
    try { language.value = localStorage.getItem('orange-search-language') || (navigator.language.startsWith('ar') ? 'ar-JO' : 'en-US'); } catch { /* optional preference */ }
    language.addEventListener('change', () => { recognition?.abort(); try { localStorage.setItem('orange-search-language', language.value); } catch { /* optional preference */ } });
  }
  const reset = () => { listening = false; voice.classList.remove('is-listening'); voice.setAttribute('aria-pressed', 'false'); voice.setAttribute('aria-label', 'Voice search'); };
  voice.addEventListener('click', () => {
    if (listening) { recognition?.abort(); reset(); say('Voice search stopped.'); return; }
    if (!Recognition || !window.isSecureContext) { const message = !Recognition ? 'This browser does not support voice search. Type your search instead.' : 'Microphone search requires HTTPS or localhost.'; say(message); window.orangeUi?.toast(message, 'info'); input.focus(); return; }
    const current = new Recognition(); recognition = current; recognition.lang = language?.value || 'en-US'; recognition.interimResults = false; recognition.maxAlternatives = 1; recognition.continuous = false;
    listening = true; voice.setAttribute('aria-pressed', 'true'); voice.setAttribute('aria-label', 'Stop voice search'); voice.classList.add('is-listening'); say('Listening…');
    recognition.onresult = event => { if (recognition !== current || !listening) return; const phrase = event.results[0]?.[0]?.transcript; if (phrase) { input.value = phrase.trim().slice(0, 100); input.dispatchEvent(new Event('input', { bubbles: true })); input.focus(); } };
    recognition.onend = () => { if (recognition === current) reset(); };
    recognition.onerror = event => { if (recognition !== current) return; reset(); if (event.error === 'aborted') return; const messages = { 'not-allowed': 'Microphone permission was denied. Allow it in browser settings or type your search.', 'audio-capture': 'No microphone is available.', 'no-speech': 'No speech detected. Try again.', network: 'The browser speech service is unavailable. Type your search instead.' }; const message = messages[event.error] || 'Voice search stopped. Try again or type your search.'; say(message); window.orangeUi?.toast(message, 'info'); };
    try { recognition.start(); } catch { reset(); say('Unable to start the microphone. Try again or type your search.'); }
  });
  window.addEventListener('pagehide', () => { recognition?.abort(); request?.abort(); clearTimeout(timer); });
})();
