// Orange form validation — one engine for every POST form in all seven role workspaces, the public/Identity pages
// and the AJAX forms inside the tender workflow popup (Views/Shared/_WorkflowPopup.cshtml).
//
// Rules come from the markup the server renders:
//   required / aria-required / data-val-required (asp-for + [Required]), type=email|number|date|datetime-local|tel|url|file,
//   min / max / minlength / maxlength / pattern / accept, data-val-range-*, data-val-length-*, data-val-regex-*, data-val-equalto-*.
// Extra declarative rules:
//   data-fv-label="Tender title"            name used in messages (otherwise the field's <label>)
//   data-fv-required-when="decision=Reject" required only while another field (or the clicked submit button) has that value
//   data-fv-after="StartsAt"                date/number must be later than another field of the same form
//   data-fv-future                          date must be later than now
//   data-fv-max-mb="20"                     file size limit (default 20 MB when accept= is set)
//   data-fv-group-required on a container   at least one checkbox inside must be ticked (data-fv-label names the items)
//   data-fv-msg="..." / data-fv-msg-ar=".." exact message overrides
//   formnovalidate / data-fv-skip on a submit button skip validation (draft saves); data-fv-skip on a form opts out.
// Behaviour: red border + message under the field, * on mandatory labels, submission blocked, first invalid field focused and
// scrolled into view, the error clears as soon as the value is corrected, and nothing typed is lost. Arabic messages when the
// interface language is Arabic (cookie "orange-lang"). The server re-checks every rule (ModelState + TenderWorkflowService.Require).
(() => {
    if (window.OrangeValidation) return;

    const cookieLang = (document.cookie.match(/(?:^|;\s*)orange-lang=(ar|en)(?:;|$)/) || [])[1];
    const AR = cookieLang ? cookieLang === 'ar' : (document.documentElement.lang || '').toLowerCase().startsWith('ar');

    // Arabic names for the field labels used across the system (English label -> Arabic). Unknown labels are quoted as written.
    const AR_LABELS = {
        'title': 'العنوان', 'tender title': 'عنوان العطاء', 'request title': 'عنوان الطلب', 'name': 'الاسم', 'full name': 'الاسم الكامل',
        'description': 'الوصف', 'details': 'التفاصيل', 'justification': 'المبرر', 'business justification': 'المبرر التجاري',
        'reason': 'السبب', 'comment': 'التعليق', 'comments': 'التعليقات', 'notes': 'الملاحظات', 'message': 'الرسالة', 'subject': 'الموضوع',
        'question': 'السؤال', 'answer': 'الإجابة', 'reply': 'الرد', 'feedback': 'الملاحظات', 'correction': 'التصحيح', 'decision': 'القرار',
        'email': 'البريد الإلكتروني', 'email address': 'البريد الإلكتروني', 'work email': 'البريد الإلكتروني للعمل', 'password': 'كلمة المرور',
        'new password': 'كلمة المرور الجديدة', 'confirm password': 'تأكيد كلمة المرور', 'current password': 'كلمة المرور الحالية',
        'phone': 'رقم الهاتف', 'phone number': 'رقم الهاتف', 'mobile': 'رقم الجوال', 'company': 'الشركة', 'company name': 'اسم الشركة',
        'contact name': 'اسم جهة الاتصال', 'contact person': 'الشخص المسؤول', 'address': 'العنوان', 'city': 'المدينة', 'country': 'الدولة',
        'commercial registration': 'السجل التجاري', 'commercial registration number': 'رقم السجل التجاري', 'tax number': 'الرقم الضريبي',
        'department': 'الإدارة', 'role': 'الدور', 'user': 'المستخدم', 'member': 'العضو', 'supplier': 'المورد', 'suppliers': 'الموردون',
        'category': 'الفئة', 'priority': 'الأولوية', 'status': 'الحالة', 'type': 'النوع', 'committee': 'اللجنة', 'position': 'المنصب',
        'quantity': 'الكمية', 'unit': 'الوحدة', 'unit price': 'سعر الوحدة', 'price': 'السعر', 'amount': 'المبلغ', 'budget': 'الميزانية',
        'estimated budget': 'الميزانية التقديرية', 'estimated cost': 'التكلفة التقديرية', 'currency': 'العملة', 'total': 'المجموع',
        'tax': 'الضريبة', 'tax amount': 'مبلغ الضريبة', 'shipping cost': 'تكلفة الشحن', 'installation cost': 'تكلفة التركيب',
        'maintenance cost': 'تكلفة الصيانة', 'other cost': 'تكاليف أخرى', 'warranty': 'الضمان', 'warranty months': 'مدة الضمان بالأشهر',
        'delivery days': 'مدة التسليم بالأيام', 'delivery period': 'مدة التسليم', 'validity days': 'مدة صلاحية العرض بالأيام',
        'payment terms': 'شروط الدفع', 'technical proposal': 'العرض الفني', 'item': 'البند', 'items': 'البنود', 'item name': 'اسم البند',
        'specification': 'المواصفات', 'specifications': 'المواصفات', 'score': 'الدرجة', 'weight': 'الوزن', 'financial weight': 'الوزن المالي',
        'technical weight': 'الوزن الفني', 'financial': 'المالي', 'technical': 'الفني', 'deadline': 'الموعد النهائي',
        'bidding deadline': 'الموعد النهائي لتقديم العروض', 'closing date': 'تاريخ الإغلاق', 'opening date': 'تاريخ الفتح',
        'start date': 'تاريخ البدء', 'end date': 'تاريخ الانتهاء', 'starts at': 'وقت البدء', 'ends at': 'وقت الانتهاء', 'date': 'التاريخ',
        'required by': 'تاريخ الحاجة', 'needed by': 'تاريخ الحاجة', 'file': 'الملف', 'document': 'المستند', 'attachment': 'المرفق',
        'technical requirements': 'المتطلبات الفنية', 'commercial conditions': 'الشروط التجارية', 'delivery expectations': 'متطلبات التسليم',
        'mandatory compliance': 'متطلبات الامتثال الإلزامية', 'missing requirements': 'المتطلبات الناقصة', 'compliance': 'الامتثال',
        'result': 'النتيجة', 'outcome': 'النتيجة', 'rejection reason': 'سبب الرفض', 'recommendation': 'التوصية', 'meeting link': 'رابط الاجتماع',
        'location': 'الموقع', 'code': 'الرمز', 'verification code': 'رمز التحقق', 'username': 'اسم المستخدم', 'manager': 'المدير', 'head': 'الرئيس',
        'value': 'القيمة', 'setting': 'الإعداد', 'key': 'المفتاح', 'website': 'الموقع الإلكتروني', 'recipient': 'المستلم', 'to': 'إلى'
    };

    const T = AR ? {
        required: l => l.ar ? `${l.ar} مطلوب.` : `يرجى تعبئة حقل «${l.text}».`,
        select: l => l.ar ? `يرجى اختيار ${l.ar}.` : `يرجى اختيار «${l.text}».`,
        check: () => 'يرجى تحديد هذا المربع للمتابعة.',
        group: l => l.ar ? `يرجى اختيار ${l.ar} واحد على الأقل.` : `يرجى اختيار عنصر واحد على الأقل من «${l.text}».`,
        file: () => 'يرجى اختيار ملف لرفعه.',
        fileType: t => `يرجى رفع ملف ${t} صالح.`,
        fileSize: mb => `يجب ألا يتجاوز حجم الملف ${mb} ميغابايت.`,
        email: () => 'يرجى إدخال بريد إلكتروني صالح.',
        phone: () => 'يرجى إدخال رقم هاتف صالح.',
        url: () => 'يرجى إدخال رابط صالح يبدأ بـ https://.',
        number: l => `يجب أن تكون قيمة «${l.ar || l.text}» رقمًا صالحًا.`,
        whole: l => `يجب أن تكون قيمة «${l.ar || l.text}» عددًا صحيحًا.`,
        min: (l, v) => `يجب ألا تقل قيمة «${l.ar || l.text}» عن ${v}.`,
        max: (l, v) => `يجب ألا تزيد قيمة «${l.ar || l.text}» عن ${v}.`,
        range: (l, a, b) => `يجب أن تكون قيمة «${l.ar || l.text}» بين ${a} و${b}.`,
        minLen: (l, n) => `يجب أن يحتوي «${l.ar || l.text}» على ${n} أحرف على الأقل.`,
        maxLen: (l, n) => `يجب ألا يتجاوز «${l.ar || l.text}» ${n} حرفًا.`,
        pattern: l => `صيغة «${l.ar || l.text}» غير صحيحة.`,
        date: () => 'يرجى إدخال تاريخ صالح.',
        dateMin: (l, d) => `يجب أن يكون «${l.ar || l.text}» في ${d} أو بعده.`,
        dateMax: (l, d) => `يجب أن يكون «${l.ar || l.text}» في ${d} أو قبله.`,
        future: l => `يجب أن يكون «${l.ar || l.text}» في المستقبل.`,
        after: (l, o) => `يجب أن يكون «${l.ar || l.text}» بعد «${o.ar || o.text}».`,
        equal: (l, o) => `قيمة «${l.ar || l.text}» لا تطابق «${o.ar || o.text}».`,
        blank: l => `لا يمكن أن يحتوي «${l.ar || l.text}» على مسافات فقط.`
    } : {
        required: l => `${l.text} is required.`,
        select: l => `Please select ${article(l.lower)} ${l.lower}.`,
        check: () => 'Please tick this box to continue.',
        group: l => `Please select at least one ${singular(l.lower)}.`,
        file: () => 'Please choose a file to upload.',
        fileType: t => `Please upload a valid ${t} file.`,
        fileSize: mb => `The file must be ${mb} MB or smaller.`,
        email: () => 'Please enter a valid email address.',
        phone: () => 'Please enter a valid phone number.',
        url: () => 'Please enter a valid link starting with https://.',
        number: l => `${l.text} must be a valid number.`,
        whole: l => `${l.text} must be a whole number.`,
        min: (l, v) => `${l.text} must be at least ${v}.`,
        max: (l, v) => `${l.text} must be ${v} or less.`,
        range: (l, a, b) => `${l.text} must be between ${a} and ${b}.`,
        minLen: (l, n) => `${l.text} must be at least ${n} characters.`,
        maxLen: (l, n) => `${l.text} must be ${n} characters or fewer.`,
        pattern: l => `${l.text} is not in the correct format.`,
        date: () => 'Please enter a valid date.',
        dateMin: (l, d) => `${l.text} must be on or after ${d}.`,
        dateMax: (l, d) => `${l.text} must be on or before ${d}.`,
        future: l => `${l.text} must be in the future.`,
        after: (l, o) => `${l.text} must be later than the ${o.lower}.`,
        equal: (l, o) => `${l.text} does not match the ${o.lower}.`,
        blank: l => `${l.text} cannot be only spaces.`
    };

    function article(word) { return /^[aeiou]/i.test(word) && !/^(user|unit|uni|eu)/i.test(word) ? 'an' : 'a'; }
    function singular(word) { return word.replace(/ies$/, 'y').replace(/(?<!s)s$/, ''); }

    const FIELD = 'input, select, textarea';
    const SKIP_TYPES = /^(hidden|submit|button|reset|image|range|color)$/i;
    const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
    const PHONE = /^\+?[\d\s().-]{6,20}$/;
    const managed = new WeakSet();
    const touched = new WeakSet();

    // ---------- field state ----------
    function isActive(el) {
        if (el.disabled || SKIP_TYPES.test(el.type || '') || el.closest('fieldset[disabled]')) return false;
        if (el.readOnly && el.type !== 'file' && !el.hasAttribute('data-fv-validate-readonly')) return false;
        if (el.closest('[data-fv-ignore]')) return false;
        // Visible, or only folded inside a closed <details> (opened when the error is shown).
        for (let n = el; n && n !== el.form && n.nodeType === 1; n = n.parentElement) {
            if (n.hidden && n.tagName !== 'DETAILS') return false;
            if (n.getAttribute('aria-hidden') === 'true') return false;
            const cs = getComputedStyle(n);
            if (cs.display === 'none' || cs.visibility === 'hidden') return false;
        }
        return true;
    }

    function control(form, name) {
        if (!name) return null;
        const els = form.elements.namedItem(name) || form.elements.namedItem(name.replace(/^\*\./, '')) || form.querySelector(`[name$="${CSS.escape(name.replace(/^\*\./, ''))}"]`);
        if (!els) return null;
        return els instanceof RadioNodeList ? els : els;
    }
    function valueOf(form, name, submitter) {
        if (submitter && submitter.name === name) return submitter.value;
        const c = control(form, name);
        if (!c) return '';
        if (c instanceof RadioNodeList) return c.value;
        if (c.type === 'checkbox') return c.checked ? (c.value || 'true') : '';
        return c.value;
    }

    function isRequired(el, form, submitter) {
        const when = el.getAttribute('data-fv-required-when');
        if (when) {
            return when.split(';').some(rule => {
                const [name, values] = rule.split('=');
                const v = valueOf(form, name.trim(), submitter);
                return values === undefined ? !!v : values.split('|').some(x => x.trim() === v);
            });
        }
        if (el.hasAttribute('data-fv-optional')) return false;
        if (el.required || el.getAttribute('aria-required') === 'true' || el.hasAttribute('data-fv-required')) return true;
        // asp-for adds data-val-required to every non-nullable value type; for a checkbox that only means "true or false".
        if (el.hasAttribute('data-val-required') && el.type !== 'checkbox') return true;
        if (el.type === 'radio' && form) {
            const group = form.querySelectorAll(`input[type=radio][name="${CSS.escape(el.name)}"]`);
            return Array.from(group).some(r => r.required || r.hasAttribute('data-val-required'));
        }
        return false;
    }

    // ---------- labels ----------
    function cleanLabel(text) {
        return (text || '').replace(/\s+/g, ' ').replace(/[*:：]/g, '').replace(/\((optional|اختياري)[^)]*\)/ig, '')
            .replace(/\b(optional)\b/ig, '').trim();
    }
    function labelElement(el) {
        if (el.id) { const l = el.form?.querySelector(`label[for="${CSS.escape(el.id)}"]`) || document.querySelector(`label[for="${CSS.escape(el.id)}"]`); if (l) return l; }
        const wrap = el.closest('label');
        if (wrap) return wrap;
        if (el.type === 'radio') { const fs = el.closest('fieldset'); const lg = fs?.querySelector('legend'); if (lg) return lg; }
        const box = el.closest('.form-group, .field, .mb-3, .wp-field, .as-field, .ld-field, [data-field]');
        return box?.querySelector('label, .form-label, legend') || null;
    }
    function labelText(el) {
        let text = el.getAttribute('data-fv-label');
        if (!text) {
            const l = labelElement(el);
            if (l) {
                const copy = l.cloneNode(true);
                copy.querySelectorAll('input, select, textarea, button, .fv-msg, .fv-req, small, .hint, [aria-hidden="true"], .field-validation-error, .field-validation-valid').forEach(n => n.remove());
                text = copy.textContent;
            }
        }
        if (!cleanLabel(text)) text = el.getAttribute('aria-label') || el.getAttribute('title') || el.placeholder || '';
        if (!cleanLabel(text)) text = (el.name || 'This field').replace(/^.*\./, '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ');
        text = cleanLabel(text);
        if (text.length > 60) text = text.slice(0, 57).replace(/\s+\S*$/, '') + '…';
        const lower = /^[A-Z]{2,}/.test(text) ? text : text.charAt(0).toLowerCase() + text.slice(1);
        return { text: text.charAt(0).toUpperCase() + text.slice(1), lower, ar: el.getAttribute('data-fv-label-ar') || AR_LABELS[text.toLowerCase()] || null };
    }

    function markLabel(el, on) {
        const l = labelElement(el);
        if (!l || l.tagName === 'LEGEND' && el.type !== 'radio') return;
        let star = l.querySelector(':scope > .fv-req');
        if (!on) { if (star && star.dataset.fvAuto) star.remove(); return; }
        if (star || /\*/.test(l.firstChild?.textContent || '') || l.querySelector('.required, .req, .text-danger:not(.field-validation-error):not(.field-validation-valid)')) return;
        if (/\((optional|اختياري)/i.test(l.textContent)) return;
        star = document.createElement('span');
        star.className = 'fv-req'; star.dataset.fvAuto = '1'; star.setAttribute('aria-hidden', 'true'); star.textContent = '*';
        // For a wrapping <label>Text<input></label>, the star goes right after the text.
        const firstCtl = l.querySelector(FIELD);
        if (firstCtl && l.contains(firstCtl)) {
            let node = l.firstChild;
            while (node && node.nodeType === 3 && !node.textContent.trim()) node = node.nextSibling;
            if (node && node.nodeType === 3) node.after(star);
            else if (node && node !== firstCtl && !node.matches?.(FIELD)) node.after(star);
            else l.prepend(star);
        } else l.append(star);
    }

    // ---------- messages ----------
    function msgHost(el) {
        const form = el.form;
        const name = el.name;
        if (form && name) {
            const server = form.querySelector(`[data-valmsg-for="${CSS.escape(name)}"]`);
            if (server) return server;
        }
        if (el.type === 'radio' || el.type === 'checkbox' && el._fvGroup) {
            const group = el._fvGroup || el.closest('fieldset, [role=radiogroup], .radio-group, .wp-radios') || labelElement(el)?.parentElement;
            if (group) {
                let m = group.querySelector(':scope > .fv-msg[data-fv-for]');
                if (!m) { m = newMsg(el); group.append(m); }
                return m;
            }
        }
        let m = el._fvMsg;
        if (m && m.isConnected) return m;
        m = newMsg(el);
        // Keep the message under the field, after helper widgets glued to it (password toggles, unit suffixes).
        let anchor = el;
        const next = el.nextElementSibling;
        if (next && next.matches('.auth-password-toggle, .input-group-text, .wp-unit, [data-fv-anchor]')) anchor = next;
        if (el.parentElement?.matches('.input-group, .as-password, .password-wrap, .ld-password')) anchor = el.parentElement;
        if (el.type === 'checkbox') { const l = el.closest('label'); if (l) anchor = l; }
        anchor.after(m);
        el._fvMsg = m;
        return m;
    }
    function newMsg(el) {
        const m = document.createElement('span');
        m.className = 'fv-msg'; m.setAttribute('data-fv-for', el.name || el.id || '');
        m.id = 'fv-' + Math.random().toString(36).slice(2, 9);
        m.setAttribute('role', 'alert'); m.hidden = true;
        return m;
    }

    function setError(el, message) {
        const targets = el.type === 'radio' && el.form ? el.form.querySelectorAll(`input[type=radio][name="${CSS.escape(el.name)}"]`) : [el];
        const host = msgHost(el);
        targets.forEach(t => {
            t.classList.add('fv-invalid', 'input-validation-error'); t.classList.remove('fv-valid');
            t.setAttribute('aria-invalid', 'true');
            const ids = (t.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
            if (!ids.includes(host.id)) { if (!host.id) host.id = 'fv-' + Math.random().toString(36).slice(2, 9); ids.push(host.id); t.setAttribute('aria-describedby', ids.join(' ')); }
        });
        el.closest('.form-group, .field, .mb-3, .as-field, .ld-field, label')?.classList.add('fv-has-error');
        host.textContent = message;
        host.hidden = false;
        host.classList.add('fv-msg', 'field-validation-error'); host.classList.remove('field-validation-valid');
    }
    function clearError(el) {
        const targets = el.type === 'radio' && el.form ? el.form.querySelectorAll(`input[type=radio][name="${CSS.escape(el.name)}"]`) : [el];
        targets.forEach(t => { t.classList.remove('fv-invalid', 'input-validation-error'); t.removeAttribute('aria-invalid'); });
        el.closest('.fv-has-error')?.classList.remove('fv-has-error');
        const host = el.form && el.name ? el.form.querySelector(`[data-valmsg-for="${CSS.escape(el.name)}"]`) : null;
        const m = host || el._fvMsg || (el.type === 'radio' ? el.closest('fieldset, [role=radiogroup], .radio-group, .wp-radios')?.querySelector(':scope > .fv-msg') : null);
        if (m) { m.textContent = ''; m.classList.remove('field-validation-error'); m.classList.add('field-validation-valid'); if (!host) m.hidden = true; }
    }

    // ---------- rules ----------
    function num(v) { const n = Number(String(v).replace(/,/g, '')); return Number.isFinite(n) ? n : NaN; }
    function fmtDate(v, type) {
        const d = new Date(v);
        if (isNaN(d)) return v;
        const opts = type === 'date' ? { dateStyle: 'medium' } : { dateStyle: 'medium', timeStyle: 'short' };
        try { return d.toLocaleString(AR ? 'ar-JO' : 'en-GB', opts); } catch { return d.toLocaleString(); }
    }
    function acceptLabel(accept) {
        const exts = accept.split(',').map(x => x.trim()).filter(Boolean).map(x => {
            if (x.startsWith('.')) return x.slice(1).toUpperCase();
            if (x === 'application/pdf') return 'PDF';
            if (x.startsWith('image/')) return x === 'image/*' ? (AR ? 'صورة' : 'image') : x.slice(6).toUpperCase();
            return x.split('/').pop().toUpperCase();
        });
        const uniq = Array.from(new Set(exts));
        if (uniq.length === 1) return uniq[0];
        return uniq.slice(0, -1).join(', ') + (AR ? ' أو ' : ' or ') + uniq[uniq.length - 1];
    }
    function fileMatches(file, accept) {
        const name = file.name.toLowerCase(), type = (file.type || '').toLowerCase();
        return accept.split(',').map(x => x.trim().toLowerCase()).filter(Boolean).some(a =>
            a.startsWith('.') ? name.endsWith(a) : a.endsWith('/*') ? type.startsWith(a.slice(0, -1)) : type === a);
    }
    function custom(el, key) {
        const own = el.getAttribute(AR ? 'data-fv-msg-ar' : 'data-fv-msg');
        if (own) return own;
        if (AR) return null;
        const v = el.getAttribute('data-val-' + key);
        // Keep author-written annotation messages; replace the framework defaults ("The X field is required.").
        return v && !/^The .+ (field|must)/.test(v) && !/^The field /.test(v) ? v : null;
    }

    function check(el, form, submitter) {
        const label = labelText(el);
        const type = (el.type || '').toLowerCase();
        const required = isRequired(el, form, submitter);

        if (type === 'radio') {
            const group = form.querySelectorAll(`input[type=radio][name="${CSS.escape(el.name)}"]`);
            if (required && !Array.from(group).some(r => r.checked)) return custom(el, 'required') || T.select(label);
            return null;
        }
        if (type === 'checkbox') return required && !el.checked ? (custom(el, 'required') || T.check(label)) : null;
        if (type === 'file') {
            const files = Array.from(el.files || []);
            if (!files.length) return required ? (custom(el, 'required') || T.file(label)) : null;
            const accept = el.getAttribute('accept');
            if (accept && files.some(f => !fileMatches(f, accept))) return T.fileType(acceptLabel(accept));
            const mb = num(el.getAttribute('data-fv-max-mb') || (accept ? 20 : NaN));
            if (Number.isFinite(mb) && files.some(f => f.size > mb * 1024 * 1024)) return T.fileSize(mb);
            if (files.some(f => f.size === 0)) return T.fileType(accept ? acceptLabel(accept) : (AR ? 'غير فارغ' : 'non-empty'));
            return null;
        }

        if (type === 'number' && el.validity && el.validity.badInput) return T.number(label);
        // Page scripts can still add their own rule with setCustomValidity (e.g. one person per committee seat).
        if (el.validity && el.validity.customError && el.validationMessage) return el.validationMessage;
        const raw = el.value ?? '';
        const value = el.tagName === 'SELECT' ? raw : raw.trim();
        if (!value) {
            if (!required) return null;
            if (raw.length && el.tagName !== 'SELECT') return T.blank(label);
            return custom(el, 'required') || (el.tagName === 'SELECT' ? T.select(label) : T.required(label));
        }

        if (type === 'email' || el.hasAttribute('data-val-email')) { if (!EMAIL.test(value)) return custom(el, 'email') || T.email(); }
        if (type === 'tel' || el.hasAttribute('data-val-phone')) { if (!PHONE.test(value) || value.replace(/\D/g, '').length < 6) return custom(el, 'phone') || T.phone(); }
        if (type === 'url' || el.hasAttribute('data-val-url')) { try { const u = new URL(value); if (!/^https?:$/.test(u.protocol)) throw 0; } catch { return custom(el, 'url') || T.url(); } }

        // Length
        const minLen = num(el.getAttribute('minlength') ?? el.getAttribute('data-val-length-min') ?? el.getAttribute('data-val-minlength-min'));
        const maxLen = num(el.getAttribute('maxlength') ?? el.getAttribute('data-val-length-max') ?? el.getAttribute('data-val-maxlength-max'));
        if (Number.isFinite(minLen) && minLen > 0 && value.length < minLen) return custom(el, 'length') || custom(el, 'minlength') || T.minLen(label, minLen);
        if (Number.isFinite(maxLen) && maxLen > 0 && value.length > maxLen) return custom(el, 'length') || custom(el, 'maxlength') || T.maxLen(label, maxLen);

        // Pattern
        const pattern = el.getAttribute('pattern') || el.getAttribute('data-val-regex-pattern');
        if (pattern) { try { if (!new RegExp('^(?:' + pattern + ')$', 'u').test(value)) return custom(el, 'regex') || T.pattern(label); } catch { /* invalid pattern: ignore */ } }

        // Numbers
        const isNumber = type === 'number' || el.hasAttribute('data-val-number') || el.hasAttribute('data-val-range');
        if (isNumber && el.tagName !== 'SELECT') {
            const n = num(value);
            if (Number.isNaN(n)) return custom(el, 'number') || T.number(label);
            const step = el.getAttribute('step');
            if (step && step !== 'any' && Number(step) >= 1 && !Number.isInteger(n)) return T.whole(label);
            const min = num(el.getAttribute('min') ?? el.getAttribute('data-val-range-min'));
            const max = num(el.getAttribute('max') ?? el.getAttribute('data-val-range-max'));
            if (Number.isFinite(min) && Number.isFinite(max) && (n < min || n > max)) return custom(el, 'range') || T.range(label, min, max);
            if (Number.isFinite(min) && n < min) return custom(el, 'range') || T.min(label, min);
            if (Number.isFinite(max) && n > max) return custom(el, 'range') || T.max(label, max);
        }

        // Dates
        if (/^(date|datetime-local|month|time)$/.test(type)) {
            if (el.validity && el.validity.badInput) return T.date();
            const d = type === 'time' ? null : new Date(value);
            if (d && isNaN(d)) return T.date();
            const min = el.getAttribute('min'), max = el.getAttribute('max');
            if (min && value < min) return T.dateMin(label, fmtDate(min, type));
            if (max && value > max) return T.dateMax(label, fmtDate(max, type));
            if (el.hasAttribute('data-fv-future') && d && d <= new Date()) return T.future(label);
        }

        // Later than another field
        const afterName = el.getAttribute('data-fv-after');
        if (afterName) {
            const other = control(form, afterName);
            const otherEl = other instanceof RadioNodeList ? other[0] : other;
            if (otherEl && otherEl.value) {
                const a = /^(date|datetime-local)$/.test(type) ? new Date(value) : num(value);
                const b = /^(date|datetime-local)$/.test(otherEl.type) ? new Date(otherEl.value) : num(otherEl.value);
                if (!(a > b)) return custom(el, 'after') || T.after(label, labelText(otherEl));
            }
        }

        // Equal to another field (password confirmation)
        const eq = el.getAttribute('data-val-equalto-other');
        if (eq) {
            const other = control(form, eq);
            if (other && !(other instanceof RadioNodeList) && other.value !== raw) return custom(el, 'equalto') || T.equal(label, labelText(other));
        }
        return null;
    }

    function checkGroup(box, form) {
        if (!isGroupActive(box)) return null;
        const boxes = Array.from(box.querySelectorAll('input[type=checkbox]')).filter(c => !c.disabled);
        if (!boxes.length || boxes.some(c => c.checked)) return null;
        const own = box.getAttribute(AR ? 'data-fv-msg-ar' : 'data-fv-msg');
        if (own) return own;
        const text = box.getAttribute('data-fv-label') || (AR ? 'عنصر' : 'item');
        return T.group({ text, lower: text.toLowerCase(), ar: AR_LABELS[text.toLowerCase()] || null });
    }
    function isGroupActive(box) {
        for (let n = box; n && n.nodeType === 1; n = n.parentElement) {
            if (n.hidden && n.tagName !== 'DETAILS') return false;
            if (getComputedStyle(n).display === 'none') return false;
            if (n.tagName === 'FORM') break;
        }
        return !box.closest('fieldset[disabled]');
    }
    function groupMsg(box) {
        let m = box._fvMsg;
        if (!m || !m.isConnected) { m = newMsg({ name: box.getAttribute('data-fv-label') || 'group' }); box.after(m); box._fvMsg = m; }
        return m;
    }

    // ---------- form level ----------
    function fields(form) {
        return Array.from(form.elements).filter(el => el.matches(FIELD));
    }

    function validateField(el, form, submitter) {
        if (!isActive(el)) { clearError(el); return true; }
        const message = check(el, form, submitter);
        if (message) { setError(el, message); return false; }
        clearError(el);
        return true;
    }

    function validateForm(form, submitter) {
        let first = null;
        const seenRadio = new Set();
        for (const el of fields(form)) {
            if (el.type === 'radio') { if (seenRadio.has(el.name)) continue; seenRadio.add(el.name); }
            if (!validateField(el, form, submitter) && !first) first = el;
        }
        form.querySelectorAll('[data-fv-group-required]').forEach(box => {
            const message = checkGroup(box, form);
            const m = groupMsg(box);
            box.classList.toggle('fv-group-invalid', !!message);
            m.textContent = message || ''; m.hidden = !message;
            if (message && !first) first = box.querySelector('input[type=checkbox]:not(:disabled)');
        });
        if (first) focusField(first);
        return !first;
    }

    function focusField(el) {
        for (let n = el.parentElement; n; n = n.parentElement) if (n.tagName === 'DETAILS' && !n.open) n.open = true;
        const target = el.type === 'radio' ? (el.form?.querySelector(`input[type=radio][name="${CSS.escape(el.name)}"]`) || el) : el;
        const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
        try { target.scrollIntoView({ block: 'center', behavior: reduce ? 'auto' : 'smooth' }); } catch { target.scrollIntoView(); }
        setTimeout(() => { try { target.focus({ preventScroll: true }); } catch { target.focus(); } }, reduce ? 0 : 250);
    }

    function updateStars(form, submitter) {
        fields(form).forEach(el => {
            if (el.type === 'hidden' || el.type === 'checkbox' && !el.required) return;
            markLabel(el, isActive(el) && isRequired(el, form, submitter));
        });
        form.querySelectorAll('[data-fv-group-required]').forEach(box => {
            const lbl = box.querySelector(':scope > legend, :scope > .fv-group-label');
            if (lbl && !lbl.querySelector('.fv-req')) { const s = document.createElement('span'); s.className = 'fv-req'; s.dataset.fvAuto = '1'; s.setAttribute('aria-hidden', 'true'); s.textContent = '*'; lbl.append(s); }
        });
    }

    function shouldManage(form) {
        if (!(form instanceof HTMLFormElement) || form.hasAttribute('data-fv-skip')) return false;
        return (form.getAttribute('method') || 'get').toLowerCase() === 'post' || form.hasAttribute('data-fv');
    }

    function enhance(form) {
        if (managed.has(form) || !shouldManage(form)) return;
        managed.add(form);
        form.setAttribute('novalidate', '');
        form.classList.add('fv-form');
        updateStars(form);

        // Server-side errors already rendered (ModelState): style them the same way and reveal the first one.
        let firstServer = null;
        form.querySelectorAll('.input-validation-error').forEach(el => {
            if (!el.matches(FIELD)) return;
            el.classList.add('fv-invalid'); el.setAttribute('aria-invalid', 'true');
            const m = el.name ? form.querySelector(`[data-valmsg-for="${CSS.escape(el.name)}"]`) : null;
            if (m) { m.classList.add('fv-msg'); if (m.id) el.setAttribute('aria-describedby', m.id); }
            if (!firstServer) firstServer = el;
        });
        form.querySelectorAll('.field-validation-error').forEach(m => m.classList.add('fv-msg'));
        if (firstServer && !document.querySelector('[autofocus]')) setTimeout(() => focusField(firstServer), 60);

        const live = e => {
            const el = e.target;
            if (!el.matches || !el.matches(FIELD)) return;
            if (el.type === 'checkbox') {
                const box = el.closest('[data-fv-group-required]');
                if (box && box.classList.contains('fv-group-invalid') && !checkGroup(box, form)) { box.classList.remove('fv-group-invalid'); const m = groupMsg(box); m.hidden = true; m.textContent = ''; }
            }
            if (el.classList.contains('fv-invalid') || (e.type === 'change' && touched.has(form))) validateField(el, form);
            // Fields that depend on this one (later-than dates, required-when, equal-to).
            form.querySelectorAll('[data-fv-after], [data-fv-required-when], [data-val-equalto-other]').forEach(dep => {
                if (dep !== el && dep.classList.contains('fv-invalid')) validateField(dep, form);
            });
            if (e.type === 'change') updateStars(form);
        };
        form.addEventListener('input', live);
        form.addEventListener('change', live);
        form.addEventListener('focusout', e => {
            const el = e.target;
            // Format problems are reported when leaving a filled field; empty mandatory fields wait for submit.
            if (el.matches && el.matches(FIELD) && el.type !== 'radio' && el.type !== 'checkbox' && el.value && el.value.trim()) validateField(el, form);
        });
        form.addEventListener('reset', () => setTimeout(() => fields(form).forEach(clearError), 0));
    }

    // Capture phase on the document runs before every form's own submit handler (including the workflow popup's AJAX
    // submit and jQuery handlers), so an invalid form never reaches the network.
    document.addEventListener('submit', e => {
        const form = e.target;
        if (!shouldManage(form)) return;
        enhance(form);
        const submitter = e.submitter;
        if (submitter && (submitter.hasAttribute('formnovalidate') || submitter.hasAttribute('data-fv-skip'))) return;
        touched.add(form);
        updateStars(form, submitter);
        if (!validateForm(form, submitter)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            form.dispatchEvent(new CustomEvent('fv:invalid', { bubbles: true }));
        }
    }, true);

    function scan(root) {
        if (root.nodeType !== 1) return;
        if (root.tagName === 'FORM') enhance(root);
        root.querySelectorAll?.('form').forEach(enhance);
    }
    function start() {
        scan(document.body);
        new MutationObserver(list => list.forEach(r => r.addedNodes.forEach(scan))).observe(document.body, { childList: true, subtree: true });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();

    window.OrangeValidation = { validate: (form, submitter) => { enhance(form); return validateForm(form, submitter); }, validateField: el => validateField(el, el.form), enhance, arabic: AR };
})();
