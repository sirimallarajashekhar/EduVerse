// Read-only reporting presentation. All metrics, permissions and tenant scope are API-owned.
(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const set = (id, value) => { if (el(id)) el(id).textContent = value; };
    const formatNumber = value => typeof value === 'number' && Number.isFinite(value)
        ? value.toLocaleString('en-IN') : '—';
    const formatMoney = value => typeof value === 'number' && Number.isFinite(value)
        ? new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value) : '—';
    const formatPercentage = value => typeof value === 'number' && Number.isFinite(value)
        ? value.toLocaleString('en-IN', { maximumFractionDigits: 2 }) + '%' : '—';
    let generation = 0;
    let access = null;

    async function read(path, signal) {
        const response = await fetch(path, {
            method: 'GET', credentials: 'same-origin', signal,
            headers: { Accept: 'application/json' }
        });
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return response.json();
    }
    const url = (route, campusId) => {
        const target = new URL(route, location.origin);
        if (campusId) target.searchParams.set('campusId', campusId);
        return target.pathname + target.search;
    };
    const hasModule = id => access?.modules?.some(item => item.id === id && item.enabled) === true;
    const clear = ids => ids.forEach(id => set('reports-' + id, '—'));
    const note = (section, message) => set('reports-' + section + '-note', message);

    function renderSpecialized() {
        const host = el('reports-specialized-links');
        host.replaceChildren();
        const modules = access?.modules || [];
        for (const id of ['hrms', 'ams']) {
            const module = modules.find(item => item.id === id && item.enabled);
            if (!module || typeof module.route !== 'string' || !module.route.startsWith('/') ||
                module.route.startsWith('//')) continue;
            const a = document.createElement('a');
            a.className = 'btn btn-outline-primary btn-sm';
            a.textContent = 'Open ' + module.name;
            a.href = module.route;
            host.append(a);
        }
        if (!host.childElementCount) {
            const p = document.createElement('p');
            p.className = 'small text-secondary mb-0';
            p.textContent = 'No HRMS or AMS workspace is enabled for this institution.';
            host.append(p);
        }
    }

    async function institution(signal) {
        const data = await read('/bff/institution-admin/dashboard', signal);
        for (const key of ['campuses', 'departments', 'buildings', 'members'])
            set('reports-' + key, formatNumber(data[key]));
    }

    async function attendance(campusId, signal) {
        const metrics = await read(url('/bff/attendance/dashboard', campusId), signal);
        set('reports-periods', formatNumber(metrics.periods));
        set('reports-slots', formatNumber(metrics.timetableSlots));
        set('reports-student-marks', formatNumber(metrics.studentMarks));
        set('reports-staff-marks', formatNumber(metrics.staffMarks));
    }

    async function admissions(campusId, signal) {
        const [applications, students] = await Promise.all([
            read(url('/bff/students/applications', campusId), signal),
            read(url('/bff/students', campusId), signal)
        ]);
        if (!Array.isArray(applications) || !Array.isArray(students))
            throw new Error('Unexpected API response');
        set('reports-applications', formatNumber(applications.length));
        set('reports-students', formatNumber(students.length));
    }

    async function finance(campusId, signal) {
        const metrics = await read(url('/bff/finance/dashboard', campusId), signal);
        for (const field of ['gross', 'collected', 'outstanding', 'overdue', 'concessions', 'refunded'])
            set('reports-' + field, formatMoney(metrics[field]));
        for (const field of ['fees', 'charges'])
            set('reports-' + field, formatNumber(metrics[field]));
    }

    function addCell(row, value) {
        const cell = document.createElement('td');
        cell.textContent = value;
        row.append(cell);
    }

    async function exams(campusId, signal) {
        const rows = el('reports-exam-rows');
        const data = await read(url('/bff/exams/', campusId), signal);
        if (!Array.isArray(data)) throw new Error('Unexpected API response');
        const published = data.filter(x => x.status === 'PUBLISHED');
        rows.replaceChildren();
        if (published.length === 0) {
            const tr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = 6;
            td.textContent = 'No published examinations for the selected campus.';
            tr.append(td);
            rows.append(tr);
            return;
        }
        // Individual exam analytics are authorized independently. One failed exam must not hide others.
        const results = await Promise.allSettled(published.map(x =>
            read(url('/bff/exams/' + encodeURIComponent(x.id) + '/analytics', campusId), signal)));
        published.forEach((exam, i) => {
            const row = document.createElement('tr');
            const item = results[i];
            const metrics = item.status === 'fulfilled' ? item.value : null;
            addCell(row, String(exam.title || exam.code || 'Exam'));
            addCell(row, metrics ? formatNumber(metrics.students) : 'Unavailable');
            for (const field of ['averagePercentage', 'passPercentage', 'highestPercentage', 'lowestPercentage'])
                addCell(row, metrics ? formatPercentage(metrics[field]) : '—');
            rows.append(row);
        });
        if (results.some(x => x.status === 'rejected'))
            note('exams', 'Some exam analytics were unavailable or access was denied.');
    }

    async function refresh() {
        const selected = el('reports-campus').value;
        const sequence = ++generation;
        if (!selected) {
            set('reports-status', 'Choose a campus to view operational reports.');
            return;
        }
        const signal = controllerSignal();
        set('reports-status', 'Loading authorized reports…');
        set('reports-updated', '');
        for (const section of ['attendance', 'admissions', 'finance', 'exams']) note(section, '');
        clear(['periods', 'slots', 'student-marks', 'staff-marks', 'applications', 'students',
            'gross', 'collected', 'outstanding', 'overdue', 'concessions', 'refunded', 'fees', 'charges']);
        el('reports-exam-rows').replaceChildren();
        const tasks = [
            ['institution', () => institution(signal)],
            ['attendance', () => attendance(selected, signal)],
            ['admissions', () => admissions(selected, signal)],
            ['finance', () => finance(selected, signal)],
            ['exams', () => exams(selected, signal)]
        ].filter(([name]) => name === 'institution' || hasModule(
            { attendance: 'attendance', admissions: 'students', finance: 'finance', exams: 'exams' }[name]));
        const skipped = ['attendance', 'admissions', 'finance', 'exams'].filter(name =>
            !tasks.some(([enabled]) => enabled === name));
        skipped.forEach(name => note(name, 'Module not enabled for this institution.'));
        const results = await Promise.allSettled(tasks.map(([, task]) => task()));
        if (sequence !== generation) return;
        const errors = [];
        results.forEach((result, index) => {
            if (result.status === 'rejected') {
                const name = tasks[index][0];
                if (name !== 'institution') note(name, 'Unavailable: ' + result.reason.message);
                errors.push(name);
            }
        });
        set('reports-status', errors.length
            ? 'Some reports could not be loaded: ' + errors.join(', ') + '. No unavailable values are estimated.'
            : 'Reporting data loaded from authorized APIs.');
        if (!errors.length) set('reports-updated', 'Refreshed ' + new Date().toLocaleTimeString());
    }

    let lastController;
    function controllerSignal() {
        if (lastController) lastController.abort();
        lastController = new AbortController();
        return lastController.signal;
    }

    async function start() {
        try {
            const [modules, campuses] = await Promise.all([
                read('/bff/tenant/modules'), read('/bff/institution-admin/campuses')
            ]);
            if (!Array.isArray(modules.modules) || !Array.isArray(campuses))
                throw new Error('Invalid institution reporting configuration.');
            access = modules;
            renderSpecialized();
            const select = el('reports-campus');
            select.replaceChildren();
            for (const campus of campuses) {
                const option = document.createElement('option');
                option.value = campus.id;
                option.textContent = campus.name + ' (' + campus.code + ')';
                select.append(option);
            }
            if (campuses.length) await refresh();
            else set('reports-status', 'Create a campus before opening operational reports.');
        } catch (error) {
            set('reports-status', 'Reporting could not be initialized: ' + error.message);
        }
    }
    el('reports-campus').addEventListener('change', refresh);
    el('reports-refresh').addEventListener('click', refresh);
    start();
})();
