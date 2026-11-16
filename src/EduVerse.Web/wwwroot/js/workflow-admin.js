// Thin page: backend owns tenant checks, transitions, clearance, and persistence.
(() => {
  const $ = (id) => document.getElementById(id);
  const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const uuid = (value) => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  const campus = () => $('campus')?.value?.trim();
  let clearedEmployee = null,clearanceRevision=0;
  const status = (message) => {$('workflow-status') ? $('workflow-status').textContent = message : void 0;};
  const key = () => crypto.randomUUID();
  async function request(path, method = 'GET', body = null) {
    if (!uuid(campus())) throw Error('Choose a campus.');
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
    work().then((result) => status(result || 'Completed.')).catch((error) => status(error.message));
  }
  $('binding-form')?.addEventListener('submit', (event) => {
    event.preventDefault();
    run(async () => {
      const employeeId = $('binding-employee')?.value?.trim();
      const userId = $('binding-user')?.value?.trim();
      if (!uuid(employeeId) || !uuid(userId)) throw Error('Choose an employee and user account.');
      const result = await request('employees/bind-identity', 'POST', { campusId: campus(), employeeId, userId });
      return 'Identity binding: ' + result.status;
    });
  });
  async function checkClearance(){
    const revision=++clearanceRevision,currentCampus=campus();
    clearedEmployee=null;
    if($('exit-button'))$('exit-button').disabled=true;
    const employeeId=$('exit-employee')?.value;
    if(!uuid(employeeId))return 'Choose an employee.';
    const result=await request('employees/'+employeeId+'/clearance?campusId='+currentCampus);
    if(revision!==clearanceRevision||campus()!==currentCampus||$('exit-employee')?.value!==employeeId)return 'Selection changed. Check clearance for the current employee.';
    if($('clearance-result'))$('clearance-result').textContent=result.outstandingAssets+' outstanding assets.';
    if(result.outstandingAssets===0){clearedEmployee=employeeId;if($('exit-button'))$('exit-button').disabled=false;}
    return result.outstandingAssets===0?'Clearance passed. Confirm to proceed.':'Return all assets first.';
  }
  $('clearance-form')?.addEventListener('submit',event=>{event.preventDefault();run(checkClearance);});
  $('exit-employee')?.addEventListener('change',()=>{clearedEmployee=null;if($('exit-button'))run(checkClearance);});
  $('campus')?.addEventListener('change',()=>{clearedEmployee=null;if($('exit-button'))$('exit-button').disabled=true;});
  $('campus')?.addEventListener('input', () => {
    clearedEmployee = null;
    $('exit-button') ? $('exit-button') ? $('exit-button').disabled = true : void 0 : void 0;
    $('clearance-result') ? $('clearance-result') ? $('clearance-result').textContent = '' : void 0 : void 0;
  });
  $('exit-employee')?.addEventListener('input', () => {
    clearedEmployee = null;
    $('exit-button') ? $('exit-button') ? $('exit-button').disabled = true : void 0 : void 0;
  });
  $('exit-button')?.addEventListener('click', () => run(async () => {
    if (!clearedEmployee) throw Error('Run clearance first.');
    if (!confirm('Offboard this employee and revoke linked tenant access?')) return 'Cancelled.';
    const id = clearedEmployee;
    clearedEmployee = null;
    $('exit-button') ? $('exit-button') ? $('exit-button').disabled = true : void 0 : void 0;
    const result = await request('employees/offboard', 'POST', { campusId: campus(), employeeId: id });
    return 'Employee offboarding: ' + result.status;
  }));
  $('purchase-form')?.addEventListener('submit', (event) => {
    event.preventDefault();
    run(async () => {
      const purchaseId = $('purchase-id')?.value?.trim();
      if (!uuid(purchaseId)) throw Error('Choose an ordered purchase.');
      const result = await request('procurement/submit-finance', 'POST', { campusId: campus(), purchaseId });
      return 'Finance request: ' + result.status;
    });
  });
})();
