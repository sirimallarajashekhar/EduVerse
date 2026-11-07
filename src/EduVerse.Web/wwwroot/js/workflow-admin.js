// Thin page: backend owns tenant checks, transitions, clearance, and persistence.
(() => {
  const $ = id => document.getElementById(id);
  const csrf = document.querySelector('input[name="__RequestVerificationToken"]').value;
  const uuid = value => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  const campus = () => $('campus').value.trim();
  let clearedEmployee = null;
  const status = message => { $('workflow-status').textContent = message; };
  const key = () => crypto.randomUUID();
  async function request(path, method = 'GET', body = null) {
    if (!uuid(campus())) throw Error('Enter a valid campus UUID.');
    const headers = { Accept: 'application/json' };
    if (method !== 'GET') {
      headers.RequestVerificationToken = csrf;
      headers['Content-Type'] = 'application/json';
      headers['Idempotency-Key'] = key();
    }
    const response = await fetch('/bff/workflows/' + path, {
      method, headers, ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) {
      const error = await response.json().catch(() => null);
      throw Error(error?.message || 'Workflow rejected (' + response.status + ').');
    }
    return response.status === 204 ? null : response.json();
  }
  function run(work) {
    status('Submitting request…');
    work().then(result => status(result || 'Completed.')).catch(error => status(error.message));
  }
  $('binding-form').addEventListener('submit', event => {
    event.preventDefault();
    run(async () => {
      const employeeId = $('binding-employee').value.trim();
      const userId = $('binding-user').value.trim();
      if (!uuid(employeeId) || !uuid(userId)) throw Error('Use valid employee and user UUIDs.');
      const result = await request('employees/bind-identity', 'POST', { campusId: campus(), employeeId, userId });
      return 'Identity binding: ' + result.status;
    });
  });
  $('clearance-form').addEventListener('submit', event => {
    event.preventDefault();
    clearedEmployee = null;
    $('exit-button').disabled = true;
    $('clearance-result').textContent = '';
    run(async () => {
      const employeeId = $('exit-employee').value.trim();
      if (!uuid(employeeId)) throw Error('Enter a valid employee UUID.');
      const result = await request('employees/' + employeeId + '/clearance?campusId=' + campus());
      $('clearance-result').textContent = result.outstandingAssets + ' outstanding assets.';
      if (result.outstandingAssets === 0) {
        clearedEmployee = employeeId;
        $('exit-button').disabled = false;
      }
      return result.outstandingAssets === 0 ? 'Clearance passed. Confirm to proceed.' : 'Return all assets first.';
    });
  });
  $('campus').addEventListener('input', () => {
    clearedEmployee = null;
    $('exit-button').disabled = true;
    $('clearance-result').textContent = '';
  });
  $('exit-employee').addEventListener('input', () => {
    clearedEmployee = null;
    $('exit-button').disabled = true;
  });
  $('exit-button').addEventListener('click', () => run(async () => {
    if (!clearedEmployee) throw Error('Run clearance first.');
    if (!confirm('Offboard this employee and revoke linked tenant access?')) return 'Cancelled.';
    const id = clearedEmployee;
    clearedEmployee = null;
    $('exit-button').disabled = true;
    const result = await request('employees/offboard', 'POST', { campusId: campus(), employeeId: id });
    return 'Employee offboarding: ' + result.status;
  }));
  $('purchase-form').addEventListener('submit', event => {
    event.preventDefault();
    run(async () => {
      const purchaseId = $('purchase-id').value.trim();
      if (!uuid(purchaseId)) throw Error('Enter a valid purchase UUID.');
      const result = await request('procurement/submit-finance', 'POST', { campusId: campus(), purchaseId });
      return 'Finance request: ' + result.status;
    });
  });
})();
