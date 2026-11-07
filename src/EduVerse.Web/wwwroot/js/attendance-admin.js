// Thin presentation adapter. All business decisions are made by the tenant-scoped API.
(() => {
 const $=id=>document.getElementById(id),val=id=>$(id).value;
 const campus=()=>val('campus'), token=document.querySelector('[name="__RequestVerificationToken"]').value;
 const status=message=>{$('status').textContent=message;};
 let periods=[],years=[],catalog=[],people=[],students=[],assignments=[],enrollments=[];
 const get=async url=>{
  const response=await fetch(url,{headers:{Accept:'application/json'}});
  if(!response.ok)throw Error('Unable to load data ('+response.status+').');
  return response.json();
 };
 const api=async (route,method='GET',payload=null,query={})=>{
  const url=new URL('/bff/attendance/'+route,window.location.origin);
  if(method==='GET')query.campusId=campus();
  for(const [key,value] of Object.entries(query))if(value!==null&&value!==undefined)url.searchParams.set(key,value);
  const response=await fetch(url.pathname+url.search,{
   method,headers:{Accept:'application/json',...(method==='POST'?{'Content-Type':'application/json',RequestVerificationToken:token}:{})},
   ...(payload?{body:JSON.stringify(payload)}:{})
  });
  if(!response.ok)throw Error('Operation rejected ('+response.status+'). Check active enrollment, holiday, schedule or permissions.');
  return response.status===204?null:response.json();
 };
 const select=(id,items,label,emptyLabel)=>{
  const target=$(id);target.replaceChildren();
  if(emptyLabel){const opt=document.createElement('option');opt.value='';opt.textContent=emptyLabel;target.append(opt);}
  for(const item of items){const opt=document.createElement('option');opt.value=item.id;opt.textContent=label(item);target.append(opt);}
 };
 const list=(id,items,label)=>{
  const target=$(id);target.replaceChildren();
  for(const item of items){const li=document.createElement('li');li.textContent=label(item);target.append(li);}
 };
 const run=async work=>{try{await work();status('Attendance administration ready.');}catch(e){status(e.message);}};
 async function refreshAttendance(){
  if(!campus()||!val('enrollment'))return;
  const enrollmentId=val('enrollment');
  const [marks,summary,staff]=await Promise.all([
   api('students','GET',null,{enrollmentId}),
   api('students/summary','GET',null,{enrollmentId}),
   api('staff','GET',null,{date:val('staff-date')})
  ]);
  list('student-marks',marks,m=>m.date+' · '+m.scope+' '+m.status);
  $('student-summary').textContent='Daily records: '+summary.total+
   ' · Present '+summary.present+' · Absent '+summary.absent+
   ' · Late '+summary.late+' · Excused '+summary.excused+
   ' · Attendance '+summary.attendancePercent+'%';
  list('staff-marks',staff,m=>m.staffUserId+' · '+m.status);
 }
 function updateTeachers(){
  const subject=val('subject'),year=val('year');
  const assignedIds=new Set(assignments.filter(a=>a.subjectId===subject&&a.academicYearId===year).map(a=>a.facultyUserId));
  select('teacher',people.filter(p=>assignedIds.has(p.id)),p=>p.displayName+' ('+p.email+')','Select assigned teacher');
 }
 async function updateSlots(){
  if(!campus()||!val('year'))return;
  const slots=await api('timetable','GET',null,{academicYearId:val('year')});
  const lookup=id=>[...catalog,...periods,...people].find(x=>x.id===id)?.name||id;
  list('timetable-list',slots,s=>'Day '+s.dayOfWeek+' · '+lookup(s.classItemId)+' · '+lookup(s.subjectId)+' · '+lookup(s.periodId));
  updateTeachers();
 }
 async function refresh(){
  if(!campus())return;
  const base=campus();
  [periods,years,catalog,assignments,students,people]=await Promise.all([
   api('periods'),
   get('/bff/academic/years?campusId='+base),
   get('/bff/academic/catalog?campusId='+base),
   get('/bff/academic/faculty-assignments?campusId='+base),
   get('/bff/students?campusId='+base),
   get('/bff/identity/users')
  ]);
  select('year',years,x=>x.name,'Select academic year');
  select('class',catalog.filter(x=>['GRADE','CLASS','SECTION','COURSE','BATCH'].includes(x.kind)),x=>x.kind+' · '+x.name,'Select class');
  select('subject',catalog.filter(x=>x.kind==='SUBJECT'),x=>x.name,'Select subject');
  select('slot-period',periods,x=>x.code+' · '+x.name,'Select period');
  select('staff',people,x=>x.displayName+' ('+x.email+')','Select staff');
  select('mark-period',periods,x=>x.name,'Daily attendance');
  list('period-list',periods,x=>x.code+' · '+x.name+' ('+x.startsAt+'–'+x.endsAt+')');
  enrollments=(await Promise.all(students.map(async student=>({
   student,items:await get('/bff/students/'+student.id+'/enrollments?campusId='+base)
  })))).flatMap(x=>x.items.filter(e=>e.status==='ACTIVE').map(e=>({...e,studentName:x.student.givenName+' '+x.student.familyName})));
  select('enrollment',enrollments,e=>e.studentName+' · '+(catalog.find(i=>i.id===e.academicItemId)?.name||'Enrollment'),'Select student enrollment');
  if(val('year'))await updateSlots();
  if(val('enrollment'))await refreshAttendance();
  const metrics=await api('dashboard');
  $('dashboard').textContent='Periods '+metrics.periods+' · Timetable allocations '+metrics.timetableSlots+
   ' · Student attendance records '+metrics.studentMarks+' · Staff attendance records '+metrics.staffMarks;
 }
 function submit(form,work){
  $(form).addEventListener('submit',event=>{
   event.preventDefault();
   run(async()=>{await work();$(form).reset();await refresh();});
  });
 }
 submit('period-form',()=>api('periods','POST',{campusId:campus(),
  code:val('period-code'),name:val('period-name'),startsAt:val('period-start'),
  endsAt:val('period-end')}));
 submit('timetable-form',()=>api('timetable','POST',{
  campusId:campus(),academicYearId:val('year'),classItemId:val('class'),
  subjectId:val('subject'),periodId:val('slot-period'),
  teacherId:val('teacher'),dayOfWeek:Number(val('weekday'))
 }));
 submit('student-mark-form',()=>api('students','POST',{
  campusId:campus(),enrollmentId:val('enrollment'),date:val('student-date'),
  periodId:val('mark-period')||null,status:val('student-status'),
  remarks:val('student-remarks')||null
 }));
 submit('staff-mark-form',()=>api('staff','POST',{
  campusId:campus(),staffUserId:val('staff'),date:val('staff-date'),
  status:val('staff-status'),remarks:val('staff-remarks')||null
 }));
 $('campus').addEventListener('change',()=>run(refresh));
 $('year').addEventListener('change',()=>run(updateSlots));
 $('subject').addEventListener('change',updateTeachers);
 $('enrollment').addEventListener('change',()=>run(refreshAttendance));
 $('staff-date').addEventListener('change',()=>run(refreshAttendance));
 const localToday=new Date(Date.now()-new Date().getTimezoneOffset()*60000).toISOString().slice(0,10);
 $('student-date').value=localToday;$('staff-date').value=localToday;
 run(async()=>{
  const campuses=await get('/bff/institution-admin/campuses');
  select('campus',campuses,c=>c.name+' ('+c.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else status('Create a campus before recording attendance.');
 });
})();