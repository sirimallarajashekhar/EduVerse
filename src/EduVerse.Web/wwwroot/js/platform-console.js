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
   const col=node('div','','col-md-6 col-xl-4');const btn=node('button',
    x.name+' · '+x.kind+' · '+x.slug+' · '+(x.active?'Active':'Inactive'),
    'btn btn-outline-primary text-start w-100 h-100');
   btn.type='button';btn.addEventListener('click',()=>safe(()=>selectInstitution(x)));col.append(btn);host.append(col);
  }
 }
 async function refresh(){
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
 el('platform-search').addEventListener('input',showInstitutions);
 el('platform-refresh').addEventListener('click',()=>safe(refresh));
 el('platform-invite').addEventListener('submit',event=>{
  event.preventDefault();safe(async()=>{
   if(!current)throw new Error('Select an institution first.');
   await api('institutions/'+current.id+'/users',{
    tenantId:current.id,email:el('invite-email').value.trim(),
    displayName:el('invite-name').value.trim(),roleCode:el('invite-role').value
   });
   message('Invitation created; user must complete secure activation.');
   el('platform-invite').reset();await selectInstitution(current);await refresh();
  });
 });
 safe(refresh);
})();
