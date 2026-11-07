// Presentation-only code. All system data comes from the relevant REST API through a thin same-origin proxy.
(async () => {
  const element = document.getElementById('api-status');
  if (!element) return;
  try {
    const response = await fetch('/bff/system/info', { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error('API unavailable');
    const info = await response.json();
    element.textContent = info.application + ' API ' + info.apiVersion + ' is reachable.';
    element.className = 'mb-0 text-success';
  } catch {
    element.textContent = 'API is unavailable. Check whether EduVerse.Api is running.';
    element.className = 'mb-0 text-danger';
  }
})();
