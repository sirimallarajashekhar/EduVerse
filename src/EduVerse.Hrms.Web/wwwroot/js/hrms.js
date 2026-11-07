// Thin HRMS UI. Backend REST API is authoritative for permissions, balances and payroll.
(() => {
 const $=id=>document.getElementById(id),v=id=>$(id).value,campus=()=>v('campus');
 const csrf=document.querySelector('input[name="__RequestVerificationToken"]').value;
 const notice=message=>{$('status').textContent=message;};
 let employees=[],designations=[],departments=[],leaveTypes=[],leaves=[];
 const link=(path,params={})=>{
  const u=new URL('/hrms/bff/'+path,location.origin);
  for(const [key,value] of Object.entries(params))
   if(value!==null&&value!==undefined)u.searchParams.set(key,value);
  return u.pathname+u.search;
 };
 async function request(path,method='GET',body=null,params={},multipart=false) {
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(body&&!multipart)headers['Content-Type']='application/json';
  const response=await fetch(link(path,params),{method,headers,
   ...(body?{body:multipart?body:JSON.stringify(body)}:{})});
  if(!response.ok)throw new Error('HRMS request rejected ('+response.status+'). Check state, permissions or leave balance.');
  return response.status===204?null:response.json();
 }
 function select(id,rows,label,empty='Select an option'){
  const host=$(id);host.replaceChildren();
  const first=document.createElement('option');first.value='';first.textContent=empty;host.append(first);
  for(const row of rows){const opt=document.createElement('option');opt.value=row.id;
   opt.textContent=label(row);host.append(opt);}
 }
 function list(id,rows,label){
  const host=$(id);host.replaceChildren();
  for(const row of rows){const li=document.createElement('li');li.textContent=label(row);host.append(li);}
 }
 async function run(job){try{await job();notice('HRMS dashboard ready.');}
  catch(e){notice(e.message);}}
 function form(id,job){$(id).addEventListener('submit',e=>{e.preventDefault();
   run(async()=>{await job();e.target.reset();await refresh();});});}
 async function refreshDocuments(){
  $('documents').replaceChildren();
  if(!v('employee'))return;
  const docs=await request('employees/'+v('employee')+'/documents','GET',null,{campusId:campus()});
  const host=$('documents');
  for(const d of docs){
   const li=document.createElement('li'),download=document.createElement('a');
   download.textContent=d.fileName+' ('+d.type+')';
   download.href=link('employees/'+v('employee')+'/documents/'+d.id+'/download',{campusId:campus()});
   li.append(download);host.append(li);
  }
 }
 async function refreshBalance(){
  $('leave-balance').textContent='';
  if(!v('employee')||!v('leave-type'))return;
  const balance=await request('employees/'+v('employee')+'/leave-balance','GET',null,
    {campusId:campus(),leaveTypeId:v('leave-type'),year:new Date().getFullYear()});
  $('leave-balance').textContent='Annual '+balance.allowance+
    ' · Used '+balance.used+' · Remaining '+balance.remaining;
 }
 async function refresh(){
  if(!campus())return;
  [employees,designations,departments,leaveTypes,leaves]=await Promise.all([
   request('employees','GET',null,{campusId:campus()}),
   request('designations','GET',null,{campusId:campus()}),
   request('departments','GET',null,{campusId:campus()}),
   request('leave-types','GET',null,{campusId:campus()}),
   request('leaves','GET',null,{campusId:campus()})
  ]);
  select('designation',designations,x=>x.title,'No designation / choose optional');
  select('department',departments,x=>x.name,'No department / choose optional');
  select('employee',employees,x=>x.givenName+' '+x.familyName+' · '+x.status,'Select an employee');
  select('leave-type',leaveTypes,x=>x.title+' ('+x.annualDays+' days)','Select leave type');
  list('designations',designations,x=>x.code+' · '+x.title);
  const host=$('leaves');host.replaceChildren();
  for(const item of leaves){
   const li=document.createElement('li');
   const employee=employees.find(x=>x.id===item.employeeId);
   li.append(document.createTextNode((employee?.givenName||item.employeeId.slice(0,8))+
     ' · '+item.from+' – '+item.to+' · '+item.days+' days · '+item.status+' '));
   if(item.status==='PENDING'){
    for(const decision of ['APPROVED','REJECTED']){
     const b=document.createElement('button');b.type='button';
     b.className='btn btn-sm btn-outline-primary ms-1';b.textContent=decision==='APPROVED'?'Approve':'Reject';
     b.addEventListener('click',()=>run(async()=>{
      await request('leaves/'+item.id+'/decision','POST',{decision},{campusId:campus()});
      await refresh();
     }));
     li.append(b);
    }
   }
   host.append(li);
  }
  const stats=await request('dashboard','GET',null,{campusId:campus()});
  $('summary').textContent='Employees '+stats.employees+' · Active '+stats.activeEmployees+
   ' · Pending leave '+stats.pendingLeaves+' · Approved leave '+stats.approvedLeaves+
   ' · Current-month preview total ₹'+Number(stats.monthlyPayrollPreview).toFixed(2);
  await refreshDocuments();
  await refreshBalance();
 }
 form('designation-form',()=>request('designations','POST',
   {campusId:campus(),code:v('des-code'),title:v('des-title')}));
 form('employee-form',()=>request('employees','POST',{
  campusId:campus(),givenName:v('first'),familyName:v('last'),
  type:v('type'),joinedOn:v('joined'),userId:v('account-user')||null,departmentId:v('department')||null,
  designationId:v('designation')||null,workEmail:v('work-email')||null
 }));
 form('lifecycle-form',async()=>{
  if(!v('employee'))throw Error('Select an employee.');
  if(v('lifecycle')==='EXITED'&&!confirm('Close this employment record?'))return;
  await request('employees/'+v('employee')+'/lifecycle','POST',
   {status:v('lifecycle'),note:v('lifecycle-note')||null},{campusId:campus()});
 });
 form('document-form',async()=>{
  if(!v('employee'))throw Error('Select an employee.');
  const file=$('document-file').files[0];
  if(!file||file.size>1048576)throw Error('Select a file up to 1 MiB.');
  const data=new FormData();data.append('file',file);
  await request('employees/'+v('employee')+'/documents','POST',data,
   {campusId:campus(),type:v('document-type')},true);
 });
 form('leave-type-form',()=>request('leave-types','POST',{
  campusId:campus(),code:v('leave-code'),title:v('leave-title'),annualDays:Number(v('leave-days'))
 }));
 form('leave-form',async()=>{
  if(!v('employee')||!v('leave-type'))throw Error('Select an employee and leave type.');
  await request('leaves','POST',{campusId:campus(),employeeId:v('employee'),
   leaveTypeId:v('leave-type'),from:v('leave-start'),to:v('leave-end'),reason:v('leave-reason')});
 });
 form('salary-form',async()=>{
  if(!v('employee'))throw Error('Select an employee.');
  await request('salaries','POST',{campusId:campus(),employeeId:v('employee'),
   effectiveOn:v('salary-date'),baseAmount:Number(v('base')),allowances:Number(v('allowance')),
   deductions:Number(v('deduction'))});
 });
 $('payroll-form').addEventListener('submit',e=>{e.preventDefault();run(async()=>{
  if(!v('employee'))throw Error('Select an employee.');
  const pay=await request('payroll/preview','POST',null,{
   campusId:campus(),employeeId:v('employee'),month:v('pay-month')+'-01'});
  $('payroll-result').textContent='Gross ₹'+pay.gross+
   ' · Deductions ₹'+pay.deductions+' · Net ₹'+pay.net+
   '. Preview only — no payment has been made.';
  await refresh();
 });});
 $('load-attendance').addEventListener('click',()=>run(async()=>{
  const attendance=await request('attendance','GET',null,{campusId:campus(),date:v('attendance-date')});
  list('attendance',attendance,x=>x.employeeId.slice(0,8)+' · '+x.date+' · '+x.status);
 }));
 $('employee').addEventListener('change',()=>run(async()=>{await refreshDocuments();await refreshBalance();}));
 $('leave-type').addEventListener('change',()=>run(refreshBalance));
 $('campus').addEventListener('change',()=>run(refresh));
 const today=new Date(Date.now()-new Date().getTimezoneOffset()*60000).toISOString().slice(0,10);
 $('joined').value=today;$('salary-date').value=today;$('attendance-date').value=today;
 $('pay-month').value=today.slice(0,7);
 run(async()=>{
  const campuses=await request('campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else notice('Create a campus in Institution Administration first.');
 });
})();