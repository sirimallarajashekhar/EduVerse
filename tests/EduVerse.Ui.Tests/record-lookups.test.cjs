const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {JSDOM}=require('jsdom');
const behavior=fs.readFileSync(path.join(__dirname,'../../src/EduVerse.DesignSystem/wwwroot/js/record-lookups.js'),'utf8');
const tick=()=>new Promise(resolve=>setTimeout(resolve,10));
const a='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',b='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const year='cccccccc-cccc-cccc-cccc-cccccccccccc';
const context=campus=>[{id:'context-'+campus,label:'Class Nine · 2026–27 · Mathematics',classId:campus,yearId:year,subjectId:'math',className:'Class Nine',yearName:'2026–27',subjectName:'Mathematics'}];
function page(fetch,extra='',route='attendance-form'){
 const dom=new JSDOM(`<div data-activity-page="true"><label for="campus">Campus</label><select id="campus" required data-record-choice="campus"></select>${extra}</div>`,{url:'https://eduverse.test/portal/teacher/activities/'+route,runScripts:'outside-only'});
 dom.window.fetch=fetch;dom.window.eval(behavior);return dom;
}
const response=data=>({ok:true,status:200,json:async()=>data});
function selected(dom,id,value){const field=dom.window.document.getElementById(id);field.value=value;field.dispatchEvent(new dom.window.Event('change',{bubbles:true}));}
const form='<label for="teacher-class">Assigned class</label><select id="teacher-class" data-record-choice="class"></select><form><label for="enrollment">Student</label><select id="enrollment" data-record-choice="enrollment" required></select><button type="submit">Save attendance</button></form>';
test('Named class/student choices clear when the campus changes',async()=>{
 const dom=page(async url=>{const u=new URL(url,'https://eduverse.test');if(u.pathname.endsWith('campuses'))return response([{id:a,label:'North Campus'},{id:b,label:'South Campus'}]);if(u.pathname.endsWith('teacher-contexts'))return response(context(u.searchParams.get('campusId')));if(u.pathname.includes('/classes/'))return response([{enrollmentId:'enrollment',givenName:'Sneha',familyName:'Rao',admissionNumber:'EV-001',academicYearId:year}]);return response([]);},form);
 await tick();selected(dom,'teacher-class',a+'|'+year);await tick();
 const doc=dom.window.document;assert.match(doc.getElementById('enrollment').textContent,/Sneha Rao · EV-001/);
 assert.ok(!doc.getElementById('enrollment').textContent.includes(a));
 selected(dom,'enrollment','enrollment');assert.equal(doc.querySelector('button').disabled,false);
 selected(dom,'campus',b);assert.equal(doc.getElementById('enrollment').value,'');assert.equal(doc.querySelector('button').disabled,true);
 await tick();dom.window.close();
});
test('A late roster response cannot restore records from a previous campus',async()=>{
 let complete;
 const dom=page(async url=>{const u=new URL(url,'https://eduverse.test');if(u.pathname.endsWith('campuses'))return response([{id:a,label:'North'},{id:b,label:'South'}]);if(u.pathname.endsWith('teacher-contexts'))return response(context(u.searchParams.get('campusId')));return {ok:true,status:200,json:()=>new Promise(resolve=>complete=resolve)};},form);
 await tick();selected(dom,'teacher-class',a+'|'+year);await tick();assert.ok(complete);
 selected(dom,'campus',b);complete([{enrollmentId:'old',givenName:'Old',familyName:'Student',admissionNumber:'EV-OLD',academicYearId:year}]);await tick();
 assert.ok(!dom.window.document.getElementById('enrollment').textContent.includes('Old Student'));dom.window.close();
});
test('A lookup permission failure keeps required choices and submit unavailable',async()=>{
 const dom=page(async()=>({ok:false,status:403,json:async()=>[]} ),form);await tick();
 assert.equal(dom.window.document.querySelector('button').disabled,true);
 assert.match(dom.window.document.querySelector('[role=status]').textContent,/do not have access/);dom.window.close();
});
test('Selecting a teaching assignment sets the existing lesson API fields consistently',async()=>{
 const dom=page(async url=>String(url).includes('campuses')?response([{id:a,label:'North Campus'}]):response(context(a)),
 '<form id="lesson-form"><label for="teaching-context">Teaching assignment</label><select id="teaching-context" required data-record-choice="context"></select><input type="hidden" id="lesson-year"/><input type="hidden" id="lesson-class"/><input type="hidden" id="lesson-subject"/><button type="submit">Create lesson</button></form>','lesson-form');
 await tick();const doc=dom.window.document;
 assert.equal(doc.getElementById('lesson-year').value,year);assert.equal(doc.getElementById('lesson-class').value,a);assert.equal(doc.getElementById('lesson-subject').value,'math');
 assert.match(doc.getElementById('teaching-context').textContent,/Class Nine/);dom.window.close();
});
test('A late roster from another class in the same campus is discarded',async()=>{
 const pending={};
 const dom=page(async url=>{const u=new URL(url,'https://eduverse.test');if(u.pathname.endsWith('campuses'))return response([{id:a,label:'North'}]);if(u.pathname.endsWith('teacher-contexts'))return response([...context(a),...context(b)]);const cls=u.pathname.includes('/'+a+'/')?a:b;return {ok:true,status:200,json:()=>new Promise(resolve=>pending[cls]=resolve)};},form);
 await tick();selected(dom,'teacher-class',a+'|'+year);await tick();selected(dom,'teacher-class',b+'|'+year);await tick();
 pending[b]([{enrollmentId:'new',givenName:'Current',familyName:'Student',admissionNumber:'EV-NEW',academicYearId:year}]);await tick();
 pending[a]([{enrollmentId:'old',givenName:'Previous',familyName:'Student',admissionNumber:'EV-OLD',academicYearId:year}]);await tick();
 const labels=dom.window.document.getElementById('enrollment').textContent;assert.match(labels,/Current Student/);assert.ok(!labels.includes('Previous Student'));dom.window.close();
});
test('Late clearance cannot offboard the previously selected employee',async()=>{
 const first='11111111-1111-4111-8111-111111111111',second='22222222-2222-4222-8222-222222222222',campus='33333333-3333-4333-8333-333333333333';
 const dom=new JSDOM(`<input name="__RequestVerificationToken" value="csrf"/><select id="campus"><option value="${campus}">North</option></select><select id="exit-employee"><option value="${first}">First</option><option value="${second}">Second</option></select><p id="workflow-status"></p><p id="clearance-result"></p><button id="exit-button" disabled>Confirm</button>`,{url:'https://eduverse.test/admin/workflows/activities/exit-button',runScripts:'outside-only'});
 const pending={},posts=[];const w=dom.window;w.confirm=()=>true;
 w.fetch=async(url,options)=>{if(options.method==='POST'){posts.push(JSON.parse(options.body));return {ok:true,status:200,json:async()=>({status:'APPLIED'})};}return {ok:true,status:200,json:()=>new Promise(resolve=>pending[String(url).includes(first)?first:second]=resolve)};};
 w.eval(fs.readFileSync(path.join(__dirname,'../../src/EduVerse.Web/wwwroot/js/workflow-admin.js'),'utf8'));
 selected(dom,'exit-employee',first);await tick();selected(dom,'exit-employee',second);await tick();
 pending[second]({outstandingAssets:0});await tick();pending[first]({outstandingAssets:0});await tick();
 w.document.getElementById('exit-button').click();await tick();assert.equal(posts[0].employeeId,second);dom.window.close();
});
