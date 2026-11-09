// Accessible responsive shell behavior; routing and permissions remain server/API-owned.
(() => {
  const root = document.body;
  const toggle = document.getElementById('ev-nav-toggle');
  const close = document.getElementById('ev-nav-close');
  const backdrop = document.getElementById('ev-nav-backdrop');
  const sidebar = document.getElementById('ev-sidebar');
  if (!toggle || !close || !backdrop || !sidebar) return;

  let previousFocus = null;
  function setOpen(open) {
    root.classList.toggle('ev-nav-open', open);
    toggle.setAttribute('aria-expanded', String(open));
    backdrop.hidden = !open;
    if (open) {
      previousFocus = document.activeElement;
      close.focus();
    } else if (previousFocus && typeof previousFocus.focus === 'function') {
      previousFocus.focus();
      previousFocus = null;
    }
  }
  toggle.addEventListener('click', () => setOpen(true));
  close.addEventListener('click', () => setOpen(false));
  backdrop.addEventListener('click', () => setOpen(false));
  sidebar.addEventListener('click', event => {
    if (event.target.closest('a') && window.innerWidth <= 820) setOpen(false);
  });
  document.addEventListener('keydown', event => {
    if (!root.classList.contains('ev-nav-open')) return;
    if (event.key === 'Escape') { setOpen(false); return; }
    if (event.key !== 'Tab') return;
    const nodes = [...sidebar.querySelectorAll('button:not([disabled]),a[href]')]
      .filter(el => el.getClientRects().length > 0);
    if (!nodes.length) return;
    const first = nodes[0], last = nodes[nodes.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault(); first.focus();
    }
  });
  const mq = window.matchMedia('(min-width: 821px)');
  mq.addEventListener('change', () => {
    if (mq.matches && root.classList.contains('ev-nav-open')) setOpen(false);
  });
  for (const link of sidebar.querySelectorAll('a[href]')) {
    const address = new URL(link.href, window.location.href);
    if (address.pathname === window.location.pathname && !address.hash &&
        !link.hasAttribute('aria-current')) {
      link.setAttribute('aria-current', 'page');
    }
  }
})();
