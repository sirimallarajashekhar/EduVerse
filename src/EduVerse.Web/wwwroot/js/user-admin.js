// Only display and proxy operations. Validation, permission enforcement and persistence live in the Identity API.
(async function () {
  const rows = document.getElementById('user-rows');
  const form = document.getElementById('invite-form');
  if (!rows || !form) return;
  async function load() {
    try {
      const response = await fetch('/bff/identity/users', {headers: {Accept: 'application/json'}});
      if (!response.ok) throw new Error('Cannot load users');
      const users = await response.json();
      rows.replaceChildren();
      for (const user of users) {
        const tr = document.createElement('tr');
        for (const value of [user.displayName, user.email, user.roleCode]) {
          const td = document.createElement('td');
          td.textContent = String(value ?? '');
          tr.appendChild(td);
        }
        const action = document.createElement('td');
        const deactivate = document.createElement('button');
        deactivate.type = 'button';
        deactivate.className = 'btn btn-sm btn-outline-danger';
        deactivate.textContent = 'Deactivate';
        deactivate.addEventListener('click', async () => {
          if (!window.confirm('Deactivate this user in the current institution?')) return;
          const csrf = form.querySelector('[name="__RequestVerificationToken"]').value;
          try {
            const response = await fetch('/bff/identity/users/' + encodeURIComponent(user.id) + '/deactivate',
              {method: 'POST', headers: {RequestVerificationToken: csrf}});
            if (!response.ok) throw new Error('Request failed');
            await load();
          } catch {
            document.getElementById('invite-message').textContent = 'Unable to deactivate this user.';
          }
        });
        action.appendChild(deactivate);
        tr.appendChild(action);
        rows.appendChild(tr);
      }
    } catch {
      rows.replaceChildren();
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 4;
      td.textContent = 'Users cannot be loaded.';
      tr.appendChild(td);
      rows.appendChild(tr);
    }
  }
  form.addEventListener('submit', async event => {
    event.preventDefault();
    const message = document.getElementById('invite-message');
    message.textContent = 'Submitting invitation…';
    try {
      const token = form.querySelector('[name="__RequestVerificationToken"]').value;
      const response = await fetch('/bff/identity/users', {
        method: 'POST',
        headers: {'Content-Type': 'application/json', RequestVerificationToken: token},
        body: JSON.stringify({
          email: form.querySelector('#email').value,
          displayName: form.querySelector('#displayName').value,
          roleCode: form.querySelector('#roleCode').value
        })
      });
      if (!response.ok) throw new Error('Invitation rejected');
      message.textContent = 'Invitation submitted to Identity API.';
      form.reset();
      await load();
    } catch { message.textContent = 'Could not invite user. Confirm access and account details.'; }
  });
  await load();
})();
