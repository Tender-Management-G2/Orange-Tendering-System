(() => {
    function checkInsertion(event, text) {
        const input = event.target;
        if (!input.matches("[data-score-digits]") || text == null) return;
        const next = input.value.slice(0, input.selectionStart) + text + input.value.slice(input.selectionEnd);
        if (!/^[0-9]{0,2}$/.test(next)) event.preventDefault();
    }

    document.addEventListener("beforeinput", event => {
        if (event.inputType.startsWith("insert")) checkInsertion(event, event.data);
    });
    document.addEventListener("paste", event => checkInsertion(event, event.clipboardData?.getData("text")));

    function score(row, legacySelector) {
        const inputs = [...row.querySelectorAll("[data-technical-weight]")];
        if (!inputs.length) {
            const legacy = row.querySelector(legacySelector);
            return legacy?.value && legacy.checkValidity() ? Number(legacy.value) : null;
        }
        const total = inputs.every(input => input.value !== "" && input.checkValidity())
            ? Math.round(inputs.reduce((sum, input) => sum
                + Math.round(Number(input.value) * 10) * Math.round(Number(input.dataset.technicalWeight) * 100), 0) / 10) / 10000
            : null;
        row.querySelector("[data-technical-result]").value = total === null ? "—" : total.toFixed(2);
        return total;
    }

    function wire(root) {
        root.querySelectorAll("[data-approve-weights]").forEach(form => {
            const rows = form.querySelector("[data-technical-rows]");
            if (!rows) return;
            const display = form.querySelector("[data-technical-total]");
            const add = form.querySelector("[data-add-criterion]");
            const approve = form.querySelector("[data-approve-button]");
            const percent = new Intl.NumberFormat(form.dir === "rtl" ? "ar-JO" : "en", { style: "percent", maximumFractionDigits: 2 });
            const template = rows.firstElementChild.cloneNode(true);

            function update() {
                let total = 0;
                let valid = true;
                const names = new Set();
                [...rows.rows].forEach((row, index) => {
                    const name = row.querySelector("[data-criterion-name]");
                    const weight = row.querySelector("[data-criterion-weight]");
                    const remove = row.querySelector("[data-remove-criterion]");
                    name.name = `criteria[${index}].Name`;
                    weight.name = `criteria[${index}].Weight`;
                    name.setAttribute("aria-label", `Technical criterion ${index + 1}`);
                    weight.setAttribute("aria-label", `Percentage for criterion ${index + 1}`);
                    remove.setAttribute("aria-label", `Remove criterion ${index + 1}`);
                    remove.disabled = rows.rows.length === 1;
                    const key = name.value.trim().toLowerCase();
                    valid = valid && Boolean(key) && !names.has(key) && weight.checkValidity();
                    names.add(key);
                    total += Math.round(Number(weight.value) * 100);
                });
                const difference = 10000 - total;
                const amount = percent.format(Math.abs(difference) / 10000);
                const message = difference > 0 ? `${form.dataset.addLabel} ${amount}`
                    : difference < 0 ? `${form.dataset.reduceLabel} ${amount}`
                    : valid ? form.dataset.completeLabel : form.dataset.invalidLabel;
                display.textContent = `${form.dataset.totalLabel} ${percent.format(total / 10000)} · ${message}.`;
                display.setAttribute("aria-invalid", String(!valid || difference !== 0));
                approve.disabled = !valid || difference !== 0;
                add.disabled = rows.rows.length >= 20;
            }

            form.addEventListener("input", update);
            form.addEventListener("click", event => {
                const remove = event.target.closest("[data-remove-criterion]");
                if (remove && rows.rows.length > 1) remove.closest("tr").remove();
                else if (event.target.closest("[data-add-criterion]") && rows.rows.length < 20) {
                    const row = template.cloneNode(true);
                    rows.append(row);
                    row.querySelector("input").focus();
                }
                update();
            });
            update();
        });
    }

    window.OrangeTechnicalEvaluation = { score, wire };
})();
