"use strict";
// Loaded by _AdminLayout only. Other workspaces keep their existing search.
(() => {
  if (document.body.dataset.workspaceRole !== 'Admin') return;
  const form = document.querySelector('[data-admin-search]');
  const input = form.querySelector('input');
  const voice = document.querySelector('[data-admin-voice]');
  const status = document.querySelector('#admin-search-status');
  const main = document.querySelector('#workspace-main');
  const serverSearch = form.dataset.serverSearch === 'true';
  const announce = message => { status.textContent = message; };
  const voiceMessage = message => {
    announce(message);
    if (window.orangeUi?.toast) window.orangeUi.toast(message, 'info');
  };

  function searchPage() {
    const tables = [...main.querySelectorAll('.ui-table-search')];
    tables.forEach(search => {
      search.value = input.value;
      search.dispatchEvent(new Event('input', { bubbles: true }));
    });
    // Do not hide whole sections or editable form fields while searching.
    const items = main.querySelectorAll('.dash-metric, .dash-activity-item, .dash-task-item, .pg-links li, [data-search-item]');
    const term = input.value.trim().toLocaleLowerCase();
    let matches = 0;
    items.forEach(item => {
      item.hidden = !!term && !item.textContent.toLocaleLowerCase().includes(term);
      if (!item.hidden) matches++;
    });
    announce(!term ? 'Page search cleared.' : tables.length
      ? 'Table results updated on this page.'
      : items.length ? `${matches} matching items on this page.`
      : 'This page has no searchable records. Open a list page to search its records.');
  }

  if (serverSearch) input.value = new URL(location.href).searchParams.get('search') || '';
  else input.addEventListener('input', searchPage);
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (!serverSearch) { searchPage(); return; }
    const url = new URL(location.href);
    // Keep the current page's filters, including changes not yet submitted.
    const filters = [...main.querySelectorAll('form')].find(f =>
      f.method.toLowerCase() === 'get' && (f.querySelector('[name="search"]') || f.classList.contains('pg-toolbar')));
    if (filters) new FormData(filters).forEach((value, key) => {
      if (typeof value === 'string') url.searchParams.set(key, value);
    });
    if (input.value.trim()) url.searchParams.set('search', input.value.trim());
    else url.searchParams.delete('search');
    url.searchParams.delete('page');
    location.assign(url.href);
  });

  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) {
    voice.addEventListener('click', () => {
      voiceMessage('Voice search is not supported in this browser. Type your search instead.');
      input.focus();
    });
    return;
  }
  const recognition = new Recognition();
  recognition.lang = document.documentElement.lang || 'en-US';
  recognition.interimResults = false;
  recognition.maxAlternatives = 1;
  let listening = false;
  let receivedResult = false;
  let failed = false;
  recognition.onstart = () => {
    receivedResult = false;
    failed = false;
    voiceMessage('Listening. Say what you want to find.');
  };
  recognition.onend = () => {
    listening = false;
    voice.classList.remove('is-listening');
    voice.setAttribute('aria-pressed', 'false');
    voice.setAttribute('aria-label', 'Start voice search');
    if (!receivedResult && !failed) voiceMessage('No speech was recognized. Try again and wait for the listening message before speaking.');
  };
  recognition.onresult = event => {
    const transcript = event.results?.[0]?.[0]?.transcript?.trim();
    if (!transcript) return;
    receivedResult = true;
    input.value = transcript;
    form.requestSubmit();
  };
  recognition.onerror = event => {
    failed = true;
    const messages = {
      'not-allowed': 'Microphone access was blocked. Check browser and Windows microphone permissions.',
      'service-not-allowed': 'The browser blocked its speech recognition service. Try a browser that supports voice recognition.',
      'audio-capture': 'No working microphone was found. Check the selected microphone in your browser and Windows.',
      'network': 'The speech recognition service could not connect. Check your internet connection and try again.',
      'no-speech': 'No speech was detected. Wait for the listening message, then speak close to your microphone.',
      'language-not-supported': 'The speech recognition service does not support the selected language.'
    };
    voiceMessage(messages[event.error] || `Voice recognition failed (${event.error}). Try again or type your search.`);
  };
  voice.addEventListener('click', () => {
    if (listening) { recognition.stop(); return; }
    try {
      recognition.start();
      listening = true;
      voice.classList.add('is-listening');
      voice.setAttribute('aria-pressed', 'true');
      voice.setAttribute('aria-label', 'Stop voice search');
    } catch {
      listening = false;
      voice.classList.remove('is-listening');
      voice.setAttribute('aria-pressed', 'false');
      voice.setAttribute('aria-label', 'Start voice search');
      voiceMessage('Unable to start voice search. Reload the page and try again.');
    }
  });
})();
