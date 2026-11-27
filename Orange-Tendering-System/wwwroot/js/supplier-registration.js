// The supplier registration wizard (Areas/Identity/Pages/Account/Register.cshtml).
// Four steps on ONE form: 0 Contact, 1 Company, 2 Documents, 3 Review. Only one step is visible at a time.
// "Continue" checks the fields of the visible step. "Send verification code" sends the whole form to
// Register.cshtml.cs (OnPostAsync), which checks everything again.
//
// The illustration (wwwroot/js/auth-studio/register-page.js) receives a harmless description of the form through
// publishRegistrationState(): never the password, the email address, the registration / tax number or a document.

var REQUIRED_FIELDS = [
    'Input.FullName', 'Input.Email', 'Input.Password', 'Input.ConfirmPassword',
    'Input.CompanyName', 'Input.Category', 'Input.RegistrationNumber',
    'Input.CompanyEmail', 'Input.CompanyPhone', 'Input.CompanyAddress'
];
var FILE_FIELDS = ['CommercialRegistrationCertificate', 'TaxCertificate', 'LegalDocuments'];
// The fields the illustration reacts to, with the short names it knows.
var ILLUSTRATION_NAMES = {
    'Input.FullName': 'name', 'Input.Email': 'email', 'Input.Password': 'password',
    'Input.ConfirmPassword': 'confirm', 'Input.CompanyName': 'company', 'Input.Category': 'category'
};
var REGISTRATION_EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
var MAX_FILE_BYTES = 10 * 1024 * 1024;

var currentStep = 0;                                  // the visible step (0 to 3)
var stepWasChecked = [false, false, false, false];    // "Continue" was pressed on this step
var registrationSending = false;                      // the form is being sent
var registrationSubmitState = 'idle';                 // 'idle', 'sending', 'accepted' or 'failed' (for the illustration)
var registrationFocus = null;                         // the focused field's short name (for the illustration)

// =====================================================================================================
// Small helpers
// =====================================================================================================

function registrationForm() {
    return document.getElementById('registerForm');
}

function registrationSteps() {
    return registrationForm().querySelectorAll('.as-step');
}

function registrationField(name) {
    return registrationForm().elements.namedItem(name);
}

function registrationValue(name) {
    var field = registrationField(name);
    if (!field || !field.value) {
        return '';
    }
    return field.value.trim();
}

function rawValue(name) {
    var field = registrationField(name);
    if (!field || !field.value) {
        return '';
    }
    return field.value;
}

function selectedFiles(name) {
    var field = registrationField(name);
    var files = [];
    if (field && field.files) {
        for (var i = 0; i < field.files.length; i++) {
            files.push(field.files[i]);
        }
    }
    return files;
}

function allSelectedFiles() {
    var files = [];
    for (var i = 0; i < FILE_FIELDS.length; i++) {
        files = files.concat(selectedFiles(FILE_FIELDS[i]));
    }
    return files;
}

function messageSlot(name) {
    return registrationForm().querySelector('[data-valmsg-for="' + name + '"]');
}

// The visible name of a field from its <label> (the hint inside <small> is not part of it).
function fieldLabel(input) {
    var label = registrationForm().querySelector('label[for="' + input.id + '"]');
    if (!label) {
        return 'This field';
    }
    var text = '';
    for (var i = 0; i < label.childNodes.length; i++) {
        if (label.childNodes[i].nodeType === Node.TEXT_NODE) {
            text = text + ' ' + label.childNodes[i].textContent;
        }
    }
    text = text.trim();
    if (!text) {
        return 'This field';
    }
    return text;
}

// =====================================================================================================
// Messages
// =====================================================================================================

function showRegistrationError(message) {
    var summary = document.getElementById('registrationErrors');
    summary.className = 'as-summary is-error';
    var list = document.createElement('ul');
    var item = document.createElement('li');
    item.textContent = message;
    list.appendChild(item);
    summary.replaceChildren(list);
    summary.scrollIntoView({ block: 'nearest' });
}

function clearRegistrationError() {
    var summary = document.getElementById('registrationErrors');
    summary.className = 'as-summary';
    summary.replaceChildren();
}

