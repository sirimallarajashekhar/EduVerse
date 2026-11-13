// Presentation-only module navigation. Module discovery is owned by the Platform API.
(async function loadModules() {
  const host = document.getElementById('module-navigation');
  if (!host) return;
  if (document.getElementById('tenant-module-navigation')) { host.hidden=true; return; }
  try {
    const response = await fetch('/bff/platform/modules', { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error('Module registry unavailable');
    const modules = await response.json();
    host.replaceChildren();
    for (const module of modules) {
      // Build DOM nodes without HTML insertion to avoid scripting through labels/URLs.
      if (!['ams', 'hrms'].includes(module.id)) continue;
      const link = document.createElement('a');
      link.className = 'ev-nav-item';
      link.href = module.routePrefix;
      link.textContent = module.displayName;
      if (location.pathname === module.routePrefix || location.pathname.startsWith(module.routePrefix + '/')) {
        link.setAttribute('aria-current', 'page');
      }
      host.append(link);
    }
  } catch {
    host.replaceChildren();
    const warning = document.createElement('span');
    warning.className = 'text-white-50 small';
    warning.textContent = 'Modules unavailable';
    host.append(warning);
  }
})();
