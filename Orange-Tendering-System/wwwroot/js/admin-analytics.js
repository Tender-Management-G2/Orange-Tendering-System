(() => {
    const page = document.querySelector("[data-analytics-page]");
    const form = page?.querySelector("[data-analytics-filters]");
    if (!form) return;

    const status = form.querySelector("[data-analytics-status]");
    const clear = form.querySelector("[data-analytics-clear]");
    let filterRequest;
    let searchTimer;
    let refreshing = false;
    let historyUrl = new URL(window.location.href);

    function cancelFilter() {
        window.clearTimeout(searchTimer);
        searchTimer = undefined;
        filterRequest?.abort();
        filterRequest = undefined;
    }

    function currentFilters() {
        const url = new URL(form.action);
        for (const [name, value] of new FormData(form)) {
            if (value.trim()) url.searchParams.set(name, value.trim());
        }
        return url;
    }

    function syncControls(nextForm) {
        for (const control of form.elements) {
            if (control.name) control.value = nextForm.elements.namedItem(control.name)?.value ?? "";
        }
    }

    async function update(url, { push = true, sync = false } = {}) {
        cancelFilter();
        const request = new AbortController();
        filterRequest = request;
        status.textContent = form.dataset.updating;
        try {
            const response = await fetch(url, { signal: request.signal, credentials: "same-origin", cache: "no-store" });
            if (request.signal.aborted) return;
            if (response.redirected) {
                window.location.assign(url);
                return;
            }
            if (!response.ok) throw new Error("Could not load filters");

            const documentHtml = await response.text();
            if (request.signal.aborted) return;
            const nextPage = new DOMParser().parseFromString(documentHtml, "text/html").querySelector("[data-analytics-page]");
            const nextForm = nextPage?.querySelector("[data-analytics-filters]");
            if (!nextForm) throw new Error("Missing filter form");

            const replacements = [...page.querySelectorAll("[data-analytics-region]")].map(region => {
                const next = nextPage.querySelector(`[data-analytics-region="${region.dataset.analyticsRegion}"]`);
                if (!next) throw new Error("Missing results");
                return [region, next];
            });
            if (sync) syncControls(nextForm);
            for (const [region, next] of replacements) {
                region.replaceWith(next);
            }
            if (push && url.href !== window.location.href) {
                history.pushState(null, "", url);
                historyUrl = url;
            }
            status.textContent = "";
        } catch {
            if (!request.signal.aborted) status.textContent = form.dataset.updateError;
        } finally {
            if (filterRequest === request) filterRequest = undefined;
        }
    }

    function applyFilters() {
        cancelFilter();
        const from = form.elements.namedItem("from").value;
        const to = form.elements.namedItem("to").value;
        if (!form.checkValidity()) {
            status.textContent = [...form.elements].find(control => control.validity && !control.validity.valid)?.validationMessage ?? "";
        } else if (from && to && from > to) {
            status.textContent = form.dataset.invalidRange;
        } else {
            void update(currentFilters());
        }
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        applyFilters();
    });
    form.addEventListener("input", event => {
        if (event.target.name !== "search" || event.isComposing) return;
        cancelFilter();
        status.textContent = "";
        searchTimer = window.setTimeout(applyFilters, 300);
    });
    form.addEventListener("change", event => {
        if (event.target.name !== "search") applyFilters();
        else if (searchTimer) applyFilters();
    });
    clear.addEventListener("click", event => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        void update(new URL(clear.href), { sync: true });
    });
    window.addEventListener("popstate", () => {
        const url = new URL(window.location.href);
        const previousUrl = historyUrl;
        historyUrl = url;
        if (url.pathname !== previousUrl.pathname || url.search !== previousUrl.search) {
            void update(url, { push: false, sync: true });
        }
    });
    form.querySelector("[data-analytics-apply]").hidden = true;

    async function refresh() {
        const panel = page.querySelector("[data-analytics-live]");
        if (!panel?.dataset.source || refreshing || filterRequest || document.hidden || panel.contains(document.activeElement)) return;
        refreshing = true;
        try {
            const response = await fetch(panel.dataset.source, { credentials: "same-origin", cache: "no-store", redirect: "error" });
            if (!response.ok) return;
            const content = await response.text();
            if (panel.isConnected && !filterRequest && !panel.contains(document.activeElement) && !window.getSelection()?.toString()) {
                panel.innerHTML = content;
            }
        } catch {
            // Keep the last successful snapshot and retry on the next interval.
        } finally {
            refreshing = false;
        }
    }

    if (page.querySelector("[data-analytics-live]")) {
        window.setInterval(refresh, 60_000);
        document.addEventListener("visibilitychange", refresh);
    }
})();