// The red text under one field ('' removes it).
function setRegistrationFieldError(name, message) {
    var input = registrationField(name);
    var slot = messageSlot(name);
    if (slot) {
        slot.textContent = message || '';
        slot.classList.toggle('field-validation-error', Boolean(message));
        slot.classList.toggle('field-validation-valid', !message);
    }
    if (input && input.classList) {
        if (message) {
            input.classList.add('input-validation-error');
            input.setAttribute('aria-invalid', 'true');
        } else {
            input.classList.remove('input-validation-error');
            input.removeAttribute('aria-invalid');
        }
    }
}

// =====================================================================================================
// Validation (the same rules as the server: Register.cshtml.cs and Identity's password options)
// =====================================================================================================

// The password rules that are not met yet, for example ['a number', 'a symbol'].
function missingPasswordRules() {
    var password = rawValue('Input.Password');
    var missing = [];
    if (password.length < 8) {
        missing.push('at least 8 characters');
    }
    if (!/[a-z]/.test(password)) {
        missing.push('a lowercase letter');
    }
    if (!/[A-Z]/.test(password)) {
        missing.push('an uppercase letter');
    }
    if (!/\d/.test(password)) {
        missing.push('a number');
    }
    if (!/[^A-Za-z0-9]/.test(password)) {
        missing.push('a symbol');
    }
    return missing;
}

// The problem with one file input, or '' when it is fine.
function fileProblem(name) {
    var files = selectedFiles(name);
    if (files.length === 0) {
        if (name === 'CommercialRegistrationCertificate') {
            return 'Commercial registration certificate is required.';
        }
        if (name === 'LegalDocuments') {
            return 'At least one business license or legal document is required.';
        }
        if (registrationValue('Input.TaxNumber')) {
            return 'Tax certificate is required when a tax number is provided.';
        }
        return '';
    }
    for (var i = 0; i < files.length; i++) {
        if (!/\.(pdf|png|jpe?g)$/i.test(files[i].name)) {
            return files[i].name + ' is not a PDF, PNG or JPEG file.';
        }
        if (files[i].size > MAX_FILE_BYTES) {
            return files[i].name + ' is larger than 10 MB.';
        }
        if (files[i].size === 0) {
            return files[i].name + ' is empty.';
        }
    }
    if (name === 'LegalDocuments' && allSelectedFiles().length > 7) {
        return 'Upload no more than seven documents.';
    }
    return '';
}

// The problem with one field, or '' when it is fine.
function fieldProblem(name) {
    var input = registrationField(name);
    if (!input) {
        return '';
    }
    if (FILE_FIELDS.indexOf(name) >= 0) {
        return fileProblem(name);
    }

    var text = (input.value || '').trim();
    if (name === 'Input.Password') {
        if (!input.value) {
            return 'Password is required.';
        }
        var missing = missingPasswordRules();
        if (missing.length > 0) {
            return 'Password still needs ' + missing.join(', ') + '.';
        }
        return '';
    }
    if (name === 'Input.ConfirmPassword') {
        if (!input.value) {
            return 'Enter the password again to confirm it.';
        }
        if (input.value !== rawValue('Input.Password')) {
            return 'The two passwords do not match.';
        }
        return '';
    }
    if (name === 'Input.Category') {
        if (text) {
            return '';
        }
        return 'Choose your main supply category.';
    }
    if (!text) {
        if (REQUIRED_FIELDS.indexOf(name) >= 0) {
            return fieldLabel(input) + ' is required.';
        }
        return '';
    }
    if (name === 'Input.Email' || name === 'Input.CompanyEmail') {
        if (REGISTRATION_EMAIL_PATTERN.test(text)) {
            return '';
        }
        return 'Enter a valid email address, for example name@company.com.';
    }
    // The server's message for a pattern, for example the telephone: exactly 10 digits.
    if (input.dataset.valRegex && input.validity.patternMismatch) {
        return input.dataset.valRegex;
    }
    if (input.checkValidity()) {
        return '';
    }
    return 'Check ' + fieldLabel(input).toLowerCase() + '.';
}

