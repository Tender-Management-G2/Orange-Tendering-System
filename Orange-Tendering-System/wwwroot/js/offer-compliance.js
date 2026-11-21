(function () {
    'use strict';
    function sync(form) {
        var status = form.querySelector('[name="status"]');
        var reason = form.querySelector('[name="notes"]');
        var missing = form.querySelector('[name="missingRequirements"]');
        if (!status || !reason || !missing) return;
        var nonCompliant = status.value === 'NonCompliant';
        var clarification = status.value === 'PendingClarification';
        form.querySelector('#noncompliant-box').hidden = !nonCompliant;
        form.querySelector('#clarification-box').hidden = !clarification;
        reason.required = nonCompliant;
        reason.disabled = !nonCompliant;
        missing.required = clarification;
        missing.disabled = !clarification;
        form.querySelector('[data-compliance-confirmation]').hidden = status.value !== 'Compliant';
    }
    function initialize(root) {
        if (root.nodeType !== 1) return;
        if (root.matches('form[data-offer-compliance]')) sync(root);
        root.querySelectorAll('form[data-offer-compliance]').forEach(sync);
    }
    document.addEventListener('change', function (event) {
        var form = event.target.closest('form[data-offer-compliance]');
        if (form) sync(form);
    });
    // Run before the generic modal's submit handler builds its FormData.
    document.addEventListener('submit', function (event) {
        if (event.target.matches('form[data-offer-compliance]')) sync(event.target);
    }, true);
    initialize(document.documentElement);
    // The shared modal strips page scripts, so initialize newly inserted forms here as well.
    new MutationObserver(function (records) {
        records.forEach(function (record) { record.addedNodes.forEach(initialize); });
    }).observe(document.body, { childList: true, subtree: true });
})();
