// Phase 5: read-only, authorized presentation of existing reporting APIs.
// Date filtering affects receipt rows and counts ONLY; financial balances remain backend-owned.
(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const message = value => { el('reports-insights-status').textContent = value; };
    const money = value => typeof value === 'number' && Number.isFinite(value)
        ? new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value) : '—';
    const state = {campus: null, receipts: [], visible: [], finance: false, exams: false, ready: false};
    const dateOf = item => {
        const value = item.collectedUtc || item.collectedOn || null;
        if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}(?:T|$)/.test(value))
            return null;
        const day = value.slice(0, 10);
        const parsed = new Date(day + 'T00:00:00Z');
        return Number.isNaN(parsed.valueOf()) || parsed.toISOString().slice(0, 10) !== day ? null : day;
    };
    const text = (parent, tag, content, className) => {
        const node = document.createElement(tag);
        node.textContent = String(content);
        if (className) node.className = className;
        parent.append(node);
        return node;
    };
    const emptyTable = info => {
        const row = document.createElement('tr');
        const cell = text(row, 'td', info, 'text-secondary');
        cell.colSpan = 4;
        el('reports-receipt-rows').replaceChildren(row);
    };
    const showBars = (target, records, max, percent) => {
        const root = el(target);
        root.replaceChildren();
        if (!records.length) {
            text(root, 'p', 'No data available for this chart.', 'small text-secondary');
            return;
        }
        for (const item of records) {
            const line = text(root, 'div', '', 'ev-report-chart-row');
            const label = text(line, 'span', item.label, 'ev-report-chart-label');
            label.title = item.label;
            const track = text(line, 'div', '', 'ev-report-chart-track');
            const bar = text(track, 'span', '', 'ev-report-chart-bar');
            const value = Number.isFinite(item.value) ? Math.max(0, item.value) : 0;
            bar.style.width = Math.min(100, max ? value * 100 / max : 0) + '%';
            text(line, 'span', percent ? value.toFixed(1) + '%' : String(value), 'ev-report-chart-value');
        }
    };
    function chartReceipts() {
        const groups = new Map();
        for (const receipt of state.visible) {
            const month = dateOf(receipt).slice(0, 7);
            groups.set(month, (groups.get(month) || 0) + 1);
        }
        const items = [...groups.entries()].sort(([a], [b]) => a.localeCompare(b))
            .map(([label, value]) => ({label, value}));
        showBars('reports-receipt-chart', items, Math.max(0, ...items.map(x => x.value)), false);
    }
    function chartExams() {
        const table = el('reports-exam-rows');
        const values = [...table.querySelectorAll('tr')].map(row => {
            const cells = row.querySelectorAll('td');
            if (cells.length !== 6) return null;
            const raw = cells[2].textContent.trim();
            if (!/^(?:\d{1,3})(?:\.\d+)?%$/.test(raw)) return null;
            const value = Number(raw.slice(0, -1));
            if (!Number.isFinite(value) || value < 0 || value > 100) return null;
            return {label: cells[0].textContent.trim(), value};
        }).filter(Boolean);
        showBars('reports-exam-chart', values, 100, true);
    }
    function applyDates() {
        const from = el('reports-from').value, to = el('reports-to').value;
        if (from && to && from > to) {
            message('Start date must be on or before end date.');
            return;
        }
        const ignored = state.receipts.filter(x => !dateOf(x)).length;
        state.visible = state.receipts.filter(receipt => {
            const date = dateOf(receipt);
            return date !== null && (!from || date >= from) && (!to || date <= to);
        });
        const body = el('reports-receipt-rows');
        body.replaceChildren();
        for (const receipt of state.visible.slice(0, 500)) {
            const row = document.createElement('tr');
            text(row, 'td', receipt.receiptNumber ?? '—');
            text(row, 'td', dateOf(receipt));
            text(row, 'td', money(receipt.amount));
            text(row, 'td', receipt.reconciliationStatus ?? '—');
            body.append(row);
        }
        if (!state.visible.length) emptyTable('No receipts match these dates.');
        const truncated = Math.max(0, state.visible.length - 500);
        chartReceipts();
        message(state.visible.length + ' matching receipt(s).' +
            (ignored ? ' ' + ignored + ' receipt(s) lacked a valid collection date and were excluded.' : '') +
            (truncated ? ' Table display limited to 500 rows; CSV export includes all matching rows.' : ''));
        const allowed = state.ready && state.finance;
        el('reports-export-csv').disabled = !allowed || !state.visible.length;
        el('reports-print').disabled = !state.ready;
    }
    // Prefix untrusted spreadsheet-like cells so CSV cannot trigger formula execution in Excel.
    const safeCell = value => {
        let str = String(value ?? '');
        if (/^[\s]*[=+@\-\t\r]/.test(str)) str = "'" + str;
        return '"' + str.replace(/"/g, '""') + '"';
    };
    function csvExport() {
        if (!state.ready || !state.finance || !state.visible.length) return;
        const rows = [
            ['Receipt number', 'Collected date', 'Amount INR', 'Reconciliation status'],
            ...state.visible.map(item => [
                item.receiptNumber ?? '', dateOf(item) ?? '',
                typeof item.amount === 'number' ? item.amount.toFixed(2) : '',
                item.reconciliationStatus ?? ''
            ])
        ];
        const csv = '\uFEFF' + rows.map(row => row.map(safeCell).join(',')).join('\r\n');
        const blob = new Blob([csv], {type: 'text/csv;charset=utf-8'});
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = 'EduVerse-receipts-' + new Date().toISOString().slice(0, 10) + '.csv';
        document.body.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(objectUrl), 5000);
    }
    async function load(campus, signal, modules) {
        state.ready = false;
        state.campus = campus;
        state.finance = Boolean(modules.finance);
        state.exams = Boolean(modules.exams);
        state.receipts = [];
        state.visible = [];
        el('reports-export-csv').disabled = true;
        el('reports-print').disabled = true;
        el('reports-apply-dates').disabled = !state.finance;
        el('reports-receipt-chart').replaceChildren();
        el('reports-exam-chart').replaceChildren();
        emptyTable('Loading receipts…');
        message('Loading campus reporting details…');
        if (!state.finance) {
            emptyTable('Finance module unavailable under the current subscription.');
            state.ready = true;
            el('reports-print').disabled = false;
            if (state.exams) chartExams();
            message('Finance reports are unavailable for this institution.');
            return;
        }
        try {
            const endpoint = new URL('/bff/finance/payments', location.origin);
            endpoint.searchParams.set('campusId', campus);
            const response = await fetch(endpoint.pathname + endpoint.search, {
                credentials: 'same-origin', signal,
                headers: {Accept: 'application/json'}
            });
            if (!response.ok) throw new Error('Finance API HTTP ' + response.status);
            const items = await response.json();
            if (!Array.isArray(items)) throw new Error('Unexpected finance API response');
            if (signal.aborted) return;
            state.receipts = items;
            state.ready = true;
            el('reports-print').disabled = false;
            applyDates();
        } catch (error) {
            if (signal.aborted) return;
            emptyTable('Receipts could not be loaded.');
            message('Receipts unavailable: ' + error.message);
        }
        if (!signal.aborted && state.exams) chartExams();
    }
    el('reports-apply-dates').addEventListener('click', applyDates);
    el('reports-export-csv').addEventListener('click', csvExport);
    el('reports-print').addEventListener('click', () => {
        if (state.ready) window.print();
    });
    window.EduVerseInsights = {load};
})();
