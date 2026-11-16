// Presentation-only record filtering on an individual activity page.
(() => {
  const root=document.querySelector('[data-activity-page]');
  if(!root)return;
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
