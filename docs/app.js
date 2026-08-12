const menuButton = document.querySelector('[data-menu-button]');
const navLinks = document.querySelector('[data-nav-links]');
const header = document.querySelector('[data-header]');

function closeMenu() {
  menuButton?.setAttribute('aria-expanded', 'false');
  navLinks?.classList.remove('open');
}

menuButton?.addEventListener('click', () => {
  const open = menuButton.getAttribute('aria-expanded') === 'true';
  menuButton.setAttribute('aria-expanded', String(!open));
  navLinks?.classList.toggle('open', !open);
});

navLinks?.querySelectorAll('a').forEach((link) => link.addEventListener('click', closeMenu));

window.addEventListener('scroll', () => {
  header?.classList.toggle('scrolled', window.scrollY > 20);
}, { passive: true });

const copyButton = document.querySelector('[data-copy-command]');
const command = document.querySelector('#install-command');

copyButton?.addEventListener('click', async () => {
  if (!command) return;
  try {
    await navigator.clipboard.writeText(command.textContent ?? '');
    const label = copyButton.querySelector('span');
    if (!label) return;
    label.textContent = 'Copied';
    copyButton.classList.add('copied');
    window.setTimeout(() => {
      label.textContent = 'Copy command';
      copyButton.classList.remove('copied');
    }, 1800);
  } catch {
    command.closest('.terminal-line')?.classList.add('select-command');
    window.getSelection()?.selectAllChildren(command);
    const label = copyButton.querySelector('span');
    if (!label) return;
    label.textContent = 'Command selected';
    window.setTimeout(() => {
      label.textContent = 'Copy command';
      command.closest('.terminal-line')?.classList.remove('select-command');
    }, 1800);
  }
});

const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
if (reducedMotion || !('IntersectionObserver' in window)) {
  document.querySelectorAll('.reveal').forEach((element) => element.classList.add('visible'));
} else {
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    });
  }, { threshold: 0.12 });
  document.querySelectorAll('.reveal').forEach((element) => observer.observe(element));
}
