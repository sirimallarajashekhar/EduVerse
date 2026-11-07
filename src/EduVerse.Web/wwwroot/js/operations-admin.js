// Presentation only; the REST API is the authority for lending, fines, capacity and occupancy.
(() => {
 const $=id=>document.getElementById(id),v=id=>$(id).value,campus=()=>v('campus');
 const csrf=document.querySelector('input[name="__RequestVerificationToken"]').value;
 let data={};
 const fail=e=>{$('status').textContent=e.message;};
 const run=async job=>{try{await job();$('status').textContent='Campus operations loaded.';}catch(e){fail(e);}};
 const link=(base,query={})=>{
  const u=new URL(base,location.origin);
  for(const [k,val] of Object.entries(query))
   if(val!==null&&val!==undefined)u.searchParams.set(k,val);
  return u.pathname+u.search;
 };
 async function api(name,method='GET',body=null,parameters={}) {
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(body)headers['Content-Type']='application/json';
  if(method==='GET')parameters.campusId=campus();
  const response=await fetch(link('/bff/operations/'+name,parameters),{
   method,headers,...(body?{body:JSON.stringify(body)}:{})});
  if(!response.ok) {
   const error=await response.json().catch(()=>null);
   throw new Error(error?.message||'Operation rejected ('+response.status+').');
  }
  return response.status===204?null:response.json();
 }
 async function external(resource,params={}) {
  const response=await fetch(link(resource,params));
  if(!response.ok)throw new Error('Unable to load campus/student list ('+response.status+').');
  return response.json();
 }
 function select(id,rows,label,empty='Select an option') {
  const host=$(id);host.replaceChildren();
  const start=document.createElement('option');start.value='';start.textContent=empty;host.append(start);
  for(const r of rows){const o=document.createElement('option');o.value=r.id;o.textContent=label(r);host.append(o);}
 }
 function list(id,rows,label) {
  const host=$(id);host.replaceChildren();
  for(const r of rows){const li=document.createElement('li');li.textContent=label(r);host.append(li);}
 }
 const studentName=id=>{
  const x=(data.students||[]).find(s=>s.id===id);
  return x?x.givenName+' '+x.familyName:id.slice(0,8);
 };
 function actionList(id,rows,label,action,resource) {
  const host=$(id);host.replaceChildren();
  for(const x of rows){
   const li=document.createElement('li');
   li.append(document.createTextNode(label(x)+' '));
   if(x.status==='ACTIVE'||x.status==='OPEN'){
    const button=document.createElement('button');
    button.type='button';button.className='btn btn-sm btn-outline-primary';
    button.textContent=action==='return'?'Return':action==='vacate'?'Vacate':'Release';
    button.addEventListener('click',()=>run(async()=>{
     await api(resource+'/'+x.id+'/'+action,'POST',null,{campusId:campus()});
     await refresh();
    }));
    li.append(button);
   }
   host.append(li);
  }
 }
 function updateStops(){
  select('assign-stop',(data.stops||[]).filter(x=>x.routeId===v('assign-route')),x=>x.name+' (#'+x.order+')','Choose pickup');
 }
 async function refresh(){
  if(!campus())return;
  const keys=['titles','copies','loans','reservations','vehicles','routes','stops',
   'route-assignments','hostel-blocks','hostel-rooms','hostel-beds','hostel-allocations','dashboard'];
  const results=await Promise.all([...keys.map(k=>api(k)),
   external('/bff/students',{campusId:campus()})]);
  keys.forEach((k,i)=>{data[k]=results[i];});
  data.students=results.at(-1).filter(s=>s.status==='ACTIVE');
  const students=data.students;
  const studentLabel=s=>s.givenName+' '+s.familyName+' · '+s.admissionNumber;
  for(const id of ['loan-student','reserve-student','assign-student','hostel-student'])
   select(id,students,studentLabel,'Select student');
  for(const id of ['copy-title','reserve-title'])
   select(id,data.titles,x=>x.title+' · '+x.author,'Select title');
  select('loan-copy',data.copies.filter(x=>x.status==='AVAILABLE'),x=>x.barcode,'Select available copy');
  select('route-vehicle',data.vehicles,x=>x.registration+' · '+x.capacity+' seats','Select vehicle');
  for(const id of ['stop-route','assign-route'])
   select(id,data.routes,x=>x.code+' · '+x.name,'Select route');
  updateStops();
  select('room-block',data['hostel-blocks'],x=>x.code+' · '+x.name,'Select block');
  select('bed-room',data['hostel-rooms'],x=>x.number,'Select room');
  select('hostel-bed',data['hostel-beds'].filter(x=>!x.occupied),x=>x.number,'Select available bed');
  list('beds-list',data['hostel-beds'],x=>x.number+(x.occupied?' · occupied':' · available'));
  actionList('loan-list',data.loans,
   x=>studentName(x.studentId)+' · '+(data.copies.find(c=>c.id===x.copyId)?.barcode||x.copyId.slice(0,8))+
    ' · due '+x.dueOn+' · '+x.status+(x.status==='RETURNED'?' · fine ₹'+x.fine:''),
   'return','loans');
  list('reservation-list',data.reservations,x=>
   studentName(x.studentId)+' · '+(data.titles.find(t=>t.id===x.titleId)?.title||x.titleId.slice(0,8))+
   ' · '+x.status);
  actionList('route-assignments',data['route-assignments'],
   x=>studentName(x.studentId)+' · '+(data.routes.find(r=>r.id===x.routeId)?.name||'route')+' · '+x.status,
   'release','route-assignments');
  actionList('hostel-allocations',data['hostel-allocations'],
   x=>studentName(x.studentId)+' · '+(data['hostel-beds'].find(b=>b.id===x.bedId)?.number||'bed')+' · '+x.status,
   'vacate','hostel-allocations');
  const s=data.dashboard;
  $('summary').textContent='Library: '+s.titles+' titles, '+s.copiesAvailable+' available, '+s.loansOpen+
   ' lent, '+s.overdueLoans+' overdue; returned fines ₹'+s.outstandingFines+
   '. Transport: '+s.transportRoutes+' routes, '+s.transportAssigned+
   ' students. Hostel: '+s.hostelOccupied+'/'+s.hostelBeds+' beds occupied.';
 }
 function form(name,job) {
  $(name).addEventListener('submit',event=>{event.preventDefault();run(async()=>{
   await job();event.target.reset();await refresh();
  });});
 }
 form('title-form',()=>api('titles','POST',{campusId:campus(),title:v('title'),
  author:v('author'),isbn:v('isbn')||null}));
 form('copy-form',()=>api('copies','POST',{campusId:campus(),titleId:v('copy-title'),barcode:v('barcode')}));
 form('loan-form',()=>api('loans','POST',{campusId:campus(),copyId:v('loan-copy'),
  studentId:v('loan-student'),dueOn:v('loan-due')}));
 form('reservation-form',()=>api('reservations','POST',{campusId:campus(),titleId:v('reserve-title'),
  studentId:v('reserve-student')}));
 form('vehicle-form',()=>api('vehicles','POST',{campusId:campus(),registration:v('registration'),
  capacity:Number(v('capacity')),driverName:v('driver'),driverPhone:v('driver-phone')||null}));
 form('route-form',()=>api('routes','POST',{campusId:campus(),vehicleId:v('route-vehicle'),
  code:v('route-code'),name:v('route-name')}));
 form('stop-form',()=>api('stops','POST',{campusId:campus(),routeId:v('stop-route'),
  name:v('stop-name'),order:Number(v('stop-order'))}));
 form('route-assignment-form',()=>api('route-assignments','POST',{campusId:campus(),
  studentId:v('assign-student'),routeId:v('assign-route'),stopId:v('assign-stop')}));
 form('block-form',()=>api('hostel-blocks','POST',{campusId:campus(),code:v('block-code'),name:v('block-name')}));
 form('room-form',()=>api('hostel-rooms','POST',{campusId:campus(),blockId:v('room-block'),number:v('room-number')}));
 form('bed-form',()=>api('hostel-beds','POST',{campusId:campus(),roomId:v('bed-room'),number:v('bed-number')}));
 form('hostel-allocation-form',()=>api('hostel-allocations','POST',{campusId:campus(),
  studentId:v('hostel-student'),bedId:v('hostel-bed')}));
 $('assign-route').addEventListener('change',updateStops);
 $('campus').addEventListener('change',()=>run(refresh));
 run(async()=>{
  const campuses=await external('/bff/institution-admin/campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else $('status').textContent='Create an institution campus first.';
 });
})();