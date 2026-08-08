(() => {
    "use strict";

    const groupsRoot = document.querySelector("#sensor-groups");
    const filterForm = document.querySelector("[data-sensor-filters]");
    const streamState = document.querySelector("#stream-state");
    const snapshotTime = document.querySelector("#snapshot-time");
    const emptyState = document.querySelector("#empty-filter-state");

    if (!groupsRoot || !filterForm || !streamState || !snapshotTime || !emptyState) {
        return;
    }

    const filters = {
        search: filterForm.querySelector("[data-filter-search]"),
        status: filterForm.querySelector("[data-filter-status]"),
        quantity: filterForm.querySelector("[data-filter-quantity]"),
        device: filterForm.querySelector("[data-filter-device]"),
        origin: filterForm.querySelector("[data-filter-origin]")
    };

    const normalized = value => (value ?? "").toString().trim().toLocaleLowerCase();

    function applyFilters() {
        const selected = {
            search: normalized(filters.search?.value),
            status: normalized(filters.status?.value),
            quantity: normalized(filters.quantity?.value),
            device: filters.device?.value ?? "",
            origin: normalized(filters.origin?.value)
        };
        let visibleCount = 0;

        groupsRoot.querySelectorAll("[data-sensor-group]").forEach(group => {
            let groupVisible = false;
            group.querySelectorAll("[data-sensor-row]").forEach(row => {
                const visible = (!selected.search || normalized(row.dataset.search).includes(selected.search)) &&
                    (!selected.status || normalized(row.dataset.status) === selected.status) &&
                    (!selected.quantity || normalized(row.dataset.quantity) === selected.quantity) &&
                    (!selected.device || row.dataset.device === selected.device) &&
                    (!selected.origin || normalized(row.dataset.origin) === selected.origin);
                row.hidden = !visible;
                groupVisible ||= visible;
                visibleCount += visible ? 1 : 0;
            });
            group.hidden = !groupVisible;
        });

        emptyState.hidden = visibleCount !== 0;
    }

    function setConnectionState(label) {
        streamState.textContent = label;
        streamState.className = `status-badge status-${label.toLocaleLowerCase()}`;
    }

    function element(name, className, text) {
        const node = document.createElement(name);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function appendTextLine(parent, label, value) {
        const line = element("span", "sensor-subtitle");
        line.append(document.createTextNode(`${label}: `), document.createTextNode(value));
        parent.append(line);
    }

    function readingView(reading) {
        const presentation = reading.presentation ?? null;
        const deviceKey = presentation?.deviceGroupKey?.trim() || `${reading.source}:default`;
        const deviceName = presentation?.deviceDisplayName?.trim() || reading.source;
        const deviceCategory = presentation?.deviceCategory?.trim() || "Other";
        const rawLabel = presentation?.rawLabel?.trim() || reading.displayName;
        const calculated = reading.source === "calculated" || reading.id.startsWith("calculated:");
        const origin = calculated ? "Calculated" : "Physical";
        const searchable = [
            reading.displayName,
            rawLabel,
            reading.alias,
            reading.id,
            deviceName,
            deviceKey,
            reading.source,
            presentation?.description
        ].filter(Boolean).join(" ");

        return {
            ...reading,
            presentation,
            deviceKey,
            deviceName,
            deviceCategory,
            rawLabel,
            origin,
            searchable
        };
    }

    function createCopyButton(sensorId) {
        const button = element("button", "copy-button", "Copy ID");
        button.type = "button";
        button.dataset.copyValue = sensorId;
        return button;
    }

    function createReadingRow(reading) {
        const row = document.createElement("tr");
        row.dataset.sensorRow = "";
        row.dataset.search = reading.searchable;
        row.dataset.status = reading.status;
        row.dataset.quantity = reading.quantity;
        row.dataset.device = reading.deviceKey;
        row.dataset.origin = reading.origin;

        const identity = document.createElement("td");
        identity.append(
            element("strong", "sensor-name", reading.displayName),
            element("span", "sensor-subtitle", `Raw: ${reading.rawLabel}`)
        );
        const details = element("details", "sensor-details");
        details.append(element("summary", null, "Identity and description"));
        if (reading.presentation?.description) {
            details.append(element("p", null, reading.presentation.description));
        }
        const idLine = element("div", "id-line");
        idLine.append(element("code", null, reading.id), createCopyButton(reading.id));
        details.append(idLine);
        if (reading.presentation?.interpretationConfidence) {
            details.append(element("span", null, `Confidence: ${reading.presentation.interpretationConfidence}`));
        }
        identity.append(details);

        const valueCell = element("td", "sensor-value");
        const hasValue = Number.isFinite(reading.value);
        valueCell.append(element("span", null, hasValue ? formatValue(reading.value) : "—"));
        if (hasValue && reading.unitSymbol) {
            valueCell.append(element("span", "unit", reading.unitSymbol));
        }
        valueCell.append(element("span", "sensor-subtitle", reading.quantity));

        const statusCell = document.createElement("td");
        const knownStatuses = new Set(["available", "unavailable", "stale", "calculationerror", "missingdependency"]);
        const statusKey = normalized(reading.status).replaceAll(" ", "");
        statusCell.append(element(
            "span",
            `status-badge status-${knownStatuses.has(statusKey) ? statusKey : "unavailable"}`,
            reading.status));

        const updatedCell = document.createElement("td");
        if (reading.lastSuccessfulUpdate) {
            const time = element("time", null, formatTimestamp(reading.lastSuccessfulUpdate));
            time.dateTime = reading.lastSuccessfulUpdate;
            updatedCell.append(time);
        } else {
            updatedCell.append(element("span", null, "Never"));
        }

        const contextCell = document.createElement("td");
        contextCell.append(element("span", null, `${reading.origin} · ${reading.source}`));
        if (reading.origin === "Calculated") {
            contextCell.append(element("code", "alias-key", reading.alias));
            const manageLink = element("a", "sensor-action", "Manage calculation");
            manageLink.href = `/calculations/${encodeURIComponent(reading.alias)}`;
            contextCell.append(manageLink);
        } else if (reading.alias) {
            contextCell.append(element("code", "alias-key", reading.alias));
            const manageLink = element("a", "sensor-action", "Manage alias");
            manageLink.href = `/aliases/${encodeURIComponent(reading.alias)}`;
            contextCell.append(manageLink);
        } else if (reading.origin === "Physical") {
            const createLink = element("a", "sensor-action", "Create alias");
            createLink.href = `/aliases?targetId=${encodeURIComponent(reading.id)}`;
            contextCell.append(createLink);
        }

        row.append(identity, valueCell, statusCell, updatedCell, contextCell);
        return row;
    }

    function createGroup(group) {
        const section = element("section", "sensor-group");
        section.dataset.sensorGroup = "";
        section.dataset.deviceKey = group.key;

        const heading = element("div", "sensor-group-heading");
        const headingText = document.createElement("div");
        headingText.append(
            element("p", "eyebrow", group.category),
            element("h2", null, group.name)
        );
        const suffix = group.readings.length === 1 ? "reading" : "readings";
        heading.append(headingText, element("span", "sensor-count", `${group.readings.length} ${suffix}`));

        const scroll = element("div", "table-scroll");
        const table = element("table", "sensor-table");
        const head = document.createElement("thead");
        const headRow = document.createElement("tr");
        ["Reading", "Value", "Status", "Last update", "Context"].forEach(label => {
            const cell = element("th", null, label);
            cell.scope = "col";
            headRow.append(cell);
        });
        head.append(headRow);
        const body = document.createElement("tbody");
        group.readings.forEach(reading => body.append(createReadingRow(reading)));
        table.append(head, body);
        scroll.append(table);
        section.append(heading, scroll);
        return section;
    }

    function groupReadings(readings) {
        const groups = new Map();
        readings.map(readingView).forEach(reading => {
            if (!groups.has(reading.deviceKey)) {
                groups.set(reading.deviceKey, {
                    key: reading.deviceKey,
                    name: reading.deviceName,
                    category: reading.deviceCategory,
                    readings: []
                });
            }
            groups.get(reading.deviceKey).readings.push(reading);
        });
        return [...groups.values()]
            .map(group => ({
                ...group,
                readings: group.readings.sort((left, right) =>
                    left.displayName.localeCompare(right.displayName) || left.id.localeCompare(right.id))
            }))
            .sort((left, right) =>
                left.category.localeCompare(right.category) ||
                left.name.localeCompare(right.name) ||
                left.key.localeCompare(right.key));
    }

    function updateDeviceOptions(groups) {
        if (!filters.device) {
            return;
        }
        const selected = filters.device.value;
        const options = [new Option("All devices", "")];
        groups.forEach(group => options.push(new Option(group.name, group.key)));
        if (selected && !groups.some(group => group.key === selected)) {
            options.push(new Option(selected, selected));
        }
        filters.device.replaceChildren(...options);
        filters.device.value = selected;
    }

    function renderSnapshot(snapshot) {
        const groups = groupReadings(snapshot.sensors ?? []);
        groupsRoot.replaceChildren(...groups.map(createGroup));
        updateDeviceOptions(groups);
        if (snapshot.capturedAt) {
            snapshotTime.textContent = formatTimestamp(snapshot.capturedAt);
            snapshotTime.dateTime = snapshot.capturedAt;
        }
        applyFilters();
    }

    function formatValue(value) {
        return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value);
    }

    function formatTimestamp(value) {
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? value : date.toLocaleString();
    }

    filterForm.addEventListener("submit", event => {
        event.preventDefault();
        applyFilters();
        const query = new URLSearchParams(new FormData(filterForm));
        const nextUrl = query.toString() ? `${location.pathname}?${query}` : location.pathname;
        history.replaceState(null, "", nextUrl);
    });
    Object.values(filters).forEach(control => {
        control?.addEventListener(control.type === "search" ? "input" : "change", applyFilters);
    });

    groupsRoot.addEventListener("click", async event => {
        const button = event.target.closest("[data-copy-value]");
        if (!button) {
            return;
        }
        try {
            await navigator.clipboard.writeText(button.dataset.copyValue);
            button.textContent = "Copied";
            window.setTimeout(() => { button.textContent = "Copy ID"; }, 1500);
        } catch {
            button.textContent = "Copy unavailable";
        }
    });

    const source = new EventSource("/api/sensors/stream");
    source.addEventListener("open", () => setConnectionState("Live"));
    source.addEventListener("sensors", event => {
        try {
            renderSnapshot(JSON.parse(event.data));
            setConnectionState("Live");
        } catch {
            setConnectionState("Reconnecting");
        }
    });
    source.addEventListener("error", () => {
        setConnectionState(source.readyState === EventSource.CLOSED ? "Disconnected" : "Reconnecting");
    });
    window.addEventListener("offline", () => setConnectionState("Disconnected"));
    window.addEventListener("online", () => setConnectionState("Reconnecting"));

    applyFilters();
})();
