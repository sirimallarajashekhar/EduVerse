// Named record selections only. APIs remain responsible for role, tenant and record scope.
(() => {
 const root=document.querySelector('[data-activity-page]');
 if(!root||!root.querySelector('[data-record-choice]'))return;
 const el=id=>root.querySelector(`[id="${id}"]`);
 const fields=[...root.querySelectorAll('select[data-record-choice]')];
 const campus=el('campus')||el('p23-campus');
 const isTeacher=location.pathname.startsWith('/portal/teacher/');
 const relation=location.pathname.startsWith('/portal/parent/')?'PARENT':'SELF';
 let rosterRevision=0,assignmentRevision=0;
 let epoch=0,contexts=[],assessments=[],periods=[],searching=false;
 const status=document.createElement('p');status.className='small text-secondary';status.setAttribute('role','status');root.append(status);
 function syncSubmit(){const blocked=fields.some(field=>field.required&&(!field.value||field.disabled));for(const form of root.querySelectorAll('form'))for(const button of form.querySelectorAll('button[type=submit]'))button.disabled=blocked;}
 function options(select,rows,config=select?._choiceOptions||{}){
  const {id=x=>x.id,label=x=>x.label,placeholder='Choose a record',auto=false}=config;if(select)select._choiceOptions=config;
  if(!select)return;
  const previous=select.value;select.replaceChildren(new Option(rows.length?placeholder:'No matching records',''));
  for(const row of rows)select.append(new Option(label(row),id(row)));
  select.disabled=!rows.length&&select.required;
  select.value=rows.some(row=>String(id(row))===previous)?previous:(auto&&rows.length?String(id(rows[0])):'');syncSubmit();
 }
 function clear(select,text='Choose a campus first'){
  if(!select)return;select.replaceChildren(new Option(text,''));select.disabled=true;syncSubmit();
 }
 async function read(url){const response=await fetch(url,{headers:{Accept:'application/json'}});if(!response.ok)throw Error(response.status===403?'You do not have access to these records.':'Unable to load records. Please try again.');const rows=await response.json();if(!Array.isArray(rows))throw Error('The selection service returned an invalid response.');return rows;}
 const choice=(resource,q='',base='/bff/activity-lookups/')=>read(base+resource+'?'+new URLSearchParams({...(campus?.value?{campusId:campus.value}:{}),q}));
 function notify(select){select?.dispatchEvent(new Event('change',{bubbles:true}));}
 const classKey=row=>row.classId+'|'+row.yearId;
 function updatePeriods(){const [cls,year]=(el('teacher-class')?.value||'').split('|');options(el('period'),periods.filter(row=>row.classId===cls&&row.yearId===year),{placeholder:'Whole day (optional)'});}
 const unique=(rows,id)=>[...new Map(rows.map(row=>[id(row),row])).values()];
 async function roster(classId,version,yearId){
  const revision=++rosterRevision;
  for(const field of fields.filter(x=>x.dataset.recordChoice==='enrollment'))clear(field,'Choose a class or assessment first');
  if(!classId||!campus?.value)return;
  let rows=await read('/bff/portals/teacher/classes/'+encodeURIComponent(classId)+'/students?campusId='+encodeURIComponent(campus.value));if(version!==epoch||revision!==rosterRevision)return;
  if(yearId)rows=rows.filter(row=>row.academicYearId===yearId);
  for(const field of fields.filter(x=>x.dataset.recordChoice==='enrollment'))options(field,rows,{id:x=>x.enrollmentId,label:x=>x.givenName+' '+x.familyName+' · '+x.admissionNumber,placeholder:'Choose a student'});
  if(el('roster')){el('roster').replaceChildren();for(const student of rows){const p=document.createElement('p');p.textContent=student.givenName+' '+student.familyName+' · '+student.admissionNumber;el('roster').append(p);}}
 }
 function applyContext(){const row=contexts.find(x=>x.id===el('teaching-context')?.value);for(const [id,property]of [['lesson-year','yearId'],['lesson-class','classId'],['lesson-subject','subjectId']])if(el(id))el(id).value=row?.[property]||'';}
 async function assignments(version){
  const revision=++assignmentRevision;
  const field=el('p23-assignment'),student=el('p23-student');if(!field)return;clear(field,'Choose your student profile first');
  if(!student?.value||!campus?.value)return;
  const rows=await read('/bff/portals/student/'+encodeURIComponent(student.value)+'/assignments?campusId='+encodeURIComponent(campus.value));if(version!==epoch||revision!==assignmentRevision)return;
  options(field,rows,{label:x=>x.title+' · Due '+new Date(x.dueUtc).toLocaleDateString(),placeholder:'Choose an assignment'});
 }
 async function hydrate(){
  const version=++epoch;++rosterRevision;++assignmentRevision;
  for(const field of fields)if(field!==campus)clear(field,campus?.value?'Loading records…':'Choose a campus first');
  applyContext();if(!campus?.value)return;
  status.textContent='Loading available records…';
  try{
   const tasks=[];
   if(isTeacher&&(el('teacher-class')||el('teaching-context')||el('classes')||el('load-classes')))
    tasks.push(choice('teacher-contexts').then(rows=>{if(version!==epoch)return;rows=unique(rows,x=>classKey(x)+'|'+x.subjectId);contexts=rows;
     if(el('teaching-context')){el('teaching-context')._choiceResource='teacher-contexts';el('teaching-context')._choiceBase='/bff/activity-lookups/';}
     options(el('teaching-context'),rows,{placeholder:'Choose a teaching assignment',auto:true});applyContext();
     options(el('teacher-class'),unique(rows,classKey),{id:classKey,label:x=>x.className+' · '+x.yearName,placeholder:'Choose an assigned class'});
     let classHost=el('classes')||el('assigned-context-summary');if(!classHost&&el('load-classes')){classHost=document.createElement('div');classHost.id='assigned-context-summary';el('load-classes').after(classHost);}if(classHost){classHost.replaceChildren();for(const row of unique(rows,classKey)){const p=document.createElement('p');p.textContent=row.className+' · '+row.yearName;classHost.append(p);}}
    }));
   if(isTeacher&&el('lessons'))tasks.push(read('/bff/portals/teacher/lessons?campusId='+encodeURIComponent(campus.value)).then(rows=>{if(version!==epoch)return;el('lessons').replaceChildren();for(const row of rows){const p=document.createElement('p');p.textContent=row.title+' · '+row.status;el('lessons').append(p);}}));
   const direct={assessment:'teacher-assessments',period:'teacher-periods',lesson:'teacher-lessons',submission:'teacher-submissions',employee:'employees',user:'users',purchase:'purchases'};
   for(const field of fields){const resource=direct[field.dataset.recordChoice];if(!resource)continue;
    const base=field.dataset.choiceBase||'/bff/activity-lookups/';
    tasks.push(choice(resource,'',base).then(rows=>{if(version!==epoch)return;options(field,rows,{placeholder:'Choose '+(field.previousElementSibling?.textContent||'a record')});field._choiceResource=resource;field._choiceBase=base;if(resource==='teacher-assessments')assessments=rows;if(resource==='teacher-periods'){periods=rows;updatePeriods();}}));
   }
   if(fields.some(field=>field.dataset.recordChoice==='linked-student'))tasks.push(choice('linked-students').then(rows=>{if(version!==epoch)return;rows=rows.filter(x=>x.relation===relation);for(const field of fields.filter(x=>x.dataset.recordChoice==='linked-student')){options(field,rows,{placeholder:'Choose a student',auto:true});field._choiceResource='linked-students';field._choiceBase='/bff/activity-lookups/';}return assignments(version);}));
   await Promise.all(tasks);if(version===epoch)status.textContent='Choose the record for this activity.';
  }catch(error){if(version===epoch)status.textContent=error.message;}
 }
 campus?.addEventListener('change',hydrate);
 el('teacher-class')?.addEventListener('change',()=>(updatePeriods(),roster(el('teacher-class').value.split('|')[0],epoch,el('teacher-class').value.split('|')[1])).catch(error=>status.textContent=error.message));
 el('assessment')?.addEventListener('change',()=>roster(assessments.find(x=>x.id===el('assessment').value)?.classId,epoch,assessments.find(x=>x.id===el('assessment').value)?.yearId).catch(error=>status.textContent=error.message));
 el('teaching-context')?.addEventListener('change',applyContext);
 el('p23-student')?.addEventListener('change',()=>assignments(epoch).catch(error=>status.textContent=error.message));
 document.addEventListener('ev:reload-choices',hydrate);
 // Search operates on labels, never on user-entered identifiers. Server search handles bounded lists.
 for(const select of fields){
  select.addEventListener('change',syncSubmit);
  const label=root.querySelector(`label[for="${select.id}"]`)?.textContent||'records';
  const search=document.createElement('input');search.type='search';search.className='form-control form-control-sm mb-2';search.placeholder='Search '+label.toLowerCase();search.setAttribute('aria-label','Search '+label);search.setAttribute('aria-controls',select.id);select.before(search);
  let timer,revision=0;
  search.addEventListener('input',()=>{clearTimeout(timer);const request=++revision,currentCampus=campus?.value,currentEpoch=epoch;
   if(select._choiceResource&&(select===campus||campus?.value)){timer=setTimeout(async()=>{try{let rows=await choice(select._choiceResource,search.value.trim(),select._choiceBase);if(request!==revision||currentCampus!==campus?.value||currentEpoch!==epoch)return;if(select.dataset.recordChoice==='linked-student')rows=rows.filter(x=>x.relation===relation);if(select.dataset.recordChoice==='context')contexts=rows;if(select.dataset.recordChoice==='assessment')assessments=rows;if(select.dataset.recordChoice==='period'){periods=rows;updatePeriods();}else options(select,rows);notify(select);}catch(error){status.textContent=error.message;}},250);}
   else for(const option of select.options)option.hidden=!!option.value&&!option.textContent.toLocaleLowerCase().includes(search.value.trim().toLocaleLowerCase());
  });
 }
 async function start(){
  try{
   if(campus?.dataset.recordChoice==='campus'){campus._choiceResource='campuses';campus._choiceBase='/bff/activity-lookups/';options(campus,await choice('campuses'),{placeholder:'Choose a campus',auto:true});notify(campus);}
   else {
    await hydrate();
    // Existing module adapters populate campus choices asynchronously.
    if(campus)new MutationObserver(()=>{if(!searching){searching=true;queueMicrotask(()=>{searching=false;hydrate();});}}).observe(campus,{childList:true});
   }
  }catch(error){status.textContent=error.message;}
 }
 syncSubmit();
 start();
})();
