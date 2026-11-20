(() => {
    const statusHeaders = ["status", "decision", "result", "compliance", "verification"];
    const norm = value => (value || "").trim().toLowerCase();

    function enhance(table, index) {
        if (table.dataset.reportTableReady === "true" || !table.tHead || !table.tBodies.length) return;
        const headers = [...table.tHead.rows[0].cells].map(cell => norm(cell.textContent));
        const statusIndex = headers.findIndex(header => statusHeaders.some(key => header.includes(key)));
        const rows = [...table.tBodies[0].rows].filter(row => row.cells.length === headers.length);
        if (!rows.length) return;

        const controls = document.createElement("div");
        controls.className = "report-table-tools";
        const search = document.createElement("input");
        search.type = "search";
        search.placeholder = "Search this table…";
        search.setAttribute("aria-label", "Search table");
        controls.append(search);

        let status;
        if (statusIndex >= 0) {
            status = document.createElement("select");
            status.setAttribute("aria-label", "Filter table by status");
            status.append(new Option("All statuses", ""));
            [...new Set(rows.map(row => row.cells[statusIndex]?.textContent.trim()).filter(Boolean))]
                .sort((a, b) => a.localeCompare(b))
                .forEach(value => status.append(new Option(value, norm(value))));
            controls.append(status);
        }

        const count = document.createElement("small");
        count.className = "report-table-count";
        controls.append(count);
        table.parentElement.before(controls);
        table.dataset.reportTableReady = "true";

        const apply = () => {
            const text = norm(search.value);
            const selected = status ? status.value : "";
            let visible = 0;
            rows.forEach(row => {
                const matchText = !text || norm(row.textContent).includes(text);
                const matchStatus = !selected || norm(row.cells[statusIndex]?.textContent) === selected;
                row.hidden = !(matchText && matchStatus);
                if (!row.hidden) visible++;
            });
            count.textContent = `${visible} record${visible === 1 ? "" : "s"}`;
        };
        search.addEventListener("input", apply);
        status?.addEventListener("change", apply);
        apply();
    }

    document.addEventListener("DOMContentLoaded", () => document.querySelectorAll(".reports-detail-page .pg-table, .purchase-reports-page .pg-table, .operational-reports-page .pg-table, .supplier-reports-page .pg-table, .top-committee-reports-page .pg-table, .reports-management-page .pg-table").forEach(enhance));
})();
