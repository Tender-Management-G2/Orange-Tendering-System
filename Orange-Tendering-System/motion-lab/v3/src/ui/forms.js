// Lab forms. They behave like the real pages (labels, validation, keyboard, errors beside fields)
// but submit nothing. What they hand to the director is deliberately small and sanitised:
// readiness flags, never the password and never the e-mail address.

// Mirrors the server: Register.cshtml.cs requires StringLength(100, MinimumLength = 8) and ASP.NET
// Core Identity's default options require a digit, lower, upper and non-alphanumeric character.
export const PASSWORD_RULES = [
  { test: v => v.length >= 8, label: 'at least 8 characters' },
  { test: v => /[a-z]/.test(v), label: 'a lowercase letter' },
  { test: v => /[A-Z]/.test(v), label: 'an uppercase letter' },
  { test: v => /\d/.test(v), label: 'a number' },
  { test: v => /[^A-Za-z0-9]/.test(v), label: 'a symbol' }
];

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
export const isEmail = v => EMAIL.test(v.trim());

function showError(field, message) {
  const box = field.querySelector('.field-error');
  const input = field.querySelector('input');
  if (message) {
    box.textContent = message;
    box.hidden = false;
    field.classList.add('has-error');
    input.setAttribute('aria-invalid', 'true');
  } else {
    box.hidden = true;
    box.textContent = '';
    field.classList.remove('has-error');
    input.removeAttribute('aria-invalid');
  }
}

function bindReveal(form) {
  for (const button of form.querySelectorAll('[data-reveal]')) {
    button.addEventListener('click', () => {
      const input = form.querySelector(`#${button.dataset.reveal}`);
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      button.textContent = show ? 'Hide' : 'Show';
      button.setAttribute('aria-pressed', String(show));
    });
  }
}

export function bindRegistrationForm(form, onChange) {
  const el = name => form.querySelector(`[data-field="${name}"]`);
  const input = name => el(name).querySelector('input');
  const fields = ['name', 'email', 'password', 'confirm'];
  const touched = new Set();
  let submitted = false;
  let focus = null;
  const note = form.querySelector('.form-note');
  bindReveal(form);

  const read = () => {
    const name = input('name').value;
    const email = input('email').value;
    const password = input('password').value;
    const confirm = input('confirm').value;
    const met = PASSWORD_RULES.map(r => r.test(password));
    const rules = met.filter(Boolean).length;

    let emailState = 'empty';
    if (email.trim()) emailState = isEmail(email) ? 'valid' : focus === 'email' && !submitted ? 'typing' : 'invalid';

    let confirmState = 'empty';
    if (confirm) {
      if (confirm === password) confirmState = 'match';
      else if (focus === 'confirm' && password.startsWith(confirm) && !submitted) confirmState = 'partial';
      else confirmState = 'mismatch';
    }
    return { name, email, password, confirm, met, rules, emailState, confirmState };
  };

  const render = () => {
    const s = read();
    const show = f => submitted || touched.has(f);

    showError(el('name'), show('name') && !s.name.trim() ? "Enter the contact person's full name." : '');
    showError(el('email'), s.emailState === 'invalid' || (show('email') && !s.email.trim()) ? 'Enter a valid email address, for example name@company.com.' : '');

    form.querySelectorAll('.rules li').forEach((li, i) => {
      li.classList.toggle('is-met', s.met[i]);
      li.querySelector('.state').textContent = s.met[i] ? '— met' : '— not met';
    });
    const missing = PASSWORD_RULES.filter((_, i) => !s.met[i]).map(r => r.label);
    showError(el('password'), show('password') && missing.length ? `Password still needs ${missing.join(', ')}.` : '');
    showError(
      el('confirm'),
      s.confirmState === 'mismatch' ? 'The two passwords do not match.' : show('confirm') && !s.confirm ? 'Enter the password again to confirm it.' : ''
    );

    onChange({
      name: s.name,
      email: s.emailState,
      hasPassword: s.password.length > 0,
      rules: s.rules,
      confirm: s.confirmState,
      focus
    });
    return s;
  };

  for (const f of fields) {
    const i = input(f);
    i.addEventListener('input', () => {
      note.textContent = '';
      render();
    });
    i.addEventListener('focus', () => {
      focus = f;
      render();
    });
    i.addEventListener('blur', () => {
      if (i.value) touched.add(f);
      if (focus === f) focus = null;
      render();
    });
  }

  form.addEventListener('submit', event => {
    event.preventDefault();
    submitted = true;
    const s = render();
    const order = [
      ['name', !s.name.trim()],
      ['email', s.emailState !== 'valid'],
      ['password', s.rules < PASSWORD_RULES.length],
      ['confirm', s.confirmState !== 'match']
    ];
    const firstInvalid = order.find(([, bad]) => bad);
    if (firstInvalid) {
      input(firstInvalid[0]).focus();
      note.textContent = '';
      return;
    }
    note.textContent = 'This is where the lab stops. Company details (R03) belong to the next review gate — nothing was submitted or saved.';
  });

  return {
    render,
    /** Synthetic values for QA and the inspector presets. */
    fill(values) {
      for (const [f, v] of Object.entries(values)) {
        const i = input(f);
        i.value = v;
        if (v) touched.add(f);
        else touched.delete(f);
      }
      submitted = false;
      note.textContent = '';
      render();
    },
    reset() {
      form.reset();
      touched.clear();
      submitted = false;
      note.textContent = '';
      render();
    }
  };
}

