document.querySelectorAll('form[data-offer-pricing]').forEach(function (form) {
    const totals = form.querySelector('[data-offer-totals]');
    if (!totals) return;

    const currency = form.querySelector('[data-offer-currency]');
    const rows = form.querySelectorAll('[data-offer-item]');
    const extras = form.querySelectorAll('[data-offer-extra]');
    const amounts = totals.querySelectorAll('[data-offer-amount]');
    const invoiceCurrency = totals.querySelector('[data-offer-invoice-currency]');
    const rate = BigInt(totals.dataset.offerTotals);
    const formatter = new Intl.NumberFormat('en');

    function scaledValue(input, scale) {
        return BigInt(Math.round((input.valueAsNumber || 0) * scale));
    }

    function formatMoney(cents) {
        return formatter.format(cents / 100n) + '.' + String(cents % 100n).padStart(2, '0');
    }

    function displayCents(milliCents) {
        const cents = milliCents / 1000n;
        const fraction = milliCents % 1000n;
        // Display subtotals and totals with the same midpoint rounding as .NET's N2.
        return fraction > 500n || (fraction === 500n && cents % 2n === 1n) ? cents + 1n : cents;
    }

    function update() {
        invoiceCurrency.textContent = currency.value;
        if (form.querySelector('input[type="number"]:invalid')) {
            amounts.forEach(function (output) { output.textContent = '—'; });
            return;
        }

        // Quantities have three decimals and unit prices are in cents. Multiply the
        // scaled values before rounding so the tax matches the server's decimal calculation.
        let products = 0n;
        rows.forEach(function (row) {
            products += scaledValue(row.querySelector('[data-offer-quantity]'), 1000)
                * scaledValue(row.querySelector('[data-offer-price]'), 100);
        });
        const values = { products: displayCents(products) };
        let servicesCents = 0n;
        extras.forEach(function (input) {
            const cost = scaledValue(input, 100);
            values[input.dataset.offerExtra] = cost;
            servicesCents += cost;
        });
        const subtotal = products + servicesCents * 1000n;
        const taxCents = (subtotal * rate + 50000n) / 100000n;
        values.services = servicesCents;
        values.subtotal = displayCents(subtotal);
        values.tax = taxCents;
        values.total = displayCents(subtotal + taxCents * 1000n);
        amounts.forEach(function (output) { output.textContent = formatMoney(values[output.dataset.offerAmount]); });
    }

    form.addEventListener('input', update);
    currency.addEventListener('change', update);
    update();
    totals.hidden = false;
});
