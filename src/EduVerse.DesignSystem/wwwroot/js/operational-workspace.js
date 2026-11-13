// Reusable presentation helpers. No API calls, permissions, records, or calculations are defined here.
(() => {
  const root = document.querySelector('.ev-module-workspace');
  if (!root) return;
  // Keep existing API-bound elements mounted so changing views preserves unsaved input.
  let tabs = [...root.querySelectorAll('.ev-module-tab[href^="#"]')];
  if (!tabs.length) {
    const cards = [...root.querySelectorAll('.ev-card')].filter(card =>
      card.querySelector('h2') && !card.parentElement.closest('.ev-card'));
    if (cards.length > 1) {
      const nav = document.createElement('nav');
      nav.className = 'ev-module-tabs';
      nav.setAttribute('aria-label', 'Choose a task');
      cards.forEach((card, index) => {
        const heading = card.querySelector('h2');
        heading.id ||= `ev-task-${index + 1}`;
        const link = document.createElement('a');
        link.className = 'ev-module-tab';
        link.href = `#${heading.id}`;
        link.textContent = heading.textContent;
        nav.append(link);
      });
      const heading = root.querySelector('h1');
      (heading?.closest('header') || heading)?.after(nav);
      tabs = [...nav.children];
    }
  }
  const views = tabs.map(tab => {
    const heading = document.getElementById(tab.hash.slice(1));
    const panel = heading?.closest('.ev-card');
    if (!panel || !root.contains(panel)) return null;
    const host = panel.parentElement.matches('[class*="col-"]') ? panel.parentElement : panel;
    panel.classList.add('ev-task-view');
    if (host !== panel) host.classList.add('ev-task-column');
    panel.setAttribute('aria-labelledby', heading.id);
    return { tab, panel, host };
  }).filter(Boolean);
  const activate = (moveFocus = false) => {
    let hash;
    try { hash = decodeURIComponent(window.location.hash); } catch { hash = ''; }
    const active = views.find(view => view.tab.hash === hash ||
      (hash && view.panel.contains(document.getElementById(hash.slice(1))))) || views[0];
    for (const view of views) {
      const selected = view === active;
      view.panel.hidden = !selected;
      if (selected) view.tab.setAttribute('aria-current', 'location');
      else view.tab.removeAttribute('aria-current');
    }
    for (const view of views) {
      if (view.host !== view.panel)
        view.host.hidden = !views.some(other => other.host === view.host && !other.panel.hidden);
    }
    // Empty Bootstrap rows must not leave blank space between the task selector and content.
    for (const row of root.querySelectorAll('.row')) {
      if ([...row.children].some(child => child.classList.contains('ev-task-view') || child.classList.contains('ev-task-column')))
        row.classList.toggle('ev-task-row-empty', [...row.children].every(child => child.hidden));
    }
    if (moveFocus && active) {
      const heading = active.panel.querySelector('h2');
      heading.setAttribute('tabindex', '-1');
      heading.focus({ preventScroll: true });
    }
  };
  window.addEventListener('hashchange', () => activate(true));
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
