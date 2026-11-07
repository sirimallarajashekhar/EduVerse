// Thin learning UI: all validation, tenancy, grading and delivery live in the API.
(() => {
 const $=id=>document.getElementById(id),v=id=>$(id).value,campus=()=>v('campus');
 const csrf=document.querySelector('input[name="__RequestVerificationToken"]').value;
 let lessons=[],assignments=[],submissions=[],students=[],enrollments=[],threads=[],notifications=[];
 const url=(base,params={})=>{
  const x=new URL(base,location.origin);
  for(const [k,value] of Object.entries(params))if(value!==null&&value!==undefined)x.searchParams.set(k,value);
  return x.pathname+x.search;
 };
 async function api(path,method='GET',body=null,params={}){
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(body)headers['Content-Type']='application/json';
  if(method==='GET')params.campusId=campus();
  const r=await fetch(url('/bff/learning/'+path,params),{method,headers,...(body?{body:JSON.stringify(body)}:{})});
  if(!r.ok)throw Error('Learning API rejected request ('+r.status+'). Check permissions, course scope or workflow state.');
  return r.status===204?null:r.json();
 }
 async function reference(path,params={}){
  const r=await fetch(url(path,params));
  if(!r.ok)throw Error('Unable to load academic/student references ('+r.status+').');
  return r.json();
 }
 function select(id,items,label,placeholder='Select'){
  const el=$(id);el.replaceChildren();
  const first=document.createElement('option');first.value='';first.textContent=placeholder;el.append(first);
  for(const x of items){const option=document.createElement('option');option.value=x.id;option.textContent=label(x);el.append(option);}
 }
 function list(id,items,label){
  const el=$(id);el.replaceChildren();
  for(const x of items){const li=document.createElement('li');li.textContent=label(x);el.append(li);}
 }
 const run=async fn=>{try{await fn();$('status').textContent='Learning dashboard ready.';}catch(e){$('status').textContent=e.message;}};
 const form=(id,fn)=>$(id).addEventListener('submit',e=>{e.preventDefault();run(async()=>{await fn();e.target.reset();await refresh();});});
 const name=id=>{const x=students.find(s=>s.id===id);return x?x.givenName+' '+x.familyName:id.slice(0,8);};
 async function loadGuardians(){
  if(!v('thread-student'))return select('guardian',[],x=>x.fullName,'Choose guardian');
  const rows=await reference('/bff/students/'+v('thread-student')+'/guardians',{campusId:campus()});
  select('guardian',rows,x=>x.fullName,'Choose guardian');
 }
 async function loadMessages(){
  const rows=v('thread')?await api('threads/'+v('thread')+'/messages'):[];
  list('messages',rows,m=>m.author.slice(0,8)+' · '+m.body);
 }
 async function progress(){
  $('progress').textContent='';
  if(!v('enrollment'))return;
  const r=await api('enrollments/'+v('enrollment')+'/progress');
  $('progress').textContent='Progress: '+r.submitted+'/'+r.assignments+' submitted, '+r.graded+
   ' graded; average '+r.averagePercentage+'%';
 }
 async function refresh(){
  if(!campus())return;
  const [years,catalog,newLessons,newAssignments,newSubmissions,newThreads,news,inbox,people,metrics]=
   await Promise.all([
    reference('/bff/academic/years',{campusId:campus()}),
    reference('/bff/academic/catalog',{campusId:campus()}),
    api('lessons'),api('assignments'),api('submissions'),api('threads'),
    api('announcements'),api('notifications'),
    reference('/bff/students',{campusId:campus()}),api('dashboard')
   ]);
  lessons=newLessons;assignments=newAssignments;submissions=newSubmissions;
  threads=newThreads;notifications=inbox;students=people.filter(s=>s.status==='ACTIVE');
  select('year',years,x=>x.name,'Select year');
  select('class',catalog.filter(x=>['GRADE','CLASS','SECTION','COURSE','BATCH'].includes(x.kind)),x=>x.name,'Select class / course');
  select('subject',catalog.filter(x=>x.kind==='SUBJECT'),x=>x.name,'Select subject');
  select('assignment-lesson',lessons.filter(x=>x.status==='PUBLISHED'),x=>x.title,'Published lesson');
  select('submission-assignment',assignments.filter(x=>x.status==='PUBLISHED'),x=>x.title,'Published assignment');
  select('thread-student',students,x=>x.givenName+' '+x.familyName,'Select student');
  select('thread',threads,x=>x.subject,'Select thread');
  enrollments=(await Promise.all(students.map(async s=>({student:s,
   rows:await reference('/bff/students/'+s.id+'/enrollments',{campusId:campus()})
  })))).flatMap(x=>x.rows.filter(r=>r.status==='ACTIVE').map(r=>({...r,studentName:x.student.givenName+' '+x.student.familyName})));
  select('enrollment',enrollments,x=>x.studentName+' · '+x.academicItemId.slice(0,8),'Select enrolled student');
  const lessonList=$('lessons');lessonList.replaceChildren();
  for(const x of lessons){
   const li=document.createElement('li');li.append(document.createTextNode(x.title+' · '+x.status+' '));
   if(x.status==='DRAFT'){
    const btn=document.createElement('button');btn.type='button';btn.textContent='Publish';btn.className='btn btn-sm btn-outline-primary';
    btn.onclick=()=>run(async()=>{await api('lessons/'+x.id+'/publish','POST',null,{campusId:campus()});await refresh();});li.append(btn);
   }lessonList.append(li);
  }
  const assignmentList=$('assignments');assignmentList.replaceChildren();
  for(const x of assignments){
   const li=document.createElement('li');
   li.append(document.createTextNode(x.title+' · due '+x.dueUtc+' · '+x.status+' '));
   if(x.status==='DRAFT'){
    const btn=document.createElement('button');btn.type='button';btn.textContent='Publish';btn.className='btn btn-sm btn-outline-primary';
    btn.onclick=()=>run(async()=>{await api('assignments/'+x.id+'/publish','POST',null,{campusId:campus()});await refresh();});
    li.append(btn);
   }assignmentList.append(li);
  }
  const submissionList=$('submissions');submissionList.replaceChildren();
  for(const x of submissions){
   const li=document.createElement('li');
   const enrollment=enrollments.find(e=>e.id===x.enrollmentId);
   li.append(document.createTextNode((enrollment?.studentName||x.enrollmentId.slice(0,8))+
    ' · '+x.responseText.slice(0,90)+' · '+x.status+' '));
   if(x.status==='SUBMITTED'){
    const button=document.createElement('button');button.type='button';button.textContent='Grade';button.className='btn btn-sm btn-outline-primary';
    button.onclick=()=>run(async()=>{
     const raw=prompt('Score (0 to assignment maximum)');
     if(raw===null)return;
     const score=Number(raw);
     if(!Number.isFinite(score))throw Error('Numeric score required.');
     const feedback=prompt('Feedback')||'Reviewed';
     await api('submissions/'+x.id+'/grade','POST',{campusId:campus(),score,feedback},{campusId:campus()});
     await refresh();
    });li.append(button);
   }else li.append(document.createTextNode('Score '+x.score+' · '+x.feedback));
   submissionList.append(li);
  }
  list('announcements',news,x=>x.title+' · '+x.audience);
  const inboxNode=$('notifications');inboxNode.replaceChildren();
  for(const x of inbox){
   const li=document.createElement('li');
   li.append(document.createTextNode(x.title+' · '+x.status+' '));
   if(x.status==='DELIVERED'){
    const b=document.createElement('button');b.type='button';b.textContent='Mark read';b.className='btn btn-sm btn-outline-primary';
    b.onclick=()=>run(async()=>{await api('notifications/'+x.id+'/read','POST',null,{campusId:campus()});await refresh();});
    li.append(b);
   }inboxNode.append(li);
  }
  $('metrics').textContent='Lessons '+metrics.lessons+' · Published assignments '+metrics.publishedAssignments+
   ' · Submissions '+metrics.submitted+' · Graded '+metrics.graded+' · Mean '+metrics.averagePercentage+
   '% · Announcements '+metrics.announcements+' · Threads '+metrics.threads+
   ' · Undelivered-to-read '+metrics.unreadNotifications;
  await loadGuardians();await loadMessages();await progress();
 }
 form('lesson-form',()=>api('lessons','POST',{campusId:campus(),academicYearId:v('year'),
  classId:v('class'),subjectId:v('subject'),title:v('lesson-title'),
  body:v('lesson-body'),resourceUrl:v('resource')||null}));
 form('assignment-form',()=>api('assignments','POST',{campusId:campus(),lessonId:v('assignment-lesson'),
  title:v('assignment-title'),instructions:v('instructions'),
  dueUtc:new Date(v('due')).toISOString(),maxScore:Number(v('max-score'))}));
 form('submission-form',()=>api('submissions','POST',{campusId:campus(),
  assignmentId:v('submission-assignment'),enrollmentId:v('enrollment'),responseText:v('submission-body')}));
 form('announcement-form',()=>api('announcements','POST',{campusId:campus(),
  title:v('announcement-title'),body:v('announcement-body'),audience:v('audience')}));
 form('thread-form',()=>api('threads','POST',{campusId:campus(),
  studentId:v('thread-student'),guardianId:v('guardian'),subject:v('thread-subject')}));
 form('message-form',()=>api('messages','POST',{campusId:campus(),threadId:v('thread'),body:v('message')}));
 $('campus').addEventListener('change',()=>run(refresh));
 $('thread-student').addEventListener('change',()=>run(loadGuardians));
 $('thread').addEventListener('change',()=>run(loadMessages));
 $('enrollment').addEventListener('change',()=>run(progress));
 run(async()=>{
  const campuses=await reference('/bff/institution-admin/campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else $('status').textContent='Create a campus first.';
 });
})();