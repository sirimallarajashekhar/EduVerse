// Browser presentation only. The Exam REST API enforces all access and grading rules.
(() => {
 const $=id=>document.getElementById(id),v=id=>$(id).value, campus=()=>v('campus');
 const csrf=document.querySelector('[name="__RequestVerificationToken"]').value;
 const status=t=>{$('status').textContent=t;};
 let exams=[],years=[],schemes=[],catalog=[],assessments=[],enrollments=[],students=[];
 const url=(base,query={})=>{
  const target=new URL(base,location.origin);
  for(const [k,v] of Object.entries(query))if(v!==null&&v!==undefined)target.searchParams.set(k,v);
  return target.pathname+target.search;
 };
 async function request(path,method='GET',payload=null,query={}) {
  const headers={Accept:'application/json'};
  if(method!=='GET')headers.RequestVerificationToken=csrf;
  if(payload)headers['Content-Type']='application/json';
  if(method==='GET')query.campusId=campus();
  const response=await fetch(url('/bff/exams/'+path,query),{
   method,headers,...(payload?{body:JSON.stringify(payload)}:{})
  });
  if(!response.ok)throw Error('Examination request failed ('+response.status+'). Check exam state, marks or tenant access.');
  return response.status===204?null:response.json();
 }
 async function data(path){
  const r=await fetch(path);
  if(!r.ok)throw Error('Unable to load academic reference data ('+r.status+').');
  return r.json();
 }
 function select(id,items,label,blank='Select'){
  const host=$(id);host.replaceChildren();
  const empty=document.createElement('option');empty.value='';empty.textContent=blank;host.append(empty);
  for(const item of items){
   const option=document.createElement('option');
   option.value=item.id;option.textContent=label(item);host.append(option);
  }
 }
 function list(id,items,label){
  const host=$(id);host.replaceChildren();
  for(const item of items){const li=document.createElement('li');li.textContent=label(item);host.append(li);}
 }
 const run=async action=>{try{await action();status('Examination administration ready.');}catch(e){status(e.message);}};
 async function loadAssessments(){
  assessments=v('exam')?await request('assessments','GET',null,{examId:v('exam')}):[];
  select('assessment',assessments,a=>catalog.find(x=>x.id===a.subjectId)?.name+
    ' · '+a.maxMarks+' marks','Select assessment');
  list('assessments',assessments,a=>(catalog.find(x=>x.id===a.subjectId)?.name||a.subjectId)+
   ' · '+a.scheduledOn+' · '+a.maxMarks+' max');
  const current=exams.find(e=>e.id===v('exam'));
  $('exam-state').textContent=current?'Status: '+current.status:'Select an exam';
  if(v('assessment'))await loadMarks();else $('mark-list').replaceChildren();
  if(current?.status==='PUBLISHED'){
   const analytics=await request(current.id+'/analytics');
   $('analytics').textContent='Students '+analytics.students+
    ' · Average '+analytics.averagePercentage+'% · Pass '+analytics.passPercentage+
    '% · Highest '+analytics.highestPercentage+'% · Lowest '+analytics.lowestPercentage+'%';
  }else $('analytics').textContent='Result analytics become available after publication.';
 }
 async function loadMarks(){
  const items=v('assessment')?await request('marks','GET',null,{assessmentId:v('assessment')}):[];
  list('mark-list',items,m=>m.enrollmentId.slice(0,8)+' · '+(m.isAbsent?'ABSENT':m.marks));
 }
 async function refresh(){
  if(!campus())return;
  [years,schemes,exams,catalog,students]=await Promise.all([
   data(url('/bff/academic/years',{campusId:campus()})),request('schemes'),
   request(''),data(url('/bff/academic/catalog',{campusId:campus()})),
   data(url('/bff/students',{campusId:campus()}))
  ]);
  select('year',years,x=>x.name,'Academic year');
  select('scheme',schemes,x=>x.name,'Grading scheme');
  select('exam',exams,x=>x.code+' · '+x.title+' · '+x.status,'Select exam');
  select('item',catalog.filter(x=>['GRADE','CLASS','SECTION','COURSE','BATCH'].includes(x.kind)),
   x=>x.kind+' · '+x.name,'Select class or course');
  select('subject',catalog.filter(x=>x.kind==='SUBJECT'),x=>x.name,'Select subject');
  enrollments=(await Promise.all(students.map(async student=>({
   student,records:await data(url('/bff/students/'+student.id+'/enrollments',{campusId:campus()}))
  })))).flatMap(x=>x.records.filter(e=>e.status==='ACTIVE')
   .map(e=>({...e,studentName:x.student.givenName+' '+x.student.familyName})));
  select('enrollment',enrollments,x=>x.studentName+' · '+x.academicYearId.slice(0,8),'Select student');
  select('result-enrollment',enrollments,x=>x.studentName,'Select student');
  await loadAssessments();
 }
 const bind=(form,work)=>{
  $(form).addEventListener('submit',event=>{event.preventDefault();run(async()=>{
   await work();event.target.reset();await refresh();
  });});
 };
 bind('scheme-form',()=>{
  const bands=v('scheme-bands').trim().split(/\r?\n/).map(line=>{
   const parts=line.split(',').map(x=>x.trim());
   if(parts.length!==4||parts.some(x=>!x))throw Error('Every grade row requires grade,min,max,points.');
   return {grade:parts[0],minPercent:Number(parts[1]),maxPercent:Number(parts[2]),points:Number(parts[3])};
  });
  return request('schemes','POST',{campusId:campus(),code:v('scheme-code'),
   name:v('scheme-name'),bands});
 });
 bind('exam-form',()=>request('','POST',{
  campusId:campus(),academicYearId:v('year'),schemeId:v('scheme'),
  code:v('exam-code'),title:v('exam-title'),type:v('exam-type')
 }));
 bind('assessment-form',()=>request('assessments','POST',{
  campusId:campus(),examId:v('exam'),academicItemId:v('item'),subjectId:v('subject'),
  scheduledOn:v('exam-date'),startsAt:v('exam-start')||null,durationMinutes:null,
  maxMarks:Number(v('max')),passMarks:Number(v('pass')),credits:Number(v('credits'))
 }));
 bind('mark-form',()=>request('marks','POST',{
  campusId:campus(),assessmentId:v('assessment'),enrollmentId:v('enrollment'),
  marks:$('absent').checked?null:Number(v('marks')),isAbsent:$('absent').checked
 }));
 for(const action of ['approve','publish'])$(action).addEventListener('click',()=>run(async()=>{
  if(!v('exam'))throw Error('Select an exam.');
  if(!confirm('Confirm '+action+'? This locks the examination state.'))return;
  await request(v('exam')+'/'+action+'?campusId='+campus(),'POST');
  await refresh();
 }));
 $('exam').addEventListener('change',()=>run(loadAssessments));
 $('assessment').addEventListener('change',()=>run(loadMarks));
 $('campus').addEventListener('change',()=>run(refresh));
 $('result-view').addEventListener('click',()=>run(async()=>{
  if(!v('exam')||!v('result-enrollment'))throw Error('Select exam and student.');
  const result=await request(v('exam')+'/results/'+v('result-enrollment'));
  $('result-output').textContent=[
   result.student+' ('+result.admissionNumber+')',result.exam,
   result.total+'/'+result.maximum+' · '+result.percentage+'% · GPA '+result.gpa,
   result.passed?'PASS':'FAIL',
   ...result.subjects.map(s=>s.subject+': '+(s.absent?'ABSENT':s.marks)+' / '+s.maxMarks+' · '+s.grade)
  ].join('\n');
 }));
 $('result-download').addEventListener('click',()=>{
  if(!v('exam')||!v('result-enrollment')){status('Select exam and student.');return;}
  location.assign(url('/bff/exams/'+v('exam')+'/results/'+v('result-enrollment')+'/download',
   {campusId:campus()}));
 });
 run(async()=>{
  const campuses=await data('/bff/institution-admin/campuses');
  select('campus',campuses,x=>x.name+' ('+x.code+')','Select campus');
  if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
  else status('Create a campus first.');
 });
})();