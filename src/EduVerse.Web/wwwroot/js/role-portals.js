// Role-specific presentation only. All permissions and row ownership are verified by the REST API.
(async () => {
 const el = id => document.getElementById(id);
 const notice = (message, error=false) => {
  if(el('portal-message')){ el('portal-message').textContent=message;
    el('portal-message').className=error?'mt-3 text-danger':'mt-3 text-success';}
 };
 const route=window.location.pathname;
 const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
 const token=el('portal-token')?.querySelector('[name="__RequestVerificationToken"]')?.value;
 async function api(path,method='GET',body) {
  const headers={Accept:'application/json'};
  if(method!=='GET') {headers.RequestVerificationToken=token; headers['Content-Type']='application/json';}
  const response=await fetch('/bff/portals/'+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body)});
  if(!response.ok)throw new Error('API denied the request ('+response.status+').');
  return response.status===204?null:response.json();
 }
 function make(tag,text,className) {const node=document.createElement(tag);node.textContent=text??'';
  if(className)node.className=className;return node;}
 function renderList(host,items,description) {
  host.replaceChildren();
  if(!items.length){host.append(make('p','No '+description+' found.','text-secondary'));return;}
  const list=make('ul',null,'list-group list-group-flush');
  for(const item of items)list.append(make('li',item,'list-group-item px-0'));
  host.append(list);
 }
 function query(campus){return '?campusId='+encodeURIComponent(campus);}
 async function loadOwn(kind) {
  const links=await api(kind==='parent'?'parent/children':'student/me');
  const host=el('links');host.replaceChildren();
  if(!links.length){host.append(make('p','No linked profiles. Ask your institution administrator to verify your account.'));return;}
  for(const link of links) {
   const button=make('button','Open student '+link.studentId,'btn btn-outline-primary btn-sm me-2 mb-2');
   button.type='button';button.addEventListener('click',()=>openStudent(kind,link));
   host.append(button);
  }
  if(links.length===1)await openStudent(kind,links[0]);
 }
 async function openStudent(kind,link) {
  try {
   const base=kind==='parent'?'parent/children/'+link.studentId:'student/'+link.studentId;
   const suffix=query(link.campusId);
   const sections=kind==='parent'?['attendance','results','notices']:
    ['attendance','results','timetable','assignments','notices'];
   const data=await Promise.all(sections.map(section=>api(base+'/'+section+suffix)));
   sections.forEach((section,i)=>{
    const items=data[i];const rows=items.map(x=>{
     if(section==='attendance')return x.date+' · '+x.status+' ('+x.scope+')';
     if(section==='results')return x.exam+' · '+x.percentage+'% · '+(x.passed?'Passed':'Not passed');
     if(section==='notices')return x.title+' — '+x.body;
     if(section==='timetable')return 'Day '+x.dayOfWeek+' · '+x.class+' · '+x.subject+
        ' ('+x.startsAt+'–'+x.endsAt+')';
     return x.title+' · Due '+new Date(x.dueUtc).toLocaleDateString()+' · '+x.instructions;
    });
    renderList(el(section),rows,section);
   });
   notice('Student records loaded.');
  } catch(e){notice(e.message,true);}
 }
 async function loadTeacher() {
  const campus=el('campus').value.trim();
  if(!uuid.test(campus)){notice('Enter a valid campus UUID.',true);return;}
  try{
   const [classes,lessons]=await Promise.all([
    api('teacher/classes'+query(campus)),api('teacher/lessons'+query(campus))
   ]);
   const host=el('classes');host.replaceChildren();
   for(const item of classes){
    const button=make('button','View class '+item.classId,'btn btn-outline-secondary btn-sm me-2 mb-2');
    button.type='button';
    button.addEventListener('click',async()=>{
     try {
      const rows=await api('teacher/classes/'+item.classId+'/students'+query(campus));
      renderList(el('roster'),rows.map(x=>x.givenName+' '+x.familyName+' ('+x.admissionNumber+')'),'students');
     }catch(e){notice(e.message,true);}
    });host.append(button);
   }
   if(!classes.length)host.append(make('p','No assigned classes.','text-secondary'));
   renderList(el('lessons'),lessons.map(x=>x.title+' · '+x.status),'lessons');
   notice('Assigned teaching records loaded.');
  }catch(e){notice(e.message,true);}
 }
 function validateCampus(){const campus=el('campus').value.trim();
  if(!uuid.test(campus))throw new Error('Enter a valid campus UUID first.');return campus;}
 function requireUuid(id,name){const value=el(id).value.trim();
  if(!uuid.test(value))throw new Error('Enter a valid '+name+' UUID.');return value;}
 if(route==='/portal/teacher'){
  el('load-classes').addEventListener('click',loadTeacher);
  el('on').value=new Date().toISOString().slice(0,10);
  el('attendance-form').addEventListener('submit',async event=>{
   event.preventDefault();
   try{
    const period=el('period').value.trim();
    if(period&&!uuid.test(period))throw new Error('Invalid period UUID.');
    const response=await api('teacher/attendance','POST',{
     campusId:validateCampus(),enrollmentId:requireUuid('enrollment','enrollment'),
     date:el('on').value,periodId:period||null,status:el('attendance-status').value,remarks:null
    });
    notice('Attendance saved for '+response.date+'.');
   }catch(e){notice(e.message,true);}
  });
  el('marks-form').addEventListener('submit',async event=>{
   event.preventDefault();
   try{
    const absent=el('absent').checked;
    const value=el('marks').value;
    if(!absent && (value===''||!Number.isFinite(Number(value))||Number(value)<0))
      throw new Error('Enter valid marks.');
    await api('teacher/marks','POST',{
     campusId:validateCampus(),assessmentId:requireUuid('assessment','assessment'),
     enrollmentId:requireUuid('marks-enrollment','enrollment'),marks:absent?null:Number(value),isAbsent:absent
    });
    notice('Assessment marks saved.');
   }catch(e){notice(e.message,true);}
  });
 } else if(route==='/portal/parent') {
  try{await loadOwn('parent');}catch(e){notice(e.message,true);}
 } else if(route==='/portal/student') {
  try{await loadOwn('student');}catch(e){notice(e.message,true);}
 }
})();
