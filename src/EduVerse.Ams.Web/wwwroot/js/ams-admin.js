// Thin API-only AMS presentation. All authorization, balances and lifecycle validation are server-side.
(() => {
 const $=id=>document.getElementById(id),value=id=>$(id).value,campus=()=>value('campus');
 const csrf=document.querySelector('input[name="__RequestVerificationToken"]').value;
 const show=message=>{$('status').textContent=message;};
 let categories=[],assets=[],vendors=[],stock=[],orders=[],purchases=[],locations=[],departments=[],employees=[];
 function path(segment,query={}) {
  const u=new URL('/ams/bff/'+segment,location.origin);
  for(const [k,v] of Object.entries(query))if(v!==null&&v!==undefined)u.searchParams.set(k,v);
  return u.pathname+u.search;
 }
 async function request(segment,method='GET',body=null,params={},key=null) {
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(body)headers['Content-Type']='application/json';
  if(key)headers['Idempotency-Key']=key;
  if(method==='GET'&&segment!=='campuses')params.campusId=campus();
  const response=await fetch(path(segment,params),{method,headers,...(body?{body:JSON.stringify(body)}:{})});
  if(!response.ok) {
   const data=await response.json().catch(()=>null);
   throw new Error(data?.message||'AMS request rejected ('+response.status+').');
  }
  return response.status===204?null:response.json();
 }
 function select(id,items,label,empty='Select') {
  const node=$(id);node.replaceChildren();
  const first=document.createElement('option');first.value='';first.textContent=empty;node.append(first);
  for(const row of items){const o=document.createElement('option');o.value=row.id;o.textContent=label(row);node.append(o);}
 }
 function list(id,items,label) {
  const node=$(id);node.replaceChildren();
  for(const item of items){const li=document.createElement('li');li.textContent=label(item);node.append(li);}
 }
 async function run(job) {try{await job();show('AMS dashboard ready.');}catch(e){show(e.message);}}
 function form(id,job){$(id).addEventListener('submit',e=>{e.preventDefault();run(async()=>{
  await job();e.target.reset();await refresh();
 });});}
 const money=n=>'₹'+Number(n).toFixed(2);
 async function loadEvents(){
  if(!value('asset'))return $('asset-events').replaceChildren();
  const rows=await request('assets/'+value('asset')+'/events');
  list('asset-events',rows,r=>r.eventCode+' · '+(r.note||'')+' · '+r.createdUtc);
 }
 async function refresh(){
  if(!campus())return;
  [categories,assets,vendors,stock,orders,purchases,locations,departments,employees]=await Promise.all([
   request('categories'),request('assets'),request('vendors'),request('stock'),
   request('maintenance'),request('purchases'),
   request('locations/buildings'),request('locations/departments'),request('employees')
  ]);
  select('asset-category',categories,r=>r.name,'Select category');
  select('asset',assets,r=>r.assetCode+' · '+r.name+' · '+r.status,'Select asset');
  select('building',locations,r=>r.name,'No building');
  select('department',departments,r=>r.name,'No department');
  select('employee',employees,r=>r.givenName+' '+r.familyName,'No employee');
  select('movement-stock',stock,r=>r.sku+' · '+r.name+' ('+r.quantity+')','Select stock item');
  select('purchase-stock',stock,r=>r.sku+' · '+r.name,'Select item');
  select('purchase-vendor',vendors,r=>r.name,'Select vendor');
  list('category-list',categories,r=>r.code+' · '+r.name);
  list('vendor-list',vendors,r=>r.name+(r.email?' · '+r.email:''));
  list('stock-items',stock,r=>r.sku+' · '+r.name+' · '+r.quantity+' '+r.unit+(r.lowStock?' · LOW STOCK':''));
  list('purchases',purchases,r=>r.status+' · '+r.quantity+' × '+money(r.unitPrice)+' = '+money(r.total));
  const workList=$('work-orders');workList.replaceChildren();
  for(const order of orders){
   const li=document.createElement('li');
   li.append(document.createTextNode(order.title+' · '+order.dueOn+' · '+order.status+' '));
   if(order.status==='OPEN'){
    const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-outline-primary';
    btn.textContent='Complete';btn.addEventListener('click',()=>run(async()=>{
     await request('maintenance/'+order.id+'/complete','POST',null,{campusId:campus()});
     await refresh();
    }));
    li.append(btn);
   }
   workList.append(li);
  }
  const dashboard=await request('dashboard');
  $('metrics').textContent='Assets '+dashboard.assets+' · Allocated '+dashboard.allocated+
   ' · Open maintenance '+dashboard.maintenanceOpen+
   ' · Low stock '+dashboard.lowStockItems+' · Purchases '+dashboard.purchaseOrders;
  await loadEvents();
 }
 form('category-form',()=>request('categories','POST',{
  campusId:campus(),code:value('category-code'),name:value('category-name')
 }));
 form('asset-form',()=>request('assets','POST',{
  campusId:campus(),categoryId:value('asset-category'),name:value('asset-name'),
  serialNumber:value('serial')||null,acquiredOn:value('acquired')||null,
  purchaseAmount:value('asset-cost')===''?null:Number(value('asset-cost'))
 }));
 form('allocate-form',async()=>{
  if(!value('asset'))throw new Error('Select an asset.');
  await request('assets/allocate','POST',{
   campusId:campus(),assetId:value('asset'),buildingId:value('building')||null,
   departmentId:value('department')||null,employeeId:value('employee')||null,
   note:value('allocation-note')||null});
 });
 for(const btn of document.querySelectorAll('.asset-action'))btn.addEventListener('click',()=>run(async()=>{
  if(!value('asset'))throw new Error('Select an asset.');
  if(btn.dataset.action==='DISPOSE'&&!confirm('Permanently dispose of this retired asset?'))return;
  await request('assets/lifecycle','POST',{
   campusId:campus(),assetId:value('asset'),action:btn.dataset.action,note:null});
  await refresh();
 }));
 form('maintenance-form',async()=>{
  if(!value('asset'))throw new Error('Select an asset.');
  await request('maintenance','POST',{campusId:campus(),assetId:value('asset'),
   title:value('work-title'),dueOn:value('work-due')});
 });
 form('stock-form',()=>request('stock','POST',{campusId:campus(),
  sku:value('sku'),name:value('stock-name'),unit:value('stock-unit'),reorderLevel:Number(value('reorder'))}));
 form('movement-form',()=>request('stock/movements','POST',{
  campusId:campus(),stockId:value('movement-stock'),type:value('movement-type'),
  quantity:Number(value('movement-qty')),reference:value('movement-reference')||null
 },{},crypto.randomUUID()));
 form('vendor-form',()=>request('vendors','POST',{campusId:campus(),
  name:value('vendor-name'),email:value('vendor-email')||null}));
 form('purchase-form',()=>request('purchases','POST',{
  campusId:campus(),vendorId:value('purchase-vendor'),stockId:value('purchase-stock'),
  quantity:Number(value('purchase-quantity')),unitPrice:Number(value('purchase-price'))
 }));
 $('campus').addEventListener('change',()=>run(refresh));
 $('asset').addEventListener('change',()=>run(loadEvents));
 run(async()=>{
  const campuses=await request('campuses');
  select('campus',campuses,r=>r.name+' ('+r.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else show('Create an institution campus first.');
 });
})();