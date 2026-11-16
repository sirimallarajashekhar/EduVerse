// Presentation-only UI: validation and tenant/role enforcement are owned by REST services.
(() => {
  const base = '/bff/institution-admin/';
  const el = (id) => document.getElementById(id);
  const token = () => document.querySelector('[name="__RequestVerificationToken"]')?.value;
  const message = (value) => {el('institution-message') ? el('institution-message').textContent = value : void 0;};
  const toNullable = (id) => el(id)?.value?.trim() || null;
  async function api(path, method = 'GET', body) {
    const response = await fetch(base + path, {
      method,
      headers: {
        'Accept': 'application/json',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(method !== 'GET' ? { 'RequestVerificationToken': token() } : {})
      },
      ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) throw new Error('Request rejected (' + response.status + ')');
    return response.status === 204 ? null : response.json();
  }
  function renderItems(container, values, map) {
    const host = el(container);host?.replaceChildren();
    for (const item of values) {
      const li = document.createElement('li');
      li.textContent = map(item);host?.append(li);
    }
  }
  async function refresh() {
    const [dashboard, profile, settings, departments, buildings, campuses, plans] = await Promise.all([
    api('dashboard'), api('profile'), api('settings'), api('resources/departments'),
    api('resources/buildings'), api('campuses'), api('plans')]
    );
    el('campus-count') ? el('campus-count').textContent = dashboard.campuses : void 0;
    el('department-count') ? el('department-count').textContent = dashboard.departments : void 0;
    el('building-count') ? el('building-count').textContent = dashboard.buildings : void 0;
    el('member-count') ? el('member-count').textContent = dashboard.members : void 0;
    el('onboarding-status') ? el('onboarding-status').textContent = dashboard.onboardingStatus : void 0;
    const subscription = dashboard.subscription;
    el('subscription-status') ? el('subscription-status').textContent = (subscription.planCode || 'No assigned plan') + ' · ' + subscription.status : void 0;
    el('module-features') ? el('module-features').textContent = 'Modules: ' + (subscription.features.join(', ') || 'None enabled') : void 0;
    for (const name of ['legalName', 'contactEmail', 'contactPhone', 'addressLine', 'city', 'stateRegion', 'postalCode'])
    el(name) ? el(name).value = profile?.[name] || '' : void 0;
    renderItems('settings-list', settings, (x) => x.key + ': ' + x.value);
    renderItems('campuses-list', campuses, (x) => x.code + ' — ' + x.name);
    renderItems('plans-list', plans, (x) => x.name + ' — ' + x.description);
    renderItems('departments-list', departments, (x) => x.code + ' — ' + x.name);
    renderItems('buildings-list', buildings, (x) => x.code + ' — ' + x.name);
    const menu = el('resource-campus');menu?.replaceChildren();
    for (const campus of campuses) {
      const option = document.createElement('option');
      option.value = campus.id;option.textContent = campus.name + ' (' + campus.code + ')';menu?.append(option);
    }
  }
  el('profile-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();message('Saving institution profile…');
    const payload = {};
    for (const field of ['legalName', 'contactEmail', 'contactPhone', 'addressLine', 'city', 'stateRegion', 'postalCode'])
    payload[field] = toNullable(field);
    try {await api('profile', 'PUT', payload);await refresh();message('Profile saved.');}
    catch (error) {message(error.message);}
  });
  el('mark-ready')?.addEventListener('click', async () => {
    try {await api('onboarding/ready', 'POST');await refresh();message('Onboarding marked ready.');}
    catch (error) {message(error.message);}
  });
  el('setting-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    try {
      await api('settings', 'PUT', { key: el('setting-key')?.value, value: el('setting-value')?.value });
      await refresh();message('Setting saved.');
    } catch (error) {message(error.message);}
  });
  el('campus-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    try {
      await api('campuses', 'POST', { code: el('campus-code')?.value, name: el('campus-name')?.value });
      el('campus-code') ? el('campus-code') ? el('campus-code').value = '' : void 0 : void 0;el('campus-name') ? el('campus-name') ? el('campus-name').value = '' : void 0 : void 0;
      await refresh();message('Campus created.');
    } catch (error) {message(error.message);}
  });
  el('resource-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const type = el('resource-type')?.value;
    try {
      await api(type + '/new', 'POST', {
        campusId: el('resource-campus')?.value,
        code: el('resource-code')?.value, name: el('resource-name')?.value
      });
      el('resource-code') ? el('resource-code') ? el('resource-code').value = '' : void 0 : void 0;el('resource-name') ? el('resource-name') ? el('resource-name').value = '' : void 0 : void 0;
      await refresh();message('Resource created.');
    } catch (error) {message(error.message);}
  });
  refresh().catch((error) => message('Institution data unavailable: ' + error.message));
})();