export function bindLoginForm(form, onChange) {
  const el = name => form.querySelector(`[data-field="${name}"]`);
  const input = name => el(name).querySelector('input');
  const touched = new Set();
  let submitted = false;
  let focus = null;
  const note = form.querySelector('.form-note');
  bindReveal(form);

  const read = () => {
    const email = input('email').value;
    const password = input('password').value;
    let emailState = 'empty';
    if (email.trim()) {
      if (isEmail(email)) emailState = 'valid';
      else if (focus === 'email' && !submitted) emailState = email.includes('@') ? 'domain' : 'typing';
      else emailState = 'invalid';
    }
    return { email, password, emailState };
  };

  const render = () => {
    const s = read();
    const show = f => submitted || touched.has(f);
    showError(el('email'), s.emailState === 'invalid' || (show('email') && !s.email.trim()) ? 'Enter a valid email address, for example name@company.com.' : '');
    showError(el('password'), show('password') && !s.password ? 'Enter your password.' : '');
    // Only "is there a password" leaves the form: not its value, not its length.
    onChange({ email: s.emailState, hasPassword: s.password.length > 0, focus });
    return s;
  };

  for (const f of ['email', 'password']) {
    const i = input(f);
    i.addEventListener('input', () => {
      note.textContent = '';
      render();
    });
    i.addEventListener('focus', () => {
      focus = f;
      render();
    });
    i.addEventListener('blur', () => {
      touched.add(f);
      if (focus === f) focus = null;
      render();
    });
  }

  for (const a of form.querySelectorAll('[data-inert-link]')) a.addEventListener('click', e => e.preventDefault());

  form.addEventListener('submit', event => {
    event.preventDefault();
    submitted = true;
    const s = render();
    if (s.emailState !== 'valid') {
      input('email').focus();
      return;
    }
    if (!s.password) {
      input('password').focus();
      return;
    }
    note.textContent = 'This is where the lab stops. The verification gate (L03) belongs to the next review gate — no credentials were checked or sent.';
  });

  return {
    render,
    fill(values) {
      for (const [f, v] of Object.entries(values)) {
        input(f).value = v;
        if (v) touched.add(f);
        else touched.delete(f);
      }
      submitted = false;
      note.textContent = '';
      render();
    },
    reset() {
      form.reset();
      touched.clear();
      submitted = false;
      note.textContent = '';
      render();
    }
  };
}
