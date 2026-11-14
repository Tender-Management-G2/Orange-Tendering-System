"use strict";
(() => {
  const host = document.querySelector('[data-tc-search]');
  if (!host) return;
  const input = host.querySelector('input');
  const results = host.querySelector('#tc-search-results');
  const voice = document.querySelector('[data-tc-voice]');
  const status = document.createElement('p');
  status.setAttribute('role', 'status');
  host.append(status);
  let timer, sequence = 0;
  function close() { results.hidden = true; input.setAttribute('aria-expanded', 'false'); }
  function message(text) { status.textContent = text; }
  function show() { results.hidden = false; input.setAttribute('aria-expanded', 'true'); }
  async function search() {
    clearTimeout(timer);
    const mine = ++sequence;
    const q = input.value.trim();
    close();
    if (!q) return;
    message('Searching…');
    try {
      const url = new URL(host.dataset.searchUrl, location.href);
      url.searchParams.set('q', q);
      const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error();
      const data = await response.json();
      if (mine !== sequence) return;
      results.replaceChildren();
      for (const item of data.items) {
        const link = document.createElement('a');
        link.href = item.url;
        const title = document.createElement('strong');
        title.textContent = item.title;
        const subtitle = document.createElement('small');
        subtitle.textContent = item.subtitle;
        link.append(title, subtitle);
        results.append(link);
      }
      message(data.items.length ? `${data.items.length} results.` : 'No matching committee tenders.');
      if (data.items.length) show();
    } catch { if (mine === sequence) message('Search could not be loaded. Please try again.'); }
  }
  input.addEventListener('input', () => {
    ++sequence; clearTimeout(timer); close(); message('');
    timer = setTimeout(search, 220);
  });
  host.addEventListener('keydown', event => {
    if (event.key === 'Escape') { ++sequence; clearTimeout(timer); close(); input.focus(); }
    if (event.key === 'Enter' && event.target === input) { event.preventDefault(); search(); }
    if (event.key === 'ArrowDown' && event.target === input && !results.hidden) {
      event.preventDefault(); results.querySelector('a')?.focus();
    }
  });
  document.addEventListener('click', event => {
    if (!host.contains(event.target) && !voice.contains(event.target)) { ++sequence; clearTimeout(timer); close(); }
  });
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) {
    voice.addEventListener('click', () => { message('Voice search is not supported by this browser. Type your search instead.'); input.focus(); });
    return;
  }
  const recognition = new Recognition();
  recognition.lang = navigator.language || 'ar-JO';
  recognition.interimResults = false;
  recognition.continuous = false;
  let listening = false;
  recognition.onstart = () => {
    listening = true; voice.setAttribute('aria-pressed', 'true'); voice.title = 'Stop listening';
    message('Listening… Speak your tender search.');
  };
  recognition.onresult = event => {
    const transcript = event.results[0][0].transcript.trim();
    if (!transcript) { message('No speech detected. Please try again.'); return; }
    input.value = transcript; input.focus(); search();
  };
  recognition.onerror = event => {
    message(event.error === 'not-allowed' || event.error === 'service-not-allowed'
      ? 'Allow microphone access in your browser to use voice search.'
      : event.error === 'audio-capture' ? 'No microphone is available.'
      : 'Voice search could not hear you. Please try again.');
  };
  recognition.onend = () => {
    listening = false; voice.setAttribute('aria-pressed', 'false'); voice.title = 'Search by voice';
    if (status.textContent.startsWith('Listening')) message('Listening stopped. Please try again.');
  };
  voice.addEventListener('click', () => {
    try { if (listening) recognition.stop(); else recognition.start(); }
    catch { message('Voice search could not start. Please try again.'); }
  });
  window.addEventListener('pagehide', () => recognition.abort());
})();