// Checks one field and shows or removes its message. True when it is fine.
function checkRegistrationField(name) {
    var problem = fieldProblem(name);
    setRegistrationFieldError(name, problem);
    return problem === '';
}

// The names of the fields inside one step.
function fieldNamesInStep(index) {
    var inputs = registrationSteps()[index].querySelectorAll('input,select');
    var names = [];
    for (var i = 0; i < inputs.length; i++) {
        if (!inputs[i].disabled && inputs[i].type !== 'hidden' && inputs[i].name) {
            names.push(inputs[i].name);
        }
    }
    return names;
}

// Checks every field of one step; shows that step and focuses the first wrong field when one fails.
function validateRegistrationStep(index) {
    stepWasChecked[index] = true;
    var names = fieldNamesInStep(index);
    var firstInvalid = null;
    for (var i = 0; i < names.length; i++) {
        if (!checkRegistrationField(names[i]) && firstInvalid === null) {
            firstInvalid = registrationField(names[i]);
        }
    }
    if (index === 2) {
        updateFileStates();
    }

    if (firstInvalid !== null) {
        if (registrationSteps()[index].hidden) {
            goToRegistrationStep(index, false);
        }
        firstInvalid.focus();
        publishRegistrationState();
        return false;
    }
    clearRegistrationError();
    return true;
}

// =====================================================================================================
// Password rules list, document lines
// =====================================================================================================

// Ticks the rules under the password field.
function updatePasswordRules() {
    var password = rawValue('Input.Password');
    var met = [
        password.length >= 8,
        /[a-z]/.test(password),
        /[A-Z]/.test(password),
        /\d/.test(password),
        /[^A-Za-z0-9]/.test(password)
    ];
    var items = registrationForm().querySelectorAll('#passwordRules li');
    for (var i = 0; i < items.length; i++) {
        items[i].classList.toggle('is-met', met[i]);
        var screenReaderText = items[i].querySelector('.sr-only');
        if (screenReaderText) {
            if (met[i]) {
                screenReaderText.textContent = '— met';
            } else {
                screenReaderText.textContent = '— not met';
            }
        }
    }
}

// 'none', 'accepted' or 'rejected'. "Accepted" only means it passed the checks in the browser:
// the server checks every file again and an administrator reviews the documents later.
function fileState(name) {
    if (selectedFiles(name).length === 0) {
        return 'none';
    }
    if (fileProblem(name)) {
        return 'rejected';
    }
    return 'accepted';
}

function updateFileStates() {
    for (var i = 0; i < FILE_FIELDS.length; i++) {
        var name = FILE_FIELDS[i];
        var line = registrationForm().querySelector('[data-file-state="' + name + '"]');
        if (!line) {
            continue;
        }
        var files = selectedFiles(name);
        var state = fileState(name);
        line.classList.toggle('is-accepted', state === 'accepted');
        line.classList.toggle('is-rejected', state === 'rejected');
        if (state === 'accepted') {
            var what = files.length + ' files';
            if (files.length === 1) {
                what = files[0].name;
            }
            line.textContent = what + ' selected. Checked again on the server when you submit.';
        } else {
            line.textContent = '';
        }
    }
}

// =====================================================================================================
// What the illustration is told
// =====================================================================================================

function registrationEmailState() {
    var text = registrationValue('Input.Email');
    if (!text) {
        return 'empty';
    }
    if (REGISTRATION_EMAIL_PATTERN.test(text)) {
        return 'valid';
    }
    if (registrationFocus === 'email' && !stepWasChecked[0]) {
        return 'typing';
    }
    return 'invalid';
}

function confirmPasswordState() {
    var password = rawValue('Input.Password');
    var confirm = rawValue('Input.ConfirmPassword');
    if (!confirm) {
        return 'empty';
    }
    if (confirm === password) {
        return 'match';
    }
    if (registrationFocus === 'confirm' && password.indexOf(confirm) === 0 && !stepWasChecked[0]) {
        return 'partial';
    }
    return 'mismatch';
}

function companyStepReady() {
    var names = fieldNamesInStep(1);
    for (var i = 0; i < names.length; i++) {
        if (fieldProblem(names[i])) {
            return false;
        }
    }
    return true;
}

