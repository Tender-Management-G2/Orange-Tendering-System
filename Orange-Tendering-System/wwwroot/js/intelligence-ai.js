// ORANGE INTELLIGENCE - AI Studio: supplier selection, Summary and Text Chat (Views/Intelligence/Index.cshtml).
// Each button sends one ordinary JSON request to IntelligenceController and shows the "answer" it returns.
// The live voice call is in intelligence-audio.js.
(() => {
  'use strict';
  const studio = document.querySelector('[data-studio]');
  if (!studio) return;

  const csrfToken = document.querySelector('[data-radar]').dataset.csrf;
  const supplierSelect = studio.querySelector('[data-studio-supplier]');
  const summaryButton = studio.querySelector('[data-studio-summary-button]');
  const summaryBox = studio.querySelector('[data-studio-summary]');
  const chatList = studio.querySelector('[data-studio-chat]');
  const askForm = studio.querySelector('[data-studio-ask-form]');
  const questionInput = studio.querySelector('[data-studio-question]');
  const errorBox = studio.querySelector('[data-studio-error]');

  // Sends JSON to the server and returns the JSON reply. Throws an Error with the server's message on failure.
  async function postJson(url, body) {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrfToken },
      body: JSON.stringify(body)
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(data.error || `Request failed (${response.status}).`);
    }
    return data;
  }

  function showError(message) {
    errorBox.textContent = message || '';
    errorBox.hidden = !message;
  }

  // Returns the chosen supplier ID, or shows an error and returns null.
  function selectedSupplierId() {
    const id = Number(supplierSelect.value);
    if (!id) {
      showError('Please choose a supplier first.');
      return null;
    }
    showError('');
    return id;
  }

  // The supplier list comes from the page's analytics data (already limited to the user's authorized scope).
  function fillSuppliers(suppliers) {
    const current = supplierSelect.value;
    supplierSelect.length = 1; // keep the "Choose a supplier…" option
    const sorted = suppliers.slice().sort((a, b) => a.name.localeCompare(b.name));
    for (const supplier of sorted) {
      const name = supplier.name.replace(/^RADAR DEMO /, ''); // the demo dataset's label is not part of the name
      supplierSelect.add(new Option(name, supplier.supplierId));
    }
    supplierSelect.value = current;
  }

  // Shows one of the three modes: summary, chat or voice.
  function setMode(mode) {
    for (const button of studio.querySelectorAll('[data-studio-mode]')) {
      button.setAttribute('aria-selected', String(button.dataset.studioMode === mode));
    }
    for (const panel of studio.querySelectorAll('[data-studio-panel]')) {
      panel.hidden = panel.dataset.studioPanel !== mode;
    }
    showError('');
  }

  // A. Supplier summary
  async function generateSummary() {
    const supplierId = selectedSupplierId();
    if (!supplierId) return;
    summaryButton.disabled = true;
    summaryBox.innerHTML = '<p class="ais-loading">Gemini is writing the summary…</p>';
    try {
      const data = await postJson(studio.dataset.urlSummary, { supplierId });
      summaryBox.textContent = data.answer;
    } catch (error) {
      summaryBox.textContent = '';
      showError(error.message);
    }
    summaryButton.disabled = false;
  }

  // B. Text chat: each question is sent on its own (no conversation memory).
  function addMessage(text, who) {
    const item = document.createElement('li');
    item.className = `ais-msg is-${who}`;
    item.dir = 'auto';
    item.textContent = text;
    chatList.append(item);
    item.scrollIntoView({ block: 'nearest' });
    return item;
  }

  async function askQuestion(event) {
    event.preventDefault();
    const supplierId = selectedSupplierId();
    const question = questionInput.value.trim();
    if (!supplierId || !question) return;
    addMessage(question, 'user');
    questionInput.value = '';
    const reply = addMessage('Thinking…', 'ai');
    reply.classList.add('ais-loading');
    try {
      const data = await postJson(studio.dataset.urlAsk, { supplierId, question });
      reply.textContent = data.answer;
    } catch (error) {
      reply.textContent = error.message;
      reply.classList.add('is-error');
    }
    reply.classList.remove('ais-loading');
  }

  studio.querySelector('.ais-modes').addEventListener('click', event => {
    const button = event.target.closest('[data-studio-mode]');
    if (button) setMode(button.dataset.studioMode);
  });
  summaryButton.addEventListener('click', generateSummary);
  askForm.addEventListener('submit', askQuestion);
  supplierSelect.addEventListener('change', () => showError(''));
  window.addEventListener('radar:data', event => fillSuppliers(event.detail.suppliers));
  if (window.OrangeRadar?.data) fillSuppliers(window.OrangeRadar.data.suppliers);

  // Used by the "Ask AI" buttons in the Supplier Universe: open AI Studio with that supplier already chosen.
  window.OrangeIntelligenceAI = {
    chooseSupplier(supplierId) {
      supplierSelect.value = String(supplierId);
      supplierSelect.dispatchEvent(new Event('change')); // the supplier list (intelligence-studio-picker.js) shows the choice
      window.OrangeRadar?.setTab('studio');
      setMode('summary');
    }
  };
})();
