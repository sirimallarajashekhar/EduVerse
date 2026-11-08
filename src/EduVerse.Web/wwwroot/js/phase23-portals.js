// Presentation only. The API verifies every role, student/parent link and class assignment.
(() => {
 const el=id=>document.getElementById(id);
 const value=id=>(el(id)?.value||'').trim();
 const uid=/^[a-f\d]{8}-([a-f\d]{4}-){3}[a-f\d]{12}$/i;
 const valid=(id,label)=>{const v=value(id);if(!uid.test(v))throw new Error('Enter a valid '+label+' ID.');return v;};
 const status=(message,error=false)=>{if(!el('p23-status'))return;
  el('p23-status').textContent=message;el('p23-status').className=error?'text-danger':'text-success';};
 const token=document.querySelector('[name="__RequestVerificationToken"]')?.value||'';
 const root='/bff/role-learning/';
 async function request(path,body){
  const method=body===undefined?'GET':'POST';
  const res=await fetch(root+path,{method,headers:method==='GET'?{Accept:'application/json'}:
    {'Content-Type':'application/json','RequestVerificationToken':token},body:method==='GET'?undefined:JSON.stringify(body)});
  if(!res.ok)throw new Error('Request denied ('+res.status+'). Check campus permissions or linked records.');
  if(res.status===204)return null;return res.json();
 }
 function node(tag,text,cls){const e=document.createElement(tag);e.textContent=text;if(cls)e.className=cls;return e;}
 function list(host,items,format){
  host.replaceChildren();if(!items.length){host.append(node('p','No records available.','text-secondary'));return;}
  for(const item of items)host.append(node('div',format(item),'border-bottom py-2'));
 }
 async function act(callback){try{await callback();}catch(e){status(e.message,true);}}
 const campus=()=>valid(el('campus')?'campus':'p23-campus','campus');
 const attach=(id,cb)=>el(id)?.addEventListener('click',()=>act(cb));
 const submit=(id,cb)=>el(id)?.addEventListener('submit',e=>{e.preventDefault();act(cb);});
 submit('phase23-assignment-form',async()=>{
  const data=await request('teacher/assignments',{
   campusId:campus(),lessonId:valid('p23-lesson','lesson'),
   title:value('p23-title'),instructions:value('p23-instructions'),
   dueUtc:new Date(value('p23-due')).toISOString(),maxScore:Number(value('p23-max'))
  });status('Draft assignment created: '+data.title+'. An admin must publish it.');
 });
 attach('p23-load-submissions',async()=>{
  const rows=await request('teacher/submissions?campusId='+campus());
  list(el('p23-submissions'),rows,x=>x.id+' · '+x.status+(x.score===null?'':' · '+x.score));
  status('Loaded assigned submissions.');
 });
 submit('phase23-grade-form',async()=>{
  await request('teacher/grade',{
   campusId:campus(),submissionId:valid('p23-submission','submission'),
   score:Number(value('p23-score')),feedback:value('p23-feedback')
  });status('Grade recorded.');el('p23-load-submissions')?.click();
 });
 submit('phase23-student-submit',async()=>{
  const result=await request('student/submit',{
   campusId:valid('p23-campus','campus'),studentId:valid('p23-student','student'),
   assignmentId:valid('p23-assignment','assignment'),responseText:value('p23-response')
  });status('Assignment submitted: '+result.id);el('p23-load-work')?.click();
 });
 attach('p23-load-work',async()=>{
  const rows=await request('student/'+valid('p23-student','student')+
   '/submissions?campusId='+valid('p23-campus','campus'));
  list(el('p23-work'),rows,x=>x.assignmentId+' · '+x.status+
    (x.score===null?'':' · Score '+x.score)+(x.feedback?' · '+x.feedback:''));
  status('My submissions loaded.');
 });
 submit('phase23-parent-message',async()=>{
  await request('parent/conversations',{
   campusId:valid('p23-campus','campus'),studentId:valid('p23-child','student'),
   subject:value('p23-subject'),body:value('p23-message')
  });status('Conversation created.');el('p23-load-conversations')?.click();
 });
 attach('p23-load-conversations',async()=>{
  const rows=await request('conversations?campusId='+campus());const host=el('p23-conversations');host.replaceChildren();
  if(!rows.length){host.append(node('p','No conversations.','text-secondary'));return;}
  for(const x of rows){
   const section=node('section','', 'my-3 border-bottom py-2');
   section.append(node('h3',x.subject,'h6'));
   const messageBox=node('div','');
   const load=node('button','Read messages','btn btn-sm btn-outline-secondary me-2');
   load.type='button';load.addEventListener('click',()=>act(async()=>{
    const messages=await request('conversations/'+x.id+'/messages?campusId='+campus());
    list(messageBox,messages,m=>m.author+': '+m.body);
   }));
   const textarea=node('textarea','','form-control my-2');textarea.placeholder='Reply to conversation';
   textarea.maxLength=5000;
   const reply=node('button','Reply','btn btn-sm btn-primary');reply.type='button';
   reply.addEventListener('click',()=>act(async()=>{
    await request('conversations/reply',{campusId:campus(),threadId:x.id,body:textarea.value});
    textarea.value='';load.click();status('Reply sent.');
   }));
   section.append(load,messageBox,textarea,reply);host.append(section);
  }
  status('Conversations loaded.');
 });
 attach('p23-load-reviews',async()=>{
  const rows=await request('admin/reviews?campusId='+valid('p23-campus','campus'));
  const host=el('p23-reviews');host.replaceChildren();
  if(!rows.length){host.append(node('p','No pending drafts.','text-secondary'));return;}
  for(const x of rows){
   const section=node('section','', 'd-flex align-items-center justify-content-between border-bottom py-2');
   section.append(node('span',x.type+': '+x.title,'me-3'));
   const approve=node('button','Publish','btn btn-primary btn-sm');approve.type='button';
   approve.addEventListener('click',()=>act(async()=>{
    await request('admin/reviews/'+x.type+'/publish',{
     campusId:valid('p23-campus','campus'),contentId:x.id
    });status('Published '+x.title);el('p23-load-reviews').click();
   }));
   section.append(approve);host.append(section);
  }
 });
})();
