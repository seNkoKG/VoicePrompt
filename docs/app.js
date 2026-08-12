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

const interfaceShowcase = document.querySelector('[data-interface-showcase]');
const interfaceImage = interfaceShowcase?.querySelector('[data-interface-image]');
const interfaceTitle = interfaceShowcase?.querySelector('[data-interface-title]');
const interfaceCopy = interfaceShowcase?.querySelector('[data-interface-copy]');
const interfaceTabs = [...(interfaceShowcase?.querySelectorAll('[role="tab"]') ?? [])];

function selectInterfaceTab(selected) {
  if (!interfaceImage || !interfaceTitle || !interfaceCopy) return;
  interfaceTabs.forEach((tab) => {
    const active = tab === selected;
    tab.setAttribute('aria-selected', String(active));
    tab.tabIndex = active ? 0 : -1;
  });
  interfaceImage.src = selected.dataset.shot;
  interfaceImage.alt = selected.dataset.alt;
  interfaceTitle.textContent = selected.dataset.title;
  interfaceCopy.textContent = selected.dataset.copy;
}

interfaceTabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectInterfaceTab(tab));
  tab.addEventListener('keydown', (event) => {
    if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault();
    const offset = event.key === 'ArrowRight' ? 1 : -1;
    const next = interfaceTabs[(index + offset + interfaceTabs.length) % interfaceTabs.length];
    selectInterfaceTab(next);
    next.focus();
  });
});

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

const themeButtons = [...document.querySelectorAll('[data-theme-switcher] [data-theme]')];
const themeColors = { graphite: '#0c0e10', evergreen: '#080d0c', ember: '#0e0c0b' };

function applyTheme(theme, persist = true) {
  const selected = themeButtons.some((button) => button.dataset.theme === theme) ? theme : 'graphite';
  document.documentElement.dataset.theme = selected;
  themeButtons.forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.theme === selected)));
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', themeColors[selected]);
  if (!persist) return;
  try { localStorage.setItem('voiceprompt-theme', selected); } catch { /* Preference stays in this tab. */ }
}

let savedTheme = 'graphite';
try { savedTheme = localStorage.getItem('voiceprompt-theme') || savedTheme; } catch { /* Storage can be disabled. */ }
applyTheme(savedTheme, false);
themeButtons.forEach((button) => button.addEventListener('click', () => applyTheme(button.dataset.theme)));

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
