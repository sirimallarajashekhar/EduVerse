// Presentation only: each declared action has its own address and visible content.
// Elements stay mounted; API handlers, form values and record permissions are unchanged.
(() => {
  const root = document.querySelector('.ev-module-workspace');
  if (!root) return;
  const declared = [...root.querySelectorAll('[data-ev-action]')];
  if (!declared.length) return;
  const groups = new Map();
  for (const node of declared) {
    const key = node.dataset.evActionKey || node.id;
    if (!groups.has(key)) groups.set(key, { key, label: node.dataset.evAction, nodes: [] });
    groups.get(key).nodes.push(node);
  }
  const actions = [...groups.values()];
  const cards = [...root.querySelectorAll('.ev-card')].filter(card =>
    declared.some(node => node === card || card.contains(node)));
  const oldTabs = [...root.querySelectorAll('.ev-module-tabs')];
  const legacy = new Map();
  for (const link of root.querySelectorAll('.ev-module-tab')) {
    const target = document.getElementById(link.hash.slice(1));
    const card = target?.closest('.ev-card');
    const first = actions.find(action => action.nodes.some(node => node === card || card?.contains(node)));
    if (first) legacy.set(link.hash.slice(1), first);
  }
  for (const target of root.querySelectorAll('.ev-card[id],h2[id],h3[id]')) {
    const card = target.closest('.ev-card');
    const first = actions.find(action => action.nodes.some(node => node === card || card?.contains(node)));
    if (first && !legacy.has(target.id)) legacy.set(target.id, first);
  }
  oldTabs.forEach(nav => nav.remove());
  const nav = document.createElement('div');
  nav.className = 'ev-action-navigation ev-card';
  const label = document.createElement('label');
  label.className = 'form-label'; label.htmlFor = 'ev-action-select';
  label.textContent = 'What would you like to do?';
  const select = document.createElement('select');
  select.id = 'ev-action-select'; select.className = 'form-select';
  for (const action of actions) {
    const option = document.createElement('option');
    option.value = action.key; option.textContent = action.label;
    select.append(option);
  }
  const title = document.createElement('h2');
  title.className = 'ev-current-action'; title.id = 'ev-current-action';
  title.tabIndex = -1;
  nav.append(label, select);
  const heading = root.querySelector('h1');
  (heading?.closest('header') || heading)?.after(nav);
  nav.after(title);
  const prerequisite = document.createElement('p');
  prerequisite.className = 'alert alert-info'; prerequisite.hidden = true;
  prerequisite.setAttribute('role', 'status');
  const choose = document.createElement('a'); choose.href = '#platform-refresh';
  choose.textContent = 'Find and select an institution first.';
  prerequisite.append(choose); title.after(prerequisite);

  // Shared record selectors must stay available when their dependent action is selected.
  const shared = [];
  function share(id, keys) {
    const field = root.querySelector(`[id="${id}"]`);
    if (!field || !field.closest('.ev-card')) return;
    const owner = field.closest('form');
    if (owner?.id) field.setAttribute('form', owner.id);
    const box = document.createElement('div'); box.className = 'ev-action-context ev-card';
    const fieldLabel = root.querySelector(`label[for="${id}"]`);
    if (fieldLabel) box.append(fieldLabel);
    box.append(field); title.after(box);
    shared.push({box, keys});
  }
  if (root.querySelector('#salary-form')) share('employee', ['lifecycle-form','document-form','leave-form','salary-form','payroll-form','load-attendance','documents','leaves','attendance','leave-balance','payroll-result']);
  if (root.querySelector('#allocate-form')) share('asset', ['allocate-form','maintenance-form','asset-events','RETURN','MAINTENANCE','REPAIR','RETIRE','DISPOSE']);
  if (root.querySelector('#student-form')) share('student', ['guardian-form','enroll-form','lifecycle-form','document-form','guardians','enrollments','documents','student-detail']);
  if (root.querySelector('#exam-form')) share('exam', ['assessment-form','mark-form','approve','publish','assessments','mark-list','result-view','result-download']);
  if (root.querySelector('#finalize-form')) share('enrollment', ['registration-form','finalize-form','registrations','transcript','term-results']);
  if (root.querySelector('#clearance-form')) share('exit-employee', ['clearance-form','exit-button']);
  if (root.querySelector('#phase23-student-submit')) {
    share('p23-student', ['phase23-student-submit','p23-load-work']);
    share('p23-campus', ['phase23-student-submit','p23-load-work']);
  }
  if (root.querySelector('#phase23-parent-message')) {
    share('p23-child', ['phase23-parent-message','p23-load-conversations']);
    share('p23-campus', ['phase23-parent-message','p23-load-conversations','p23-load-notifications']);
  }
  if (root.querySelector('#attendance-form') && root.querySelector('#load-classes'))
    share('campus', actions.map(action => action.key));

  // Use a class separate from `hidden`: API-owned empty/error/selection states remain intact.
  function renderBranch(node, activeNodes) {
    if (declared.includes(node)) {
      node.classList.toggle('ev-action-inactive', !activeNodes.includes(node));
      return;
    }
    const contained = declared.filter(item => node.contains(item));
    if (contained.length) {
      const visible = activeNodes.some(item => node.contains(item));
      node.classList.toggle('ev-action-inactive', !visible);
      if (visible) for (const child of node.children) renderBranch(child, activeNodes);
    } else if (node.matches('h2,h3,hr')) {
      node.classList.add('ev-action-inactive');
    }
  }
  function activate(focus = false) {
    let key;
    try { key = decodeURIComponent(location.hash.slice(1)); } catch { key = ''; }
    const action = groups.get(key) || legacy.get(key) || actions.find(item =>
      item.nodes.some(node => node.id === key || (key && node.contains(document.getElementById(key))))) || actions[0];
    select.value = action.key; title.textContent = action.label;
    prerequisite.hidden = !action.nodes.some(node => node.closest('#platform-selected')?.hidden);
    for (const node of declared) node.classList.toggle('ev-action-inactive', !action.nodes.includes(node));
    for (const card of cards) {
      const selected = action.nodes.some(node => node === card || card.contains(node));
      card.classList.toggle('ev-action-inactive', !selected);
      card.classList.add('ev-action-card');
      if (selected) {
        if (declared.includes(card)) renderBranch(card, action.nodes);
        else for (const child of card.children) renderBranch(child, action.nodes);
      }
      const column = card.parentElement;
      if (column.matches('[class*="col-"]')) {
        column.classList.add('ev-task-column');
        column.classList.toggle('ev-action-inactive', !cards.some(other =>
          other.parentElement === column && action.nodes.some(node => node === other || other.contains(node))));
      }
    }
    for (const row of root.querySelectorAll('.row')) {
      if ([...row.children].some(child => child.classList.contains('ev-task-column')))
        row.classList.toggle('ev-task-row-empty', [...row.children].every(child => child.classList.contains('ev-action-inactive')));
    }
    for (const context of shared) context.box.hidden = !context.keys.includes(action.key) &&
      !action.nodes.some(node => context.keys.includes(node.dataset.action));
    if (focus) title.focus({preventScroll:true});
    root.dispatchEvent(new CustomEvent('ev:actionchange', { detail: { key: action.key } }));
  }
  select.addEventListener('change', () => {
    location.hash = encodeURIComponent(select.value);
  });
  window.addEventListener('hashchange', () => activate(true));
  root.addEventListener('ev:refresh-action', () => activate());
  activate();
  for (const search of root.querySelectorAll('[data-ev-filter-target]')) {
    const targetId = search.getAttribute('data-ev-filter-target');
    const list = document.getElementById(targetId);
    if (!list) continue;
    const table = list.closest('table');
    const hint = document.createElement('p');
    hint.className = 'ev-filter-empty';
    hint.setAttribute('role', 'status');
    hint.hidden = true;
    (table?.parentElement || list.parentElement).append(hint);

    function filter() {
      const term = search.value.trim().toLocaleLowerCase();
      const elements = [...list.children].filter(x => x.matches('tr,li'));
      let matched = 0;
      for (const item of elements) {
        const visible = !term || (item.textContent || '').toLocaleLowerCase().includes(term);
        item.hidden = !visible;
        if (visible) matched++;
      }
      hint.hidden = !term || matched > 0 || elements.length === 0;
      hint.textContent = term && matched === 0 ? 'No matching records on this page.' : '';
    }
    search.addEventListener('input', filter);
    // API-populated rows can change after initial page load.
    const observer = new MutationObserver(filter);
    observer.observe(list, { childList:true, subtree:false, characterData:false });
    filter();
  }
})();
