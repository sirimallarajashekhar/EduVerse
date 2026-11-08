// Thin client presentation. All feature and tenant decisions originate from API.
(async function () {
  const nav=document.getElementById('tenant-module-navigation');
  const dashboard=document.getElementById('tenant-module-cards');
  if(!nav && !dashboard)return;
  try {
    const response=await fetch('/bff/tenant/modules', {
      credentials:'same-origin',headers:{'Accept':'application/json'}
    });
    if(!response.ok)throw new Error('Institution modules unavailable');
    const data=await response.json();
    if(nav)nav.replaceChildren();
    if(dashboard)dashboard.replaceChildren();
    const name=document.getElementById('institution-name');
    if(name)name.textContent=data.institutionName+' · '+data.institutionKind;
    const plan=document.getElementById('institution-plan');
    if(plan)plan.textContent='Subscription: '+(data.planCode||'Unassigned');
    for(const module of data.modules) {
      if(nav){
        const element=document.createElement(module.enabled?'a':'span');
        element.className='ev-nav-item'+(module.enabled?'':' text-white-50');
        if(module.enabled)element.setAttribute('href',module.route);
        else element.setAttribute('title',module.unavailableReason||'Unavailable');
        element.textContent=module.name+(module.enabled?'':' · Subscription required');
        if(module.enabled&&(location.pathname===module.route||
          location.pathname.startsWith(module.route+'/')))element.setAttribute('aria-current','page');
        nav.append(element);
      }
      if(dashboard){
        const column=document.createElement('div');
        column.className='col-md-6 col-xl-4';
        const card=document.createElement('section');
        card.className='ev-card p-4 h-100';
        const title=document.createElement('h2');
        title.className='h5';
        title.textContent=module.name;
        const description=document.createElement('p');
        description.className='text-secondary';
        description.textContent=module.description;
        const action=document.createElement(module.enabled?'a':'span');
        action.className=module.enabled?'btn btn-sm btn-primary':'badge text-bg-secondary';
        if(module.enabled)action.href=module.route;
        action.textContent=module.enabled?'Open module':'Subscription required';
        card.append(title,description,action);
        column.append(card);
        dashboard.append(column);
      }
    }
  } catch {
    for(const el of [nav,dashboard].filter(Boolean)){
      el.replaceChildren();
      const warning=document.createElement('p');
      warning.className='text-danger small';
      warning.textContent='Unable to retrieve institution modules. Verify API connectivity and sign-in.';
      el.append(warning);
    }
  }
})();
