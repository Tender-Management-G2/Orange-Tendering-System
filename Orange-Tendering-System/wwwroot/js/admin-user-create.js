// Admin > Create User: live summary card, role-dependent department requirement, Sourcing Officer note,
// password strength checklist and a single-submit guard. The server (AdminService.CreateUserAsync) stays authoritative.
(function () {
    'use strict';
    var form = document.querySelector('[data-user-create]');
    if (!form) return;
    var $ = function (s) { return form.querySelector(s); };
    var deptRoles = (form.dataset.deptRoles || '').split('|');
    var name = $('#FullName'), email = $('#Email'), dept = $('#DepartmentId'), pwd = $('#Password'), active = $('#IsActive');
    var sum = function (k) { return form.querySelector('[data-sum="' + k + '"]'); };

    function role() {
        var r = form.querySelector('input[name="Role"]:checked') || form.querySelector('input[type="hidden"][name="Role"]');
        return r ? r.value : '';
    }
    function initials(v) {
        var parts = v.trim().split(/\s+/).filter(Boolean);
        return parts.length ? (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase() : '?';
    }
    function needsDept() { return deptRoles.indexOf(role()) > -1; }

    function render() {
        var n = name.value.trim(), e = email.value.trim(), r = role();
        sum('name').textContent = n || 'Full name';
        sum('initials').textContent = initials(n);
        sum('email').textContent = e || 'e-mail address';
        sum('role').textContent = r || 'Not selected';
        sum('dept').textContent = dept.value ? dept.options[dept.selectedIndex].text : 'No department';
        var on = active.checked; sum('active').textContent = on ? 'Active' : 'Inactive'; sum('active').classList.toggle('is-off', !on);
        var need = needsDept();
        form.querySelector('[data-dept-star]').hidden = !need;
        form.querySelector('[data-dept-optional]').hidden = need;
        dept.required = need; dept.setAttribute('aria-required', need ? 'true' : 'false');
        var note = form.querySelector('[data-officer-note]'); if (note) note.hidden = r !== form.dataset.officerRole;
        form.querySelectorAll('.auc-role').forEach(function (l) { l.classList.toggle('is-checked', l.querySelector('input').checked); });
    }

    var rules = {
        len: function (v) { return v.length >= 8; }, upper: function (v) { return /[A-Z]/.test(v); }, lower: function (v) { return /[a-z]/.test(v); },
        digit: function (v) { return /\d/.test(v); }, symbol: function (v) { return /[^A-Za-z0-9]/.test(v); }
    };
    function strength() {
        var v = pwd.value, ok = 0;
        Object.keys(rules).forEach(function (k) { var pass = rules[k](v); if (pass) ok++; var li = form.querySelector('[data-rule="' + k + '"]'); if (li) li.classList.toggle('is-ok', pass); });
        var meter = form.querySelector('[data-meter]');
        meter.style.width = (v ? ok * 20 : 0) + '%';
        meter.dataset.level = ok <= 2 ? 'weak' : ok < 5 ? 'fair' : 'strong';
    }

    function deptError(show) {
        var el = form.querySelector('[data-dept-error]'); if (!el) return;
        el.textContent = show ? 'Department is required for this role.' : '';
        dept.classList.toggle('input-validation-error', show);
    }

    form.addEventListener('input', function (e) { render(); if (e.target === pwd) strength(); if (e.target === dept) deptError(false); });
    form.addEventListener('change', function () { render(); strength(); deptError(false); });
    // The generator in admin-temp-password.js sets the value without an input event.
    var gen = form.querySelector('[data-password-generate]'); if (gen) gen.addEventListener('click', function () { setTimeout(strength, 0); });

    var submitting = false;
    form.addEventListener('submit', function (e) {
        var valid = !(window.jQuery && jQuery(form).valid) || jQuery(form).valid();
        if (needsDept() && !dept.value) { deptError(true); valid = false; }
        if (!role()) { var rerr = form.querySelector('[data-valmsg-for="Role"]'); if (rerr && !rerr.textContent) rerr.textContent = 'Please select a role.'; valid = false; }
        if (!valid || submitting) {
            e.preventDefault();
            var first = form.querySelector('.input-validation-error, [data-valmsg-for].field-validation-error, [data-dept-error]:not(:empty)');
            if (first) (first.matches('input,select') ? first : (first.closest('.auc-field, .auc-card') || first)).scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }
        submitting = true;
        var btn = form.querySelector('[data-submit]'); btn.disabled = true; btn.classList.add('is-busy');
        form.querySelector('[data-submit-label]').textContent = 'Creating account…';
    });
    // If the browser restores the page from the back/forward cache, allow submitting again.
    window.addEventListener('pageshow', function () { submitting = false; var b = form.querySelector('[data-submit]'); b.disabled = false; b.classList.remove('is-busy'); form.querySelector('[data-submit-label]').textContent = 'Create account'; });

    render(); strength();
})();
