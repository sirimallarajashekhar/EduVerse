const {test}=require('node:test');
const assert=require('node:assert/strict');
const {JSDOM,VirtualConsole}=require('jsdom');
const fs=require('node:fs'),path=require('node:path');
const repo=path.resolve(__dirname,'../..');
function files(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(item=>item.isDirectory()?files(path.join(dir,item.name)):[path.join(dir,item.name)]);}
const screens=files(path.join(repo,'src')).filter(file=>file.endsWith('.cshtml')&&fs.readFileSync(file,'utf8').includes('data-activity-page="true"'));
function project(source,key,routeKey=key,fixed=null){
 const dom=new JSDOM(source);
 for(const node of [...dom.window.document.querySelectorAll('[ev-activities]')]){
  if(!node.getAttribute('ev-activities').split(',').includes(key))node.remove();
  else node.removeAttribute('ev-activities');
 }
 for(const node of [...dom.window.document.querySelectorAll('[ev-requested-activities]')]) {
  if(!node.getAttribute('ev-requested-activities').split(',').includes(routeKey))node.remove();
  else node.removeAttribute('ev-requested-activities');
 }
 if(fixed){
  const select=dom.window.document.getElementById(fixed.field);
  const input=dom.window.document.createElement('input');input.type='hidden';input.id=fixed.field;input.value=fixed.value;
  select.replaceWith(input);
 }
 return dom;
}
for(const file of screens){
 test(path.relative(repo,file)+': server response contains only the requested activity',()=>{
  const source=fs.readFileSync(file,'utf8');const original=new JSDOM(source);
  const keys=[...new Set([...original.window.document.querySelectorAll('[data-ev-action-key]')].map(n=>n.dataset.evActionKey))];
  for(const key of keys){
   const dom=project(source,key),doc=dom.window.document;
   const remaining=[...doc.querySelectorAll('[data-ev-action-key]')];
   assert.ok(remaining.length,key+' has no content');
   for(const node of remaining)assert.equal(node.dataset.evActionKey,key);
   const forms=[...doc.querySelectorAll('form')].filter(form=>form.querySelector('button'));
   assert.ok(forms.length<=1,key+' contains more than one form');
   assert.equal(doc.querySelector('#ev-action-select'),null,'in-page switcher must not exist');
   dom.window.close();
  }
  original.window.close();
 });
 test(path.relative(repo,file)+': API adapters initialize with other forms absent',async()=>{
  const source=fs.readFileSync(file,'utf8');const initial=new JSDOM(source);
  const keys=[...new Set([...initial.window.document.querySelectorAll('[data-ev-action-key]')].map(n=>n.dataset.evActionKey))];initial.window.close();
  const scripts=[...source.matchAll(/<script[^>]*src="([^"]+)"/g)].map(match=>match[1]);
  for(const key of keys){
   const projected=project(source,key);const html=projected.serialize();projected.window.close();
   const base=new JSDOM(html).window.document.querySelector('[data-activity-base]').dataset.activityBase;
   const errors=[];const console=new VirtualConsole();console.on('jsdomError',e=>errors.push(e.message));
   const dom=new JSDOM(html,{url:'https://eduverse.test'+base+'/activities/'+key,runScripts:'outside-only',virtualConsole:console});const w=dom.window;
   w.document.body.insertAdjacentHTML('afterbegin','<input name="__RequestVerificationToken" value="test"/>');
   w.confirm=()=>false;w.alert=()=>{};w.prompt=()=>null;
   w.fetch=async url=>{const p=new URL(url,w.location.origin).pathname;let data=[];
    if(p.endsWith('/campuses'))data=[{id:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',code:'MAIN',name:'Main'}];
    if(p.endsWith('/dashboard')||p.endsWith('/overview'))data={subscription:{status:'ACTIVE',features:[]},months:[],kpis:{},features:[],access:{}};
    return {ok:true,status:200,json:async()=>data};
   };
   for(const src of scripts){
    let relative=src.replace(/^~?\//,'');
    const projectName=file.includes('EduVerse.Ams.Web')?'EduVerse.Ams.Web':file.includes('EduVerse.Hrms.Web')?'EduVerse.Hrms.Web':'EduVerse.Web';
    relative=relative.replace(/^(ams|hrms)\//,'');
    const script=fs.readFileSync(path.join(repo,'src',projectName,'wwwroot',relative),'utf8');
    try{w.eval(script);}catch(e){errors.push(e.message);}
   }
   await new Promise(resolve=>setTimeout(resolve,0));
   for(const status of w.document.querySelectorAll('[role=status],[id$=message],[id$=status],#rbac-feedback'))
    if(/Cannot read|is not iterable|is not a function/.test(status.textContent))errors.push(status.textContent);
   assert.deepEqual(errors,[],key+' failed with unrelated controls absent');
   dom.window.close();
  }
 });
}
test('Refund and reconciliation have disjoint HTML even with JavaScript disabled',()=>{
 const source=fs.readFileSync(path.join(repo,'src/EduVerse.Web/Views/FinanceAdmin/Index.cshtml'),'utf8');
 const refund=project(source,'refund-form');
 assert.ok(refund.window.document.getElementById('refund-form'));
 assert.equal(refund.window.document.getElementById('reconcile-form'),null);
 assert.equal(refund.window.document.getElementById('payment-form'),null);
 refund.window.close();
});
test('A standalone refund page submits to the existing API with reconciliation absent',async()=>{
 const source=fs.readFileSync(path.join(repo,'src/EduVerse.Web/Views/FinanceAdmin/Index.cshtml'),'utf8');
 const projected=project(source,'refund-form');const html=projected.serialize();projected.window.close();
 const dom=new JSDOM(html,{url:'https://eduverse.test/admin/finance/activities/refund-form',runScripts:'outside-only'});
 const w=dom.window,doc=w.document,calls=[];
 doc.body.insertAdjacentHTML('afterbegin','<input name="__RequestVerificationToken" value="csrf"/>');
 w.confirm=()=>true;
 w.fetch=async(url,options={})=>{
  calls.push({url:String(url),options});
  const payload=String(url).includes('dashboard')?{}:[];
  return {ok:true,status:options.method==='POST'?204:200,json:async()=>payload};
 };
 w.eval(fs.readFileSync(path.join(repo,'src/EduVerse.Web/wwwroot/js/finance-admin.js'),'utf8'));
 await new Promise(resolve=>setTimeout(resolve,0));
 const add=(id,value)=>{const option=doc.createElement('option');option.value=value;option.textContent=value;doc.getElementById(id).append(option);doc.getElementById(id).value=value;};
 add('campus','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
 add('refund-payment','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
 doc.getElementById('refund-amount').value='125';doc.getElementById('refund-reason').value='Duplicate payment';doc.getElementById('refund-ref').value='RF-001';
 doc.getElementById('refund-form').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await new Promise(resolve=>setTimeout(resolve,0));
 const posted=calls.find(call=>call.options.method==='POST');
 assert.ok(posted);assert.equal(posted.url,'/bff/finance/refunds');
 assert.equal(JSON.parse(posted.options.body).amount,125);
 assert.equal(doc.getElementById('reconcile-form'),null);
 assert.ok(!calls.some(call=>call.options.method==='POST'&&call.url.includes('reconciliation')));
 dom.window.close();
});

test('Promotion submits its own destination fields without an enrollment form',async()=>{
 const source=fs.readFileSync(path.join(repo,'src/EduVerse.Web/Views/StudentAdmin/Index.cshtml'),'utf8');
 const projected=project(source,'lifecycle-form','promote-student',{field:'lifecycle-action',value:'PROMOTE'});
 const html=projected.serialize();projected.window.close();
 const dom=new JSDOM(html,{url:'https://eduverse.test/admin/students/activities/promote-student',runScripts:'outside-only'});
 const w=dom.window,doc=w.document,calls=[];
 doc.body.insertAdjacentHTML('afterbegin','<input name="__RequestVerificationToken" value="csrf"/>');w.confirm=()=>true;
 w.fetch=async(url,options={})=>{calls.push({url:String(url),options});return {ok:true,status:options.method==='POST'?204:200,json:async()=>[]};};
 w.eval(fs.readFileSync(path.join(repo,'src/EduVerse.Web/wwwroot/js/student-admin.js'),'utf8'));
 await new Promise(resolve=>setTimeout(resolve,0));
 const add=(id,value)=>{const option=doc.createElement('option');option.value=value;option.textContent=value;doc.getElementById(id).append(option);doc.getElementById(id).value=value;};
 add('campus','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');add('student','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
 add('promote-year','cccccccc-cccc-cccc-cccc-cccccccccccc');add('promote-item','dddddddd-dddd-dddd-dddd-dddddddddddd');
 doc.getElementById('lifecycle-form').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await new Promise(resolve=>setTimeout(resolve,0));
 const posted=calls.find(call=>call.options.method==='POST');assert.ok(posted);
 const body=JSON.parse(posted.options.body);
 assert.equal(body.action,'PROMOTE');assert.equal(body.academicYearId,'cccccccc-cccc-cccc-cccc-cccccccccccc');
 assert.equal(body.academicItemId,'dddddddd-dddd-dddd-dddd-dddddddddddd');assert.equal(body.targetCampusId,null);
 assert.equal(doc.getElementById('enroll-form'),null);assert.equal(doc.getElementById('target-campus'),null);
 dom.window.close();
});
