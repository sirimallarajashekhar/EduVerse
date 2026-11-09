// API-only historical reporting; aggregation, RBAC, tenant/campus scope and export files are backend-owned.
(() => {
 'use strict';
 const $ = id => document.getElementById(id);
 const from = $('reports-api-from'), to = $('reports-api-to'), status = $('reports-api-status');
 const days = date => date.toISOString().slice(0,10);
 const now = new Date(), start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth()-5, 1));
 from.value = days(start);
 to.value = days(now);
 const links = ['csv','xlsx','pdf'];
 let active = 0;
 let controller = null;
 const disable = () => links.forEach(ext => {
  const a = $('reports-api-'+ext); a.classList.add('disabled'); a.setAttribute('aria-disabled','true'); a.removeAttribute('href');
 });
 const number = n => Number.isFinite(n) ? n.toLocaleString('en-IN') : '—';
 const money = n => Number.isFinite(n) ? new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR'}).format(n) : '—';
 function append(parent, tag, value, cls) {
  const item = document.createElement(tag);
  item.textContent = String(value); if(cls) item.className = cls;
  parent.append(item); return item;
 }
 function validate(campus) {
  if (!/^[0-9a-f-]{36}$/i.test(campus)) throw Error('Select a valid campus.');
  if (!/^\d{4}-\d{2}-\d{2}$/.test(from.value) || !/^\d{4}-\d{2}-\d{2}$/.test(to.value))
   throw Error('Select a valid UTC date range.');
  const startTime = Date.parse(from.value+'T00:00:00Z');
  const endTime = Date.parse(to.value+'T00:00:00Z');
  if(!Number.isFinite(startTime)||!Number.isFinite(endTime)||endTime<startTime||
     endTime-startTime>366*86400000 || endTime>Date.now()+86400000)
   throw Error('Reporting range must be at most 366 days, and end no later than tomorrow (UTC).');
  return new URLSearchParams({campusId:campus,from:from.value,to:to.value}).toString();
 }
 async function run() {
  const seq=++active;
  if(controller) controller.abort();
  controller=new AbortController();
  disable();
  $('reports-api-kpis').replaceChildren();
  $('reports-api-months').replaceChildren();
  let params;
  try {params=validate($('reports-campus').value);}
  catch(error){status.textContent=error.message;return;}
  status.textContent='Loading tenant-secured historical report…';
  try {
   const response=await fetch('/bff/reporting/dashboard?'+params,{
    credentials:'same-origin',signal:controller.signal,headers:{Accept:'application/json'}
   });
   if(!response.ok)throw Error('Historical reporting API returned HTTP '+response.status+'. Verify reports subscription and campus access.');
   const data=await response.json();
   if(seq!==active || !data.kpis || !Array.isArray(data.months))return;
   const labels=[
    ['Active students',data.kpis.activeStudents],
    ['Active enrollments',data.kpis.activeEnrollments],
    ['Active staff',data.kpis.activeStaff],
    ['Allocated assets',data.kpis.allocatedAssets],
    ['Pending admissions',data.kpis.pendingAdmissions],
    ['Charged to end date',money(data.kpis.chargedInr)],
    ['Collected to end date',money(data.kpis.collectedInr)],
    ['Refunded to end date',money(data.kpis.refundedInr)],
    ['Outstanding as calculated',money(data.kpis.outstandingInr)]
   ];
   for(const [name,value] of labels){
    const card=append($('reports-api-kpis'),'div','', 'col-sm-6 col-xl-4');
    const inner=append(card,'div','', 'border rounded p-3 h-100');
    append(inner,'div',name,'small text-secondary');
    append(inner,'strong',typeof value==='number'?number(value):value);
   }
   const rows=$('reports-api-months');
   for(const month of data.months){
    const tr=document.createElement('tr');
    for(const v of [String(month.year)+'-'+String(month.month).padStart(2,'0'),
       number(month.enrollments),number(month.payments),money(month.collectedInr)])
      append(tr,'td',v);
    rows.append(tr);
   }
   if(!data.months.length){const tr=document.createElement('tr');const td=append(tr,'td','No monthly records.');td.colSpan=4;rows.append(tr);}
   for(const ext of links){
    const a=$('reports-api-'+ext);
    a.href='/bff/reporting/monthly.'+ext+'?'+params;
    a.classList.remove('disabled');a.removeAttribute('aria-disabled');
   }
   status.textContent='Historical report generated from authorized server aggregates.';
  }catch(error){if(seq===active && error.name!=='AbortError')status.textContent=error.message;}
 }
 $('reports-api-filter').addEventListener('submit',event=>{event.preventDefault();run();});
 $('reports-campus').addEventListener('change',()=>{active++;if(controller)controller.abort();disable();$('reports-api-kpis').replaceChildren();$('reports-api-months').replaceChildren();status.textContent='Campus changed. Generate a new historical report.';});
 for(const ext of links)$('reports-api-'+ext).addEventListener('click',e=>{if(e.currentTarget.classList.contains('disabled'))e.preventDefault();});
})();
