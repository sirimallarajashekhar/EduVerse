// Reusable presentation helpers. No API calls, permissions, records, or calculations are defined here.
(() => {
  const root = document.querySelector('.ev-module-workspace');
  if (!root) return;
  const tabs = [...root.querySelectorAll('.ev-module-tab[href^="#"]')];
  const activate = () => {
    const hash = decodeURIComponent(window.location.hash || '');
    for (const tab of tabs) {
      if (hash && tab.getAttribute('href') === hash) tab.setAttribute('aria-current', 'location');
      else tab.removeAttribute('aria-current');
    }
  };
  window.addEventListener('hashchange', activate);
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
