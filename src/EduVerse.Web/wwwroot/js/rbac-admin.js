// Presentation only: RBAC authorization and validation are owned by the API.
(async function () {
 const $=id=>document.getElementById(id);
 const csrf=$('rbac-token-form').querySelector('[name="__RequestVerificationToken"]').value;
 const feedback=$('rbac-feedback');
 let roles=[],users=[],assignments=[],permissions=[];
 async function api(path,method='GET',body=null) {
  const options={method,headers:{Accept:'application/json'}};
  if(method!=='GET')options.headers.RequestVerificationToken=csrf;
  if(body!==null){options.headers['Content-Type']='application/json';options.body=JSON.stringify(body);}
  const response=await fetch('/bff/'+path,options);
  if(!response.ok)throw new Error('Request rejected ('+response.status+').');
  if(response.status===204)return null;
  return response.json();
 }
 function options(select,items,value,text) {
  select.replaceChildren();
  for(const item of items) {
   const option=document.createElement('option');
   option.value=value(item);option.textContent=text(item);select.append(option);
  }
 }
 function message(value,failed=false) {feedback.textContent=value;feedback.className=failed?'mt-3 text-danger':'mt-3 text-success';}
 async function load() {
  [roles,users,assignments,permissions]=await Promise.all([
   api('rbac/roles'),api('identity/users'),api('rbac/assignments'),api('rbac/catalog')
  ]);
  const selected=$('rbac-edit-role').value;
  options($('rbac-edit-role'),roles,x=>x.code,x=>x.displayName+' ('+x.code+')');
  if(roles.some(x=>x.code===selected))$('rbac-edit-role').value=selected;
  options($('rbac-assign-role'),roles,x=>x.code,x=>x.displayName);
  options($('rbac-user'),users.filter(x=>x.roleCode==='MEMBER'),x=>x.id,x=>x.displayName+' ('+x.email+')');
  showPermissions();showAssignments();
 }
 function showPermissions() {
  const current=roles.find(x=>x.code===$('rbac-edit-role').value);
  const host=$('rbac-permission-options');host.replaceChildren();
  for(const permission of permissions) {
   const wrap=document.createElement('div');wrap.className='col-md-6';
   const label=document.createElement('label');label.className='form-check';
   const input=document.createElement('input');input.type='checkbox';input.value=permission;
   input.className='form-check-input';input.checked=current?.permissions.includes(permission)??false;
   const name=document.createElement('span');name.className='form-check-label';name.textContent=permission;
   label.append(input,name);wrap.append(label);host.append(wrap);
  }
 }
 function showAssignments() {
  const host=$('rbac-assignments');host.replaceChildren();
  for(const item of assignments) {
   const row=document.createElement('div');row.className='d-flex align-items-center justify-content-between py-2 border-bottom';
   const user=users.find(u=>u.id===item.subjectId);
   const name=document.createElement('span');name.textContent=(user?.displayName||item.subjectId)+' — '+item.roleCode;
   const remove=document.createElement('button');remove.type='button';remove.className='btn btn-sm btn-outline-danger';
   remove.textContent='Revoke';
   remove.addEventListener('click',async()=>{
    if(!confirm('Revoke role '+item.roleCode+' from this member?'))return;
    try{await api('rbac/assignments/'+encodeURIComponent(item.subjectId)+'/'+encodeURIComponent(item.roleCode),'DELETE');
     message('Role revoked. Affected user sessions were invalidated.');await load();}
    catch(e){message(e.message,true);}
   });
   row.append(name,remove);host.append(row);
  }
  if(!assignments.length)host.textContent='No custom role assignments yet.';
 }
 $('rbac-edit-role').addEventListener('change',showPermissions);
 $('rbac-create').addEventListener('submit',async event=>{
  event.preventDefault();
  try{await api('rbac/roles','POST',{code:$('rbac-code').value,displayName:$('rbac-name').value});
   $('rbac-create').reset();message('Role created.');await load();}
  catch(e){message(e.message,true);}
 });
 $('rbac-assign').addEventListener('submit',async event=>{
  event.preventDefault();
  try{await api('rbac/assignments','POST',{userId:$('rbac-user').value,roleCode:$('rbac-assign-role').value});
   message('Role assigned; member must sign in again.');await load();}
  catch(e){message(e.message,true);}
 });
 $('rbac-permissions').addEventListener('submit',async event=>{
  event.preventDefault();
  const code=$('rbac-edit-role').value;
  if(!code)return;
  const selected=Array.from($('rbac-permission-options').querySelectorAll('input:checked')).map(x=>x.value);
  try{await api('rbac/roles/'+encodeURIComponent(code)+'/permissions','PUT',{permissions:selected});
   message('Permissions updated; affected users must sign in again.');await load();}
  catch(e){message(e.message,true);}
 });
 try{await load();}catch(e){message('RBAC service unavailable: '+e.message,true);}
})();