function publishRegistrationState() {
    var state = {
        step: currentStep,
        focus: registrationFocus,
        name: registrationValue('Input.FullName'),
        email: registrationEmailState(),
        hasPassword: rawValue('Input.Password').length > 0,
        rules: 5 - missingPasswordRules().length,
        confirm: confirmPasswordState(),
        company: registrationValue('Input.CompanyName'),
        category: registrationValue('Input.Category'),
        companyReady: companyStepReady(),
        documents: {
            registration: fileState('CommercialRegistrationCertificate'),
            tax: fileState('TaxCertificate'),
            legal: fileState('LegalDocuments')
        },
        submit: registrationSubmitState
    };
    var studio = document.getElementById('authStudio');
    studio.authState = state; // the illustration starts after this script and reads the last state from here
    studio.dispatchEvent(new CustomEvent('authstudio:state', { detail: state }));
}

// =====================================================================================================
// Steps
// =====================================================================================================

// Fills the "Review and submit" list.
function updateReview() {
    var fileNames = [];
    var files = allSelectedFiles();
    for (var i = 0; i < files.length; i++) {
        fileNames.push(files[i].name);
    }
    var rows = [
        ['Contact', registrationValue('Input.FullName')],
        ['Email', registrationValue('Input.Email')],
        ['Company', registrationValue('Input.CompanyName')],
        ['Category', registrationValue('Input.Category')],
        ['Registration', registrationValue('Input.RegistrationNumber')],
        ['Company email', registrationValue('Input.CompanyEmail')],
        ['Telephone', registrationValue('Input.CompanyPhone')],
        ['Address', registrationValue('Input.CompanyAddress')],
        ['Documents', fileNames.join(', ')]
    ];

    var review = document.getElementById('registrationReview');
    review.replaceChildren();
    for (var r = 0; r < rows.length; r++) {
        var term = document.createElement('dt');
        var value = document.createElement('dd');
        term.textContent = rows[r][0];
        value.textContent = rows[r][1] || '—';
        review.appendChild(term);
        review.appendChild(value);
    }
}

// Shows step "index" and hides the others; updates the step list and the buttons.
function goToRegistrationStep(index, moveFocus) {
    var steps = registrationSteps();
    var lastStep = steps.length - 1;
    currentStep = index;
    document.getElementById('authStudio').dataset.step = String(index);

    for (var i = 0; i < steps.length; i++) {
        steps[i].hidden = i !== index;
    }

    var progressItems = document.querySelectorAll('#authStudio .as-steps li');
    for (var p = 0; p < progressItems.length; p++) {
        progressItems[p].classList.toggle('is-current', p === index);
        progressItems[p].classList.toggle('is-done', p < index);
        if (p === index) {
            progressItems[p].setAttribute('aria-current', 'step');
        } else {
            progressItems[p].removeAttribute('aria-current');
        }
    }

    document.getElementById('registrationBack').hidden = index === 0;
    document.getElementById('registrationNext').hidden = index === lastStep;
    document.getElementById('registerSubmit').hidden = index !== lastStep;
    if (index === lastStep) {
        updateReview();
    }

    if (moveFocus) {
        var first = steps[index].querySelector('input,select');
        if (!first) {
            first = steps[index].querySelector('legend');
            first.setAttribute('tabindex', '-1');
        }
        first.focus({ preventScroll: true });
        steps[index].scrollIntoView({ block: 'nearest' });
    }
    publishRegistrationState();
}

function nextRegistrationStep() {
    if (validateRegistrationStep(currentStep)) {
        goToRegistrationStep(currentStep + 1, true);
    }
}

function previousRegistrationStep() {
    clearRegistrationError();
    goToRegistrationStep(currentStep - 1, true);
}

// =====================================================================================================
// Live checks while typing
// =====================================================================================================

function onRegistrationInput(event) {
    var name = event.target.name;
    if (name === 'Input.Password') {
        updatePasswordRules();
    }
    // A message that is already showing follows the field; no new message appears while typing.
    var slot = messageSlot(name);
    if (name && slot && slot.textContent) {
        checkRegistrationField(name);
    }
    var confirmSlot = messageSlot('Input.ConfirmPassword');
    if (name === 'Input.Password' && confirmSlot && confirmSlot.textContent) {
        checkRegistrationField('Input.ConfirmPassword');
    }
    publishRegistrationState();
}

