// Presentation only. Counts and financial calculations are backend-owned.
(() => {
  const $ = id => document.getElementById(id);
  const status = message => { $('status').textContent = message; };
  const money = value => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value);
  const query = (path, fields) => path + '?' + new URLSearchParams(fields).toString();
  const isoDate = date => date.toISOString().slice(0, 10);
  const today = new Date();
  $('to').value = isoDate(today);
  const start = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - 5, 1));
  $('from').value = isoDate(start);
  function option(label, value) {
    const opt = document.createElement('option');
    opt.value = value; opt.textContent = label; return opt;
  }
  const campus = () => $('campus').value;
  const filters = () => ({ campusId: campus(), from: $('from').value, to: $('to').value });
  async function load() {
    const fields = filters();
    if (!fields.campusId || !fields.from || !fields.to) throw Error('Select campus and dates.');
    status('Loading report…');
    const response = await fetch(query('/bff/reporting/dashboard', fields), { headers: { Accept: 'application/json' } });
    if (!response.ok) throw Error('Report unavailable (' + response.status + ').');
    const data = await response.json();
    const labels = [
      ['Active students', data.kpis.activeStudents],
      ['Active enrollments', data.kpis.activeEnrollments],
      ['Active staff', data.kpis.activeStaff],
      ['Allocated assets', data.kpis.allocatedAssets],
      ['Pending admissions', data.kpis.pendingAdmissions],
      ['Charged', money(data.kpis.chargedInr)],
      ['Concessions', money(data.kpis.concessionsInr)],
      ['Collected', money(data.kpis.collectedInr)],
      ['Refunded', money(data.kpis.refundedInr)],
      ['Outstanding', money(data.kpis.outstandingInr)]
    ];
    const host = $('kpis'); host.replaceChildren();
    for (const [name, value] of labels) {
      const dt = document.createElement('dt'); dt.className = 'col-md-3';
      const dd = document.createElement('dd'); dd.className = 'col-md-3';
      dt.textContent = name; dd.textContent = String(value); host.append(dt, dd);
    }
    const rows = $('report-rows'); rows.replaceChildren();
    for (const month of data.months) {
      const tr = document.createElement('tr');
      for (const value of [
        month.year + '-' + String(month.month).padStart(2, '0'),
        month.enrollments, month.payments, money(month.collectedInr)
      ]) {
        const cell = document.createElement('td');
        cell.textContent = String(value); tr.append(cell);
      }
      rows.append(tr);
    }
    for (const [id, extension] of [
      ['download', 'csv'], ['download-xlsx', 'xlsx'], ['download-pdf', 'pdf']
    ]) {
      $(id).href = query('/bff/reporting/monthly.' + extension, fields);
      $(id).classList.remove('disabled');
      $(id).removeAttribute('aria-disabled');
    }
    status('Report generated (UTC).');
  }
  $('report-filter').addEventListener('submit', event => {
    event.preventDefault();
    load().catch(error => {
      for (const id of ['download', 'download-xlsx', 'download-pdf']) {
        $(id).classList.add('disabled');
        $(id).setAttribute('aria-disabled', 'true');
      }
      status(error.message);
    });
  });
  fetch('/bff/institution-admin/campuses').then(response => {
    if (!response.ok) throw Error('Campus list unavailable (' + response.status + ').');
    return response.json();
  }).then(campuses => {
    $('campus').replaceChildren(option('Select campus', ''));
    for (const c of campuses) $('campus').append(option(c.name + ' (' + c.code + ')', c.id));
    if (campuses.length) { $('campus').value = campuses[0].id; return load(); }
    status('No campuses available.');
  }).catch(error => status(error.message));
})();
