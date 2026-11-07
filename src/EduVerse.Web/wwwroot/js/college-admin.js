// Thin college UI; all academic policies, grade calculation and authorization live in the REST API.
(() => {
 const $=id=>document.getElementById(id),v=id=>$(id).value,campus=()=>v('campus');
 const csrf=document.querySelector('input[name="__RequestVerificationToken"]').value;
 let rules=[],programs=[],semesters=[],courses=[],registrations=[],enrollments=[],exams=[];
 const route=(path,params={})=>{const u=new URL(path,location.origin);
  for(const [key,value] of Object.entries(params))if(value!==null&&value!==undefined)u.searchParams.set(key,value);
  return u.pathname+u.search;};
 async function call(path,method='GET',body=null,params={}){
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(body)headers['Content-Type']='application/json';
  if(method==='GET')params.campusId=campus();
  const response=await fetch(route('/bff/college/'+path,params),{method,headers,
   ...(body?{body:JSON.stringify(body)}:{})});
  if(!response.ok){
   const error=await response.json().catch(()=>null);
   throw Error(error?.message||'College request rejected ('+response.status+'). Check prerequisites, enrollment, grades or permissions.');
  }
  return response.status===204?null:response.json();
 }
 async function ref(path,params={}) {
  const response=await fetch(route(path,params));
  if(!response.ok)throw Error('Failed to fetch academic reference ('+response.status+').');
  return response.json();
 }
 function select(id,rows,label,placeholder='Select'){
  const el=$(id);el.replaceChildren();
  const blank=document.createElement('option');blank.value='';blank.textContent=placeholder;el.append(blank);
  for(const x of rows){const option=document.createElement('option');option.value=x.id;option.textContent=label(x);el.append(option);}
 }
 function list(id,rows,label){const el=$(id);el.replaceChildren();
  for(const x of rows){const item=document.createElement('li');item.textContent=label(x);el.append(item);}
 }
 async function run(fn){try{await fn();$('status').textContent='College administration ready.';}
  catch(e){$('status').textContent=e.message;}}
 function form(id,fn){$(id).addEventListener('submit',event=>{event.preventDefault();run(async()=>{
  await fn();event.target.reset();await refresh();
 });});}
 async function refreshRegistrations(){
  registrations=[];
  $('registrations').replaceChildren();$('term-results').replaceChildren();$('transcript').textContent='';
  if(!v('enrollment'))return;
  registrations=await call('registrations','GET',null,{enrollmentId:v('enrollment')});
  const host=$('registrations');
  for(const x of registrations){
   const li=document.createElement('li');
   li.append(document.createTextNode(
    (courses.find(c=>c.id===x.courseId)?.title||x.courseId.slice(0,8))+
    ' · '+x.status+(x.gradePoints!==null?' · '+x.gradeLabel+' / '+x.gradePoints+' points':'')+' '));
   if(x.status==='ENROLLED'){
    const pick=document.createElement('select');pick.className='form-select form-select-sm d-inline-block ms-1';
    pick.style.maxWidth='165px';
    const empty=document.createElement('option');empty.textContent='Published exam';empty.value='';pick.append(empty);
    for(const exam of exams.filter(e=>e.status==='PUBLISHED'&&['SEMESTER','FINAL'].includes(e.type))){
     const opt=document.createElement('option');opt.value=exam.id;opt.textContent=exam.title;pick.append(opt);
    }
    const button=document.createElement('button');button.type='button';
    button.className='btn btn-sm btn-outline-primary ms-2';button.textContent='Import grade';
    button.onclick=()=>run(async()=>{
     if(!pick.value)throw Error('Select a published exam.');
     await call('grades/import','POST',{campusId:campus(),registrationId:x.id,examId:pick.value});
     await refreshRegistrations();
    });
    li.append(pick,button);
   }
   host.append(li);
  }
  const transcript=await call('transcripts','GET',null,{enrollmentId:v('enrollment')});
  $('transcript').textContent='CGPA '+transcript.cgpa+' · Attempted credits '+transcript.attemptedCredits+
   ' · Earned '+transcript.earnedCredits+' · Backlogs '+transcript.backlogs;
  list('term-results',transcript.semesters,x=>'Semester '+(semesters.find(s=>s.id===x.semesterId)?.number||'?')+
   ' · SGPA '+x.sgpa+' · Earned '+x.earnedCredits+'/'+x.attemptedCredits+
   ' · Backlogs '+x.backlogs+' · Progression '+(x.progressionEligible?'eligible':'blocked'));
 }
 async function refresh(){
  if(!campus())return;
  const [r,p,s,c,years,catalog,students,examination,report]=await Promise.all([
   call('regulations'),call('programs'),call('semesters'),call('courses'),
   ref('/bff/academic/years',{campusId:campus()}),
   ref('/bff/academic/catalog',{campusId:campus()}),
   ref('/bff/students',{campusId:campus()}),
   ref('/bff/exams',{campusId:campus()}),call('report')
  ]);
  rules=r;programs=p;semesters=s;courses=c;exams=examination;
  select('program-rule',rules,x=>x.name);
  select('catalog-program',catalog.filter(x=>x.kind==='PROGRAM'),x=>x.name);
  for(const id of ['sem-program','course-program'])
   select(id,programs,x=>x.name);
  select('sem-year',years,x=>x.name);
  select('sem-catalog',catalog.filter(x=>x.kind==='SEMESTER'),x=>x.name);
  for(const id of ['course-semester','finalize-semester'])
   select(id,semesters,x=>'Semester '+x.number+' · '+(programs.find(p=>p.id===x.programId)?.code||''));
  select('course-subject',catalog.filter(x=>x.kind==='SUBJECT'),x=>x.name);
  select('course-pre',courses,x=>x.code+' · '+x.title,'No prerequisite');
  select('register-course',courses,x=>x.code+' · '+x.title,'Select course');
  list('regulations',rules,x=>x.code+' · pass '+x.passingPoints+' · min '+x.minCreditsForProgression+' credits');
  list('programs',programs,x=>x.code+' · '+x.name);
  list('semesters',semesters,x=>x.number+' · '+x.status);
  list('courses',courses,x=>x.code+' · '+x.type+' · '+x.credits+' credits · '+x.capacity+' seats');
  enrollments=(await Promise.all(students.filter(s=>s.status==='ACTIVE').map(async student=>{
   const records=await ref('/bff/students/'+student.id+'/enrollments',{campusId:campus()});
   return records.filter(x=>x.status==='ACTIVE'&&
     catalog.some(item=>item.id===x.academicItemId&&item.kind==='COURSE'&&
       programs.some(p=>p.catalogProgramId===item.parentId)))
    .map(x=>({...x,name:student.givenName+' '+student.familyName}));
  }))).flat();
  const before=v('enrollment');
  select('enrollment',enrollments,x=>x.name+' · '+x.academicYearId.slice(0,8),'Select college enrollment');
  if(enrollments.some(x=>x.id===before))$('enrollment').value=before;
  $('metrics').textContent='Programs '+report.programs+' · Semesters '+report.semesters+
   ' · Courses '+report.courses+' · Electives '+report.electives+' · Enrollments '+report.registered+
   ' · Backlogs '+report.backlogs+' · Finalized '+report.finalizedSemesters+' · Mean SGPA '+report.meanSgpa;
  await refreshRegistrations();
 }
 form('regulation-form',()=>call('regulations','POST',{campusId:campus(),code:v('rule-code'),
  name:v('rule-name'),passingPoints:Number(v('passing')),minCreditsForProgression:Number(v('minimum')),
  allowBacklogs:$('allow-backlogs').checked,maxSemesters:Number(v('max-semesters'))}));
 form('program-form',()=>call('programs','POST',{campusId:campus(),catalogProgramId:v('catalog-program'),
  regulationId:v('program-rule'),code:v('program-code'),name:v('program-name')}));
 form('semester-form',()=>call('semesters','POST',{campusId:campus(),programId:v('sem-program'),
  academicYearId:v('sem-year'),catalogSemesterId:v('sem-catalog'),number:Number(v('sem-number'))}));
 form('course-form',()=>call('courses','POST',{campusId:campus(),programId:v('course-program'),
  semesterId:v('course-semester'),subjectId:v('course-subject'),code:v('course-code'),
  title:v('course-title'),type:v('course-type'),credits:Number(v('course-credits')),
  capacity:Number(v('course-capacity')),prerequisiteCourseId:v('course-pre')||null}));
 form('registration-form',()=>call('registrations','POST',{campusId:campus(),
  enrollmentId:v('enrollment'),courseId:v('register-course')}));
 form('finalize-form',async()=>{
  if(!confirm('Finalize this semester? Grades cannot be edited afterward.'))return;
  await call('semesters/finalize','POST',{campusId:campus(),
   enrollmentId:v('enrollment'),semesterId:v('finalize-semester')});
 });
 $('enrollment').addEventListener('change',()=>run(refreshRegistrations));
 $('campus').addEventListener('change',()=>run(refresh));
 run(async()=>{
  const campuses=await ref('/bff/institution-admin/campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
 });
})();