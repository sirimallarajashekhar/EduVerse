// Presentation only. Tenant identity, eligibility, transitions and document authorization are API-owned.
(() => {
  const $=id=>document.getElementById(id);
  const val=id=>$(id).value;
  const campus=()=>val('campus');
  const student=()=>val('student');
  const csrf=document.querySelector('[name="__RequestVerificationToken"]').value;
  const status=text=>{$('status').textContent=text;};
  let applications=[],students=[],years=[],items=[],campuses=[];

  async function call(path,method='GET',body=null,form=false) {
    const headers={'Accept':'application/json'};
    if(method!=='GET')headers.RequestVerificationToken=csrf;
    if(body && !form)headers['Content-Type']='application/json';
    const response=await fetch('/bff/students'+path,{
      method,headers, ...(body?{body:form?body:JSON.stringify(body)}:{})
    });
    if(!response.ok)throw Error('Request failed ('+response.status+').');
    return response.status===204?null:response.json();
  }
  async function get(url) {
    const response=await fetch(url);
    if(!response.ok)throw Error('Unable to load campus or academic data.');
    return response.json();
  }
  function select(id,rows,label,emptyLabel) {
    const target=$(id);target.replaceChildren();
    if(emptyLabel) {
      const option=document.createElement('option');option.value='';option.textContent=emptyLabel;target.append(option);
    }
    rows.forEach(r=>{
      const option=document.createElement('option');option.value=r.id;option.textContent=label(r);
      target.append(option);
    });
  }
  function list(id,records,label) {
    const host=$(id);host.replaceChildren();
    records.forEach(record=>{const li=document.createElement('li');li.textContent=label(record);host.append(li);});
  }
  function button(text,clicked) {
    const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';
    b.textContent=text;b.addEventListener('click',clicked);return b;
  }
  async function refreshStudentDetails() {
    const id=student(),campusId=campus();
    ['guardians','enrollments','documents'].forEach(name=>$(name).replaceChildren());
    if(!id)return;
    const selected=students.find(s=>s.id===id);
    $('student-detail').textContent=selected?selected.admissionNumber+' · '+selected.status:'';
    const [guardians,enrollments,documents]=await Promise.all([
      call('/'+id+'/guardians?campusId='+campusId),
      call('/'+id+'/enrollments?campusId='+campusId),
      call('/'+id+'/documents?campusId='+campusId)
    ]);
    list('guardians',guardians,g=>g.fullName+' · '+g.relationship+(g.isPrimary?' (primary)':''));
    list('enrollments',enrollments,e=>e.status+' · '+e.academicYearId+' · '+e.academicItemId);
    const host=$('documents');
    for(const d of documents) {
      const row=document.createElement('tr');
      const name=document.createElement('td');name.textContent=d.fileName;
      const state=document.createElement('td');state.textContent=d.verificationStatus;
      const actions=document.createElement('td');
      actions.append(button('Download',()=>{
        location.assign('/bff/students/'+id+'/documents/'+d.id+'?campusId='+campusId);
      }));
      if(d.verificationStatus==='PENDING'){
        for(const decision of ['VERIFIED','REJECTED'])actions.append(
          button(decision==='VERIFIED'?'Verify':'Reject',()=>run(async()=>{
            await call('/'+id+'/documents/'+d.id+'/review?campusId='+campusId,'POST',{decision});
            await refreshStudentDetails();
          })));
      }
      row.append(name,state,actions);host.append(row);
    }
  }
  async function refresh() {
    if(!campus())return;
    const campusId=campus();
    [applications,students,years,items]=await Promise.all([
      call('/applications?campusId='+campusId),
      call('?campusId='+campusId),
      get('/bff/academic/years?campusId='+campusId),
      get('/bff/academic/catalog?campusId='+campusId)
    ]);
    const body=$('applications');body.replaceChildren();
    applications.forEach(a=>{
      const tr=document.createElement('tr');
      for(const text of [String(a.applicationNumber),a.givenName+' '+a.familyName,a.status]){
        const td=document.createElement('td');td.textContent=text;tr.append(td);
      }
      const td=document.createElement('td');
      if(a.status==='SUBMITTED'){
        for(const decision of ['APPROVED','REJECTED'])td.append(button(
          decision==='APPROVED'?'Approve':'Reject',()=>run(async()=>{
            await call('/applications/'+a.id+'/decision?campusId='+campusId,'POST',{decision});await refresh();
          })));
      }
      tr.append(td);body.append(tr);
    });
    select('approved',applications.filter(a=>a.status==='APPROVED'),
      a=>a.givenName+' '+a.familyName+' (#'+a.applicationNumber+')','Select an approved application');
    select('student',students,s=>s.givenName+' '+s.familyName+' · '+s.admissionNumber,'Select a student');
    select('enroll-year',years,y=>y.name,'Select year');
    select('enroll-item',items.filter(i=>['GRADE','CLASS','SECTION','COURSE','BATCH'].includes(i.kind)),
      i=>i.kind+' · '+i.name,'Select class or course');
    await refreshStudentDetails();
  }
  async function run(work) {
    try{await work();status('Operation completed.');}
    catch(error){status(error.message);}
  }
  $('application-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      await call('/applications','POST',{
        campusId:campus(),givenName:val('given'),familyName:val('family'),
        dateOfBirth:val('dob'),email:val('email')||null,phone:val('phone')||null
      });
      e.target.reset();await refresh();
    });
  });
  $('student-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      const application=applications.find(a=>a.id===val('approved'));
      if(!application)throw Error('Approve an admission first.');
      await call('','POST',{
        campusId:campus(),applicationId:application.id,
        givenName:application.givenName,familyName:application.familyName,dateOfBirth:application.dateOfBirth
      });await refresh();
    });
  });
  $('guardian-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      if(!student())throw Error('Select a student.');
      await call('/'+student()+'/guardians?campusId='+campus(),'POST',{
        fullName:val('guardian-name'),relationship:val('guardian-relation'),
        phone:val('guardian-phone')||null,email:val('guardian-email')||null,
        isPrimary:$('guardian-primary').checked
      });e.target.reset();await refreshStudentDetails();
    });
  });
  $('enroll-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      if(!student())throw Error('Select a student.');
      await call('/'+student()+'/enrollments?campusId='+campus(),'POST',{
        academicYearId:val('enroll-year'),academicItemId:val('enroll-item')
      });await refreshStudentDetails();
    });
  });
  $('lifecycle-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      if(!student())throw Error('Select a student.');
      const action=val('lifecycle-action');
      if(!confirm('Confirm '+action.toLowerCase()+' for the selected student?'))return;
      await call('/'+student()+'/lifecycle?campusId='+campus(),'POST',{
        action,targetCampusId:action==='TRANSFER'?val('target-campus'):null,
        academicYearId:action==='PROMOTE'?val('enroll-year'):null,
        academicItemId:action==='PROMOTE'?val('enroll-item'):null,
        reason:val('lifecycle-reason')||null
      });await refresh();
    });
  });
  $('document-form').addEventListener('submit',e=>{
    e.preventDefault();run(async()=>{
      if(!student())throw Error('Select a student.');
      const upload=$('document-file').files[0];
      if(!upload||upload.size>1048576)throw Error('Choose a file under 1 MiB.');
      const form=new FormData();form.append('file',upload);
      await call('/'+student()+'/documents?campusId='+campus()+'&category='+encodeURIComponent(val('document-category')),
        'POST',form,true);e.target.reset();await refreshStudentDetails();
    });
  });
  $('student').addEventListener('change',()=>run(refreshStudentDetails));
  $('campus').addEventListener('change',()=>run(refresh));
  async function initialize(){
    campuses=await get('/bff/institution-admin/campuses');
    select('campus',campuses,c=>c.name+' ('+c.code+')','Select a campus');
    select('target-campus',campuses,c=>c.name+' ('+c.code+')','Select destination');
    if(campuses.length){$('campus').value=campuses[0].id;await refresh();}
    status(campuses.length?'Admissions dashboard ready.':'Create a campus first.');
  }
  run(initialize);
})();
