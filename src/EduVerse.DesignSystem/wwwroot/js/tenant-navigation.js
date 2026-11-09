// Role-aware presentation only. Tenant identity, feature flags, routes and permissions come from the API.
(async function () {
  const nav = document.getElementById('tenant-module-navigation');
  const dashboard = document.getElementById('tenant-module-cards');
  if (!nav && !dashboard) return;

  const el = id => document.getElementById(id);
  const make = (tag, text, className) => {
    const item = document.createElement(tag);
    if (text !== undefined) item.textContent = text;
    if (className) item.className = className;
    return item;
  };

  function renderNavigation(modules) {
    if (!nav) return;
    nav.replaceChildren();
    const enabled = modules.filter(module => module.enabled);
    if (!enabled.length) {
      nav.append(make('p', 'No subscribed modules available.', 'ev-sidebar-hint'));
      return;
    }
    for (const module of enabled) {
      const a = make('a', module.name, 'ev-nav-item');
      a.href = module.route;
      if (location.pathname === module.route ||
          location.pathname.startsWith(module.route + '/')) a.setAttribute('aria-current', 'page');
      nav.append(a);
    }
  }

  function renderModuleCards(modules) {
    if (!dashboard) return;
    const filter = el('institution-module-filter')?.value || 'all';
    dashboard.replaceChildren();
    const selected = modules.filter(x => filter === 'all' ||
      (filter === 'enabled' && x.enabled) || (filter === 'disabled' && !x.enabled));
    if (!selected.length) {
      dashboard.append(make('p', 'No modules match this filter.', 'text-secondary'));
      return;
    }
    for (const module of selected) {
      const column = make('div', undefined, 'col-md-6 col-xl-4');
      const card = make('article', undefined, 'ev-card ev-module-tile p-4 h-100 d-flex flex-column');
      const emblem = make('span', '▦', 'ev-module-symbol');
      emblem.setAttribute('aria-hidden', 'true');
      card.append(emblem);
      card.append(make('h3', module.name, 'h6 fw-semibold mb-2'));
      card.append(make('p', module.description, 'text-secondary small mb-3'));
      const footer = make('div', undefined, 'mt-auto');
      if (module.enabled) {
        const link = make('a', 'Open module', 'btn btn-primary btn-sm');
        link.href = module.route;
        link.setAttribute('aria-label', 'Open ' + module.name);
        footer.append(link);
      } else {
        footer.append(make('span', module.unavailableReason ||
          'Subscription required', 'badge text-bg-secondary'));
      }
      card.append(footer);
      column.append(card);
      dashboard.append(column);
    }
  }

  function renderQuickActions(modules) {
    const host = el('institution-quick-actions');
    if (!host) return;
    host.replaceChildren();
    // The ordering is a presentation preference, never an authorization rule.
    const preferred = ['students', 'academics', 'attendance', 'finance', 'exams', 'users', 'institution'];
    const enabled = modules.filter(x => x.enabled).sort((a, b) => {
      const score = x => {
        const position = preferred.indexOf(x.id);
        return position === -1 ? preferred.length : position;
      };
      return score(a) - score(b);
    });
    const count = el('institution-enabled-count');
    if (count) count.textContent = enabled.length + ' enabled';
    if (!enabled.length) {
      host.append(make('p', 'No quick actions available for the current subscription.', 'text-secondary small'));
      return;
    }
    for (const module of enabled.slice(0, 7)) {
      const link = make('a', module.name, 'btn btn-outline-primary btn-sm');
      link.href = module.route;
      host.append(link);
    }
  }

  async function loadOverview() {
    if (!dashboard) return;
    const message = el('institution-dashboard-message');
    try {
      const result = await fetch('/bff/institution-admin/dashboard', {
        credentials: 'same-origin', headers: { 'Accept': 'application/json' }
      });
      if (!result.ok) throw new Error('Unable to retrieve institution metrics (' + result.status + ')');
      const data = await result.json();
      const metrics = {
        'overview-campus-count': data.campuses,
        'overview-department-count': data.departments,
        'overview-building-count': data.buildings,
        'overview-member-count': data.members
      };
      for (const [id, value] of Object.entries(metrics)) {
        if (el(id)) el(id).textContent = Number.isFinite(value) ? value.toLocaleString() : '—';
      }
      if (el('institution-onboarding')) {
        el('institution-onboarding').textContent = 'Onboarding: ' + (data.onboardingStatus || 'Unknown');
      }
    } catch (error) {
      if (message) {
        message.textContent = 'Institution metrics could not be loaded. ' + error.message;
        message.className = 'small text-danger mt-3';
      }
    }
  }

  try {
    const result = await fetch('/bff/tenant/modules', {
      credentials: 'same-origin', headers: { 'Accept': 'application/json' }
    });
    if (!result.ok) throw new Error('Unable to retrieve institution modules (' + result.status + ')');
    const data = await result.json();
    renderNavigation(data.modules);
    if (el('institution-name')) el('institution-name').textContent =
      data.institutionName + ' · ' + data.institutionKind;
    if (el('institution-plan')) el('institution-plan').textContent =
      'Subscription: ' + (data.planCode || 'Unassigned');
    if (el('institution-type')) el('institution-type').textContent = data.institutionKind;
    renderQuickActions(data.modules);
    renderModuleCards(data.modules);
    const filter = el('institution-module-filter');
    if (filter) filter.addEventListener('change', () => renderModuleCards(data.modules));
    await loadOverview();
  } catch (error) {
    for (const host of [nav, dashboard].filter(Boolean)) {
      host.replaceChildren();
      host.append(make('p', 'Unable to retrieve institution modules. Check API connectivity and sign-in.',
        'text-danger small'));
    }
    const message = el('institution-dashboard-message');
    if (message) message.textContent = error.message;
  }
})();
