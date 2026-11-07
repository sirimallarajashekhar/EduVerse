// Phase 7: strictly presentation-only. API validates every relationship and tenant/role/plan policy.
(() => {
  const el = id => document.getElementById(id);
  const val = id => el(id).value;
  const nullable = id => val(id) || null;
  const info = text => { el('academic-message').textContent = text; };
  const csrf = document.querySelector('[name="__RequestVerificationToken"]').value;
  const campus = () => val('academic-campus');
  let cachedYears = [], cachedItems = [];
  const api = async (resource, method='GET', payload=null) => {
    let path = '/bff/academic/' + resource;
    if(method==='GET')path += '?campusId='+encodeURIComponent(campus());
    const response = await fetch(path, {
      method,
      headers: {
        'Accept':'application/json',
        ...(payload ? {'Content-Type':'application/json'} : {}),
        ...(method!=='GET' ? {'RequestVerificationToken':csrf} : {})
      },
      ...(payload ? {body:JSON.stringify(payload)} : {})
    });
    if(!response.ok)throw new Error('Request failed ('+response.status+'). Check academic references and permissions.');
    return response.status===204?null:response.json();
  };
  const options = (id, items, includeNone=false, label=x=>x.name) => {
    const select=el(id);select.replaceChildren();
    if(includeNone) {
      const empty=document.createElement('option');
      empty.value='';empty.textContent='None';select.append(empty);
    }
    for(const item of items) {
      const option=document.createElement('option');
      option.value=item.id;option.textContent=label(item);select.append(option);
    }
  };
  const list = (id,items,label) => {
    const host=el(id);host.replaceChildren();
    for(const item of items) {
      const li=document.createElement('li');
      li.textContent=label(item);host.append(li);
    }
  };
  async function refresh() {
    if(!campus())return;
    const [years,items,events,overview]=await Promise.all([
      api('years'),api('catalog'),api('calendar'),api('overview')
    ]);
    cachedYears=years;cachedItems=items;
    const yearLabel=x=>x.name+' ('+x.code+')';
    options('item-year',years,true,yearLabel);
    options('faculty-year',years,false,yearLabel);
    options('event-year',years,false,yearLabel);
    options('item-parent',items,true,x=>x.kind+' — '+x.name);
    options('faculty-subject',items.filter(x=>x.kind==='SUBJECT'),false);
    list('academic-years',years,yearLabel);
    list('academic-items',items,x=>x.kind+' · '+x.code+' — '+x.name);
    list('academic-events',events,x=>x.startsOn+' · '+x.kind+' — '+x.title);
    el('academic-overview').textContent=[
      'Years: '+overview.years,'Programs: '+overview.programs,
      'Grades: '+overview.grades,'Classes: '+overview.classes,
      'Sections: '+overview.sections,'Courses: '+overview.courses,
      'Subjects: '+overview.subjects,'Batches: '+overview.batches,
      'Faculty assignments: '+overview.assignments,'Calendar entries: '+overview.calendarEvents
    ].join('  |  ');
  }
  const submit = (form, resource, makePayload) => {
    el(form).addEventListener('submit', async event => {
      event.preventDefault();
      try {
        await api(resource,'POST',makePayload());
        el(form).reset();
        await refresh();
        info('Academic record saved.');
      } catch(error) {info(error.message);}
    });
  };
  submit('year-form','years',()=>({
    campusId:campus(),code:val('year-code'),name:val('year-name'),
    startsOn:val('year-start'),endsOn:val('year-end')
  }));
  submit('catalog-form','catalog',()=>({
    campusId:campus(),kind:val('item-kind'),code:val('item-code'),name:val('item-name'),
    parentId:nullable('item-parent'),academicYearId:nullable('item-year'),
    departmentId:nullable('item-department'),
    startsOn:nullable('item-start'),endsOn:nullable('item-end')
  }));
  submit('faculty-form','faculty-assignments',()=>({
    campusId:campus(),subjectId:val('faculty-subject'),
    academicYearId:val('faculty-year'),facultyUserId:val('faculty-person')
  }));
  submit('calendar-form','calendar',()=>({
    campusId:campus(),academicYearId:val('event-year'),
    kind:val('event-kind'),title:val('event-title'),
    startsOn:val('event-start'),endsOn:val('event-end'),notes:null
  }));
  el('academic-campus').addEventListener('change',()=>refresh().catch(e=>info(e.message)));
  async function load() {
    try {
      const [campuses,departments,faculty]=await Promise.all([
        fetch('/bff/institution-admin/campuses').then(r=>r.ok?r.json():Promise.reject(new Error('Campuses unavailable'))),
        fetch('/bff/institution-admin/resources/departments').then(r=>r.ok?r.json():Promise.reject(new Error('Departments unavailable'))),
        fetch('/bff/identity/users').then(r=>r.ok?r.json():Promise.reject(new Error('Faculty directory unavailable')))
      ]);
      options('academic-campus',campuses,false,x=>x.name+' ('+x.code+')');
      options('faculty-person',faculty,false,x=>x.displayName+' ('+x.email+')');
      options('item-department',departments,true,x=>x.name+' ('+x.code+')');
      if(campuses.length)await refresh();
      info(campuses.length?'Academic management ready.':'Add a campus in Institution Management first.');
    } catch(error){info(error.message);}
  }
  load();
})();
