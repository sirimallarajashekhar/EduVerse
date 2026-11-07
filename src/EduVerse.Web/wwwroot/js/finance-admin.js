// Presentation-only. Money and permissions are validated by the backend API.
(() => {
 const $=id=>document.getElementById(id), val=id=>$(id).value, campus=()=>val('campus');
 const token=document.querySelector('[name="__RequestVerificationToken"]').value;
 const message=t=>{$('status').textContent=t;};
 let years=[], fees=[],charges=[],payments=[],students=[],enrollments=[];
 const url=(path,params={})=>{
  const u=new URL(path,window.location.origin);
  for(const [key,value] of Object.entries(params))if(value!==undefined&&value!==null)u.searchParams.set(key,value);
  return u.pathname+u.search;
 };
 async function get(path) {
  const response=await fetch(path,{headers:{Accept:'application/json'}});
  if(!response.ok)throw Error('Unable to read finance reference data ('+response.status+').');
  return response.json();
 }
 async function api(path,method='GET',data=null,key=null) {
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=token;
  if(data)headers['Content-Type']='application/json';
  if(key)headers['Idempotency-Key']=key;
  const response=await fetch(url('/bff/finance/'+path,method==='GET'?{campusId:campus()}:{}),
   {method,headers,...(data?{body:JSON.stringify(data)}:{})});
  if(!response.ok)throw Error('Finance request rejected ('+response.status+').');
  return response.status===204?null:response.json();
 }
 const money=n=>'₹'+Number(n).toFixed(2);
 function select(id,items,label,emptyLabel){
  const host=$(id);host.replaceChildren();
  if(emptyLabel){const opt=document.createElement('option');opt.value='';opt.textContent=emptyLabel;host.append(opt);}
  for(const item of items){const opt=document.createElement('option');opt.value=item.id;opt.textContent=label(item);host.append(opt);}
 }
 function list(id,items,label){
  const host=$(id);host.replaceChildren();
  for(const item of items){const li=document.createElement('li');li.textContent=label(item);host.append(li);}
 }
 async function updateInstallments(){
  const charge=val('payment-charge');if(!charge){select('installment',[],x=>x.id,'Select installment');return;}
  const rows=await api('charges/'+charge+'/installments');
  select('installment',rows,i=>'#'+i.number+' · '+money(i.balance)+' outstanding · due '+i.dueOn,'Select installment');
 }
 async function refresh(){
  if(!campus())return;
  [fees,charges,payments]=await Promise.all([api('fees'),api('charges'),api('payments')]);
  select('fee',fees,x=>x.title+' · '+money(x.amount),'Select fee');
  select('concession-charge',charges,x=>x.id.slice(0,8)+' · '+money(x.balance),'Select fee assignment');
  select('payment-charge',charges,x=>x.id.slice(0,8)+' · '+money(x.balance),'Select fee assignment');
  select('refund-payment',payments,x=>x.receiptNumber+' · '+money(x.amount),'Select payment');
  select('reconcile-payment',payments.filter(x=>x.reconciliationStatus==='PENDING'),
   x=>x.receiptNumber,'Select pending payment');
  list('fee-list',fees,x=>x.code+' — '+x.title+' · '+money(x.amount));
  await updateInstallments();
  const stats=await api('dashboard');
  $('dashboard').textContent='Fees '+stats.fees+' · Assignments '+stats.charges+
   ' · Gross '+money(stats.gross)+' · Concessions '+money(stats.concessions)+
   ' · Collected '+money(stats.collected)+' · Refunded '+money(stats.refunded)+
   ' · Outstanding '+money(stats.outstanding)+' · Estimated overdue '+money(stats.overdue);
  const body=$('payments');body.replaceChildren();
  for(const item of payments){
   const row=document.createElement('tr');
   for(const text of [item.receiptNumber,money(item.amount),item.reconciliationStatus]){
    const cell=document.createElement('td');cell.textContent=text;row.append(cell);
   }
   const cell=document.createElement('td'),view=document.createElement('button');
   view.type='button';view.className='btn btn-sm btn-outline-primary';view.textContent='View receipt';
   view.addEventListener('click',()=>run(async()=>{
    const receipt=await api('payments/'+item.id+'/receipt');
    alert(['EduVerse · Fee Receipt',receipt.receiptNumber,receipt.institution,receipt.student,
     'Admission: '+receipt.admissionNumber,receipt.fee,money(receipt.amount),
     'Method: '+receipt.method,'Collected: '+receipt.collectedUtc].join('\n'));
   }));
   cell.append(view);row.append(cell);body.append(row);
  }
 }
 async function refreshReferences(){
  const id=campus();
  [years,students]=await Promise.all([
   get(url('/bff/academic/years',{campusId:id})),
   get(url('/bff/students',{campusId:id}))
  ]);
  enrollments=(await Promise.all(students.map(async s=>({
   student:s,rows:await get(url('/bff/students/'+s.id+'/enrollments',{campusId:id}))
  })))).flatMap(x=>x.rows.filter(e=>e.status==='ACTIVE').map(e=>({
   ...e,name:x.student.givenName+' '+x.student.familyName
  })));
  select('year',years,x=>x.name,'Select year');
  select('enrollment',enrollments,x=>x.name+' · '+x.academicYearId.slice(0,8),'Select enrollment');
  await refresh();
 }
 const run=async work=>{try{await work();message('Finance dashboard ready.');}catch(e){message(e.message);}};
 function submit(id,fn){
  $(id).addEventListener('submit',e=>{e.preventDefault();run(async()=>{
   await fn();e.target.reset();await refresh();
  });});
 }
 function installments(total,count,due){
  // Integer paise allocation preserves the exact gross and avoids binary rounding.
  const paisa=Math.round(Number(total)*100),base=Math.floor(paisa/count);
  return Array.from({length:count},(_,i)=>{
   const date=new Date(due+'T00:00:00');
   date.setMonth(date.getMonth()+i);
   const dateLabel=[date.getFullYear(),String(date.getMonth()+1).padStart(2,'0'),
    String(date.getDate()).padStart(2,'0')].join('-');
   return {number:i+1,dueOn:dateLabel,amount:(base+(i===count-1?paisa-base*count:0))/100};
  });
 }
 submit('fee-form',()=>api('fees','POST',{
  campusId:campus(),academicYearId:val('year'),code:val('fee-code'),
  title:val('fee-title'),amount:Number(val('fee-amount'))
 }));
 submit('charge-form',()=>{
  const fee=fees.find(x=>x.id===val('fee'));
  if(!fee)throw Error('Choose a fee.');
  const count=Number(val('installment-count'));
  if(Math.round(Number(fee.amount)*100)<count)throw Error('Too many installments for this fee.');
  return api('charges','POST',{campusId:campus(),enrollmentId:val('enrollment'),
   feeStructureId:fee.id,installments:installments(fee.amount,count,val('due'))});
 });
 submit('concession-form',()=>api('concessions','POST',{
  campusId:campus(),chargeId:val('concession-charge'),category:val('concession-category'),
  reason:val('concession-reason'),amount:Number(val('concession-amount'))
 }));
 submit('payment-form',async()=>{
  const request={campusId:campus(),installmentId:val('installment'),
   amount:Number(val('payment-amount')),method:val('payment-method'),
   reference:val('payment-ref')||null};
  // One key per operator submission; retries must reuse the same key by callers.
  await api('payments','POST',request,crypto.randomUUID());
 });
 submit('refund-form',async()=>{
  if(!confirm('Confirm the actual offline refund has already been issued?'))return;
  await api('refunds','POST',{campusId:campus(),paymentId:val('refund-payment'),
   amount:Number(val('refund-amount')),reason:val('refund-reason'),
   reference:val('refund-ref')},crypto.randomUUID());
 });
 submit('reconcile-form',()=>api('reconciliation','POST',{
  campusId:campus(),paymentId:val('reconcile-payment'),status:val('reconcile-status'),reference:null
 }));
 $('payment-charge').addEventListener('change',()=>run(updateInstallments));
 $('campus').addEventListener('change',()=>run(refreshReferences));
 run(async()=>{
  const campuses=await get('/bff/institution-admin/campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refreshReferences();}
  else message('Create an institution campus first.');
 });
})();