function onRegistrationChange(event) {
    var name = event.target.name;
    if (FILE_FIELDS.indexOf(name) >= 0) {
        // A wrong file is reported beside its field at once; a missing one only on Continue.
        if (selectedFiles(name).length > 0) {
            setRegistrationFieldError(name, fileProblem(name));
        } else {
            setRegistrationFieldError(name, '');
        }
        var legalSlot = messageSlot('LegalDocuments');
        if (name !== 'LegalDocuments' && legalSlot && legalSlot.textContent) {
            checkRegistrationField('LegalDocuments');
        }
        updateFileStates();
    } else if (name === 'Input.Category' && event.target.value) {
        setRegistrationFieldError(name, '');
    } else if (name === 'Input.TaxNumber') {
        var taxSlot = messageSlot('TaxCertificate');
        if (taxSlot && taxSlot.textContent) {
            checkRegistrationField('TaxCertificate');
        }
    }
    publishRegistrationState();
}

function onRegistrationFocusIn(event) {
    registrationFocus = ILLUSTRATION_NAMES[event.target.name] || null;
    publishRegistrationState();
}

function onRegistrationFocusOut(event) {
    var name = event.target.name;
    registrationFocus = null;
    // Leaving a field with content checks it; empty fields are reported only on Continue.
    if (name && FILE_FIELDS.indexOf(name) < 0 && (event.target.value || stepWasChecked[currentStep])) {
        checkRegistrationField(name);
    }
    publishRegistrationState();
}

// =====================================================================================================
// Sending the form
// =====================================================================================================

// The server answered with the page again (validation errors). Its messages are copied into this page,
// so the files the person already chose stay selected (a reloaded page would lose them).
function showServerValidation(html) {
    var returnedForm = new DOMParser().parseFromString(html, 'text/html').getElementById('registerForm');
    if (!returnedForm) {
        showRegistrationError('Registration could not be completed. Please try again.');
        return;
    }

    // 1. The summary at the top (for example "An account or company with these details already exists").
    var summary = document.getElementById('registrationErrors');
    var returnedSummaries = returnedForm.querySelectorAll('.validation-summary-errors');
    var returnedSummary = null;
    for (var i = 0; i < returnedSummaries.length; i++) {
        if (returnedSummaries[i].textContent.trim()) {
            returnedSummary = returnedSummaries[i];
            break;
        }
    }
    if (returnedSummary) {
        summary.className = 'as-summary is-error';
        summary.innerHTML = returnedSummary.innerHTML; // Razor already HTML-encoded these messages
    } else {
        clearRegistrationError();
    }

    // 2. The message under each field.
    var firstInvalid = null;
    var slots = registrationForm().querySelectorAll('[data-valmsg-for]');
    for (var s = 0; s < slots.length; s++) {
        var name = slots[s].getAttribute('data-valmsg-for');
        var returnedSlot = returnedForm.querySelector('[data-valmsg-for="' + name + '"]');
        if (!returnedSlot) {
            continue;
        }
        var message = returnedSlot.textContent.trim();
        setRegistrationFieldError(name, message);
        if (firstInvalid === null && message) {
            firstInvalid = registrationField(name);
        }
    }

    // 3. Show the step with the first wrong field.
    if (firstInvalid) {
        var steps = registrationSteps();
        for (var index = 0; index < steps.length; index++) {
            if (steps[index].contains(firstInvalid)) {
                goToRegistrationStep(index, false);
                break;
            }
        }
        firstInvalid.focus();
    } else {
        summary.scrollIntoView({ block: 'nearest' });
    }
}

function setRegistrationSubmitState(state) {
    registrationSubmitState = state;
    publishRegistrationState();
}

async function onRegistrationSubmit(event) {
    event.preventDefault();           // this script sends the form itself, with fetch()
    event.stopImmediatePropagation(); // and no page-wide form script (busy buttons, validation) handles it too
    if (registrationSending) {
        return;
    }
    // Every step before Review must be correct.
    for (var i = 0; i < registrationSteps().length - 1; i++) {
        if (!validateRegistrationStep(i)) {
            return;
        }
    }

    var form = registrationForm();
    var submitButton = document.getElementById('registerSubmit');
    registrationSending = true;
    submitButton.disabled = true;
    submitButton.textContent = 'Sending…';
    setRegistrationSubmitState('sending');
    try {
        // new FormData(form) contains every field AND the chosen files, like a normal multipart post.
        var response = await fetch(form.action || location.href, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });

        // Success: the server redirected to VerifyEmail (it created the application and sent the code).
        if (response.redirected && new URL(response.url).origin === location.origin) {
            setRegistrationSubmitState('accepted');
            location.assign(response.url);
            return;
        }

        setRegistrationSubmitState('failed');
        if (!response.ok) {
            showRegistrationError('Registration could not be completed. Please try again.');
            return;
        }
        showServerValidation(await response.text());
    } catch (e) {
        setRegistrationSubmitState('failed');
        showRegistrationError('Could not contact the registration service. Please try again.');
    } finally {
        registrationSending = false;
        submitButton.disabled = false;
        submitButton.textContent = 'Send verification code →';
    }
}

// =====================================================================================================
// Start
// =====================================================================================================

function setUpRegistrationPage() {
    var form = registrationForm();
    if (!document.getElementById('authStudio') || !form) {
        return;
    }

    document.getElementById('registrationNext').addEventListener('click', nextRegistrationStep);
    document.getElementById('registrationBack').addEventListener('click', previousRegistrationStep);
    form.addEventListener('input', onRegistrationInput);
    form.addEventListener('change', onRegistrationChange);
    form.addEventListener('focusin', onRegistrationFocusIn);
    form.addEventListener('focusout', onRegistrationFocusOut);
    // true = "capture": this listener runs before the page-wide ones, so it can stop them.
    form.addEventListener('submit', onRegistrationSubmit, true);

    for (var i = 0; i < REQUIRED_FIELDS.length; i++) {
        var field = registrationField(REQUIRED_FIELDS[i]);
        if (field) {
            field.setAttribute('aria-required', 'true');
        }
    }

    // "Show" / "Hide" on the two password fields
    var revealButtons = form.querySelectorAll('[data-reveal]');
    for (var r = 0; r < revealButtons.length; r++) {
        revealButtons[r].addEventListener('click', function (event) {
            var button = event.currentTarget;
            var input = document.getElementById(button.getAttribute('data-reveal'));
            var showText = input.type === 'password';
            if (showText) {
                input.type = 'text';
                button.textContent = 'Hide';
            } else {
                input.type = 'password';
                button.textContent = 'Show';
            }
            button.setAttribute('aria-pressed', String(showText));
        });
    }

    // Company telephone: digits only, at most 10 (the server requires exactly 10 digits).
    var phone = registrationField('Input.CompanyPhone');
    phone.addEventListener('input', function () {
        var digits = phone.value.replace(/\D/g, '').substring(0, 10);
        if (phone.value !== digits) {
            phone.value = digits;
        }
    });

    updatePasswordRules();
    updateFileStates();
    goToRegistrationStep(0, false);

    // After a normal (no-JavaScript) post with errors, open the step with the first server message.
    var firstServerError = form.querySelector('.field-validation-error, .validation-summary-errors');
    if (firstServerError) {
        var invalidInput = registrationField(firstServerError.getAttribute('data-valmsg-for'));
        var steps = registrationSteps();
        var stepWithError = 0;
        if (invalidInput) {
            for (var s = 0; s < steps.length; s++) {
                if (steps[s].contains(invalidInput)) {
                    stepWithError = s;
                }
            }
        }
        goToRegistrationStep(stepWithError, false);
    }

    // Values restored by the browser (Back button, autofill) arrive after this script ran.
    window.addEventListener('pageshow', function () {
        updatePasswordRules();
        updateFileStates();
        publishRegistrationState();
    });
}

// This script is at the end of <body>, so the form already exists: start at once (no flash of all four steps).
setUpRegistrationPage();
