"use strict";
(() => {
  const form = document.querySelector('#contact-form');
  if (!form) return;
  const status = document.querySelector('#form-status');
  const button = form.querySelector('[type="submit"]');
  const fields = [
    { input: form.elements.name, error: document.querySelector('#name-error'), message: 'Enter your full name (up to 100 characters).' },
    { input: form.elements.email, error: document.querySelector('#email-error'), message: 'Enter a valid email address (up to 254 characters).' },
    { input: form.elements.topic, error: document.querySelector('#topic-error'), message: 'Choose an inquiry topic.' },
    { input: form.elements.message, error: document.querySelector('#message-error'), message: 'Enter a message between 10 and 3,000 characters.' },
  ];
  // Native required/type checks remain available when JavaScript is disabled.
  form.noValidate = true;
  function validate(field) {
    const value = field.input.value.trim();
    const valid = value.length > 0 && field.input.validity.valid && (field.input.name !== 'message' || value.length >= 10);
    field.input.setAttribute('aria-invalid', String(!valid));
    field.error.textContent = valid ? '' : field.message;
    return valid;
  }
  fields.forEach(field => {
    field.input.addEventListener('blur', () => validate(field));
    field.input.addEventListener('input', () => { if(field.input.hasAttribute('aria-invalid')) validate(field); });
    field.input.addEventListener('change', () => { if(field.input.hasAttribute('aria-invalid')) validate(field); });
  });
  let pending = false;
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (pending) return;
    status.classList.remove('is-success');
    if (!fields.map(validate).every(Boolean)) {
      status.textContent = 'Please correct the highlighted fields. Nothing was sent.';
      fields.find(field => field.input.getAttribute('aria-invalid') === 'true').input.focus();
      return;
    }
    pending = true; button.disabled = true; form.setAttribute('aria-busy','true');
    status.textContent = 'Submitting your inquiry. Waiting for server confirmation…';
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 20000);
    try {
      const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' }, signal: controller.signal });
      const json = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
      if (response.status === 422 && json?.errors) {
        for (const field of fields) {
          const messages = Object.entries(json.errors).find(([key]) => key.toLowerCase() === field.input.name)?.[1];
          if (messages?.length) { field.input.setAttribute('aria-invalid','true'); field.error.textContent = messages.join(' '); }
        }
        fields.find(field => field.input.getAttribute('aria-invalid') === 'true')?.input.focus();
      }
      if (response.ok && typeof json?.message === 'string') {
        status.textContent = json.message; status.classList.add('is-success'); form.reset();
        fields.forEach(field => { field.input.removeAttribute('aria-invalid'); field.error.textContent=''; });
      } else {
        status.textContent = response.status === 503 ? 'Submission is currently unavailable. Saving was not confirmed. Your entries have been kept. Please try again later.' : response.status === 429 ? 'Too many requests. Wait a minute before retrying. Your entries have been kept.' : json?.message || 'Submission is unavailable or was not confirmed. Your entries have been kept. Please try again later.';
      }
    } catch (error) {
      status.textContent = error.name === 'AbortError' ? 'The request timed out. Saving was not confirmed. Your entries have been kept; check before retrying to avoid a duplicate inquiry.' : 'The server could not be reached. Saving was not confirmed. Your entries have been kept.';
    } finally {
      clearTimeout(timeout); pending = false; button.disabled = false; form.removeAttribute('aria-busy');
    }
  });
})();
