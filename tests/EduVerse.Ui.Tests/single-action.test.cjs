const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const repo = path.resolve(__dirname, '../..');
const behavior = fs.readFileSync(path.join(repo, 'src/EduVerse.DesignSystem/wwwroot/js/operational-workspace.js'), 'utf8');
function files(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(item =>
    item.isDirectory() ? files(path.join(dir, item.name)) : [path.join(dir, item.name)]);
}
const screens = files(path.join(repo, 'src')).filter(file => file.endsWith('.cshtml') &&
  !file.includes('/Shared/') && fs.readFileSync(file, 'utf8').includes('data-ev-action='));
function open(file, hash = '') {
  // Exercise the real view structure without executing Razor or API scripts.
  const html = fs.readFileSync(file, 'utf8').replace(/<script[\s\S]*?<\/script>/g, '');
  const dom = new JSDOM(html, { url: 'https://eduverse.test/' + hash, runScripts: 'outside-only' });
  dom.window.eval(behavior);
  return dom;
}
function visible(node) {
  for (let parent = node; parent; parent = parent.parentElement)
    if (parent.hidden || parent.classList.contains('ev-action-inactive') ||
      parent.classList.contains('ev-task-row-empty')) return false;
  return true;
}
function select(dom, key) {
  dom.window.location.hash = encodeURIComponent(key);
  dom.window.dispatchEvent(new dom.window.HashChangeEvent('hashchange'));
}
for (const file of screens) {
  test(path.relative(repo, file) + ': every declared action is isolated', () => {
    const dom = open(file), doc = dom.window.document;
    const root = doc.querySelector('.ev-module-workspace');
    const markers = [...doc.querySelectorAll('[data-ev-action]')];
    for (const marker of markers) assert.ok(root.contains(marker), `${marker.id} outside workspace`);
    const ids = [...doc.querySelectorAll('[id]')].map(node => node.id);
    assert.equal(new Set(ids).size, ids.length, 'duplicate IDs break existing API bindings');
    for (const form of root.querySelectorAll('form')) {
      if (form.querySelector('button')) assert.ok(form.dataset.evActionKey, `undeclared form ${form.id}`);
    }
    const picker = doc.querySelector('#ev-action-select');
    assert.ok(picker, 'missing action navigation');
    for (const option of picker.options) {
      select(dom, option.value);
      const shown = markers.filter(visible);
      // Platform detail panes legitimately wait for institution selection by the API.
      if (!file.includes('/PlatformConsole/')) assert.ok(shown.length, `empty action ${option.value}`);
      for (const marker of shown) assert.equal(marker.dataset.evActionKey, option.value, 'mixed activities');
      const forms = [...root.querySelectorAll('form')].filter(node => node.querySelector('button') && visible(node));
      assert.ok(forms.length <= 1, 'multiple editable forms visible');
    }
    dom.window.close();
  });
}
test('Action navigation preserves input values, IDs, and attached handlers', () => {
  const dom = open(path.join(repo, 'src/EduVerse.Web/Views/FinanceAdmin/Index.cshtml'));
  const doc = dom.window.document, form = doc.querySelector('#refund-form');
  const input = form.querySelector('input');
  input.value = '37'; let events = 0;
  form.addEventListener('custom-test', () => events++);
  select(dom, 'refund-form'); select(dom, 'reconcile-form'); select(dom, 'refund-form');
  assert.equal(doc.querySelector('#refund-form'), form);
  assert.equal(input.value, '37');
  form.dispatchEvent(new dom.window.Event('custom-test'));
  assert.equal(events, 1);
  dom.window.close();
});
test('Shared exam and student selectors remain visible for dependent actions', () => {
  for (const [file, field, key] of [
    ['ExamAdmin/Index.cshtml', 'exam', 'result-view'],
    ['StudentAdmin/Index.cshtml', 'student', 'enroll-form'],
    ['RolePortal/Student.cshtml', 'p23-campus', 'p23-load-work'],
    ['RolePortal/Parent.cshtml', 'p23-child', 'p23-load-conversations']
  ]) {
    const dom = open(path.join(repo, 'src/EduVerse.Web/Views', file));
    select(dom, key);
    assert.ok(visible(dom.window.document.getElementById(field)), `${file}: ${field} hidden`);
    dom.window.close();
  }
});
test('Legacy section links and direct action URLs resolve to a focused activity', () => {
  const file = path.join(repo, 'src/EduVerse.Web/Views/FinanceAdmin/Index.cshtml');
  for (const [hash, expected] of [['#ev-refunds', 'refund-form'], ['#reconcile-form', 'reconcile-form'], ['#%ZZ', 'fee-form']]) {
    const dom = open(file, hash);
    assert.equal(dom.window.document.querySelector('#ev-action-select').value, expected);
    dom.window.close();
  }
});

test('Platform sidebar anchors resolve to the requested action', () => {
  const file = path.join(repo, 'src/EduVerse.Web/Views/PlatformConsole/Index.cshtml');
  for (const [hash, expected] of [['#platform-institutions-section','platform-refresh'], ['#platform-actions-section','platform-load-actions']]) {
    const dom = open(file, hash);
    assert.equal(dom.window.document.querySelector('#ev-action-select').value, expected);
    dom.window.close();
  }
});
