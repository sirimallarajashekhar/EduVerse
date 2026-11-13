// Thin platform presentation. Every privilege, module and tenant decision belongs to the API.
(() => {
 const el=id=>document.getElementById(id);
 const token=el('platform-token').querySelector('[name="__RequestVerificationToken"]').value;
 const root='/bff/platform-control/';
 let current=null,all=[];
 function node(tag,value,cls){const x=document.createElement(tag);x.textContent=value??'';if(cls)x.className=cls;return x;}
 function message(value,error=false){const x=el('platform-message');x.textContent=value;x.className=error?'text-danger':'text-success';}
 async function api(path,body){
  const method=body===undefined?'GET':'POST';
  const response=await fetch(root+path,{method,headers:method==='GET'?{'Accept':'application/json'}:
   {'Content-Type':'application/json','RequestVerificationToken':token},
   body:body===undefined?undefined:JSON.stringify(body)});
  if(!response.ok)throw new Error('API returned '+response.status+'. Check permission, duplicate user or subscription.');
  return response.status===204?null:response.json();
 }
 async function safe(fn){try{await fn();}catch(error){message(error.message,true);}}
 function showInstitutions(){
  const search=el('platform-search').value.toLowerCase().trim();
  const host=el('platform-institutions');host.replaceChildren();
  const filtered=all.filter(x=>(x.name+' '+x.slug).toLowerCase().includes(search));
  if(!filtered.length)host.append(node('p','No matching institutions.','text-secondary'));
  for(const x of filtered){
   const col=node('div','','col-md-6 col-xl-4');
   const btn=node('button','','ev-institution-choice h-100');
   btn.type='button';
   btn.setAttribute('aria-pressed',String(current?.id===x.id));
   const heading=node('span',x.name,'d-block fw-semibold mb-1');
   const subtitle=node('span',x.kind+' · '+x.slug,'d-block small text-secondary mb-3');
   const foot=node('span',x.active?'Active':'Inactive',x.active?'badge text-bg-success':'badge text-bg-secondary');
   btn.append(heading,subtitle,foot);
   btn.addEventListener('click',()=>safe(()=>selectInstitution(x)));
   col.append(btn);host.append(col);
  }
 }
 async function loadModuleCatalog(){
  const host=el('platform-all-modules');
  if(!host)return;
  host.replaceChildren();
  const modules=await api('modules');
  if(el('platform-module-count'))el('platform-module-count').textContent=modules.length+' modules';
  if(!modules.length){
   host.append(node('p','No modules registered.','text-secondary'));
   return;
  }
  for(const module of modules){
   const col=node('div','','col-md-6 col-xl-4');
   const card=node('article','','ev-card ev-module-tile p-4 h-100');
   card.dataset.evColor=({finance:'green',reporting:'green',students:'purple',hrms:'purple',operations:'amber',ams:'amber',learning:'rose'})[module.id]||'blue';
   card.append(node('span','▦','ev-module-symbol'));
   card.append(node('h3',module.name,'h6 fw-semibold mb-2'));
   card.append(node('p',module.description,'text-secondary small mb-3'));
   card.append(node('span','Select an institution to check access','ev-micro-label'));
   col.append(card);host.append(col);
  }
 }
 async function refresh(){
  await loadModuleCatalog();
  all=await api('institutions');
  el('platform-total').textContent=all.length;
  el('platform-active').textContent=all.filter(x=>x.active).length;
  el('platform-members').textContent=all.reduce((total,x)=>total+x.users,0);
  showInstitutions();message('Platform inventory refreshed.');
 }
 async function selectInstitution(institution){
  current=institution;el('platform-selected').hidden=false;
  el('platform-selected-name').textContent=institution.name;
  el('platform-selected-scope').textContent='Institution: '+institution.slug+' | '+institution.kind+
    ' | Plan: '+(institution.plan||'Unassigned');
  const [moduleInfo,users]=await Promise.all([
   api('institutions/'+institution.id+'/modules'),
   api('institutions/'+institution.id+'/users')]);
  showInstitutions();
  if (!['#platform-modules','#platform-users','#platform-invite'].includes(location.hash))
   location.hash='platform-modules';
  document.querySelector('.ev-module-workspace')?.dispatchEvent(new Event('ev:refresh-action'));
  const modules=el('platform-modules');modules.replaceChildren();
  for(const x of moduleInfo.modules){
   const item=node('div','','py-2 border-bottom');
   item.append(node('span',x.name+' · '+(x.enabled?'Enabled':'Unavailable'),
     x.enabled?'':'text-secondary'));
   // Platform view deliberately doesn't link to tenant-only admin pages.
   modules.append(item);
  }
  const userBox=el('platform-users');userBox.replaceChildren();
  if(!users.length)userBox.append(node('p','No institution users.','text-secondary'));
  for(const user of users){
   const line=node('div','','border-bottom py-2');
   line.append(node('span',user.displayName+' · '+user.email+' · '+user.role+
     ' · '+(user.active?'Active':'Inactive'),'me-2'));
   if(!user.active){
    const approve=node('button','Approve / reactivate','btn btn-outline-success btn-sm');
    approve.type='button';approve.addEventListener('click',()=>safe(async()=>{
     const reason=window.prompt('Approval reason (recorded in platform audit):');
     if(!reason||!reason.trim())return;
     await api('institutions/'+institution.id+'/users/'+user.id+'/approve',
       {tenantId:institution.id,reason:reason.trim()});
     message('User membership approved and activated.');
     await selectInstitution(institution);
    }));
    line.append(approve);
   }
   if(user.active){
    const btn=node('button','Deactivate','btn btn-outline-danger btn-sm');
    btn.type='button';btn.addEventListener('click',()=>safe(async()=>{
     const reason=window.prompt('Reason for deactivation (audit required):');
     if(!reason||!reason.trim())return;
     await api('institutions/'+institution.id+'/users/'+user.id+'/deactivate',
       {tenantId:institution.id,reason:reason.trim()});
     message('User deactivated and active tenant sessions revoked.');
     await selectInstitution(institution);
    }));
    line.append(btn);
   }
   userBox.append(line);
  }
 }
 let systemActions=[];
 function showActions(){
  const q=el('platform-actions-search').value.trim().toLowerCase();
  const host=el('platform-actions');host.replaceChildren();
  const rows=systemActions.filter(x=>(x.module+' '+x.method+' '+x.route).toLowerCase().includes(q));
  el('platform-actions-count').textContent=rows.length+' of '+systemActions.length+' registered API actions';
  for(const x of rows){
   const item=node('div','','border-bottom py-2 d-flex flex-wrap gap-2 align-items-center');
   item.append(node('span',x.method,'badge text-bg-secondary'));
   item.append(node('code',x.route,'small'));
   item.append(node('span',x.platformManaged?'Platform-managed':'Tenant authorization required',
    x.platformManaged?'badge text-bg-success':'small text-secondary'));
   host.append(item);
  }
 }
 el('platform-actions-search').addEventListener('input',showActions);
 el('platform-load-actions').addEventListener('click',()=>safe(async()=>{
  systemActions=await api('actions');showActions();message('API action inventory loaded.');
 }));
 el('platform-search').addEventListener('input',showInstitutions);
 el('platform-refresh').addEventListener('click',()=>safe(refresh));
 el('platform-invite').addEventListener('submit',event=>{
  event.preventDefault();safe(async()=>{
   if(!current)throw new Error('Select an institution first.');
   const result=await api('institutions/'+current.id+'/users',{
    tenantId:current.id,email:el('invite-email').value.trim(),
    displayName:el('invite-name').value.trim(),roleCode:el('invite-role').value
   });
   message(result.activationEmailSent?'Invitation created and activation email sent.':
     'Account created, but activation email could not be sent. Configure SMTP and request a password reset.',
     !result.activationEmailSent);
   el('platform-invite').reset();await selectInstitution(current);await refresh();
  });
 });
 safe(refresh);
})();
