(() => {
  const body = document.body;
  const toggleButton = document.querySelector('[data-sidebar-toggle]');
  const overlay = document.querySelector('[data-sidebar-overlay]');
  const themeToggle = document.querySelector('[data-theme-toggle]');
  const themeLabel = document.querySelector('[data-theme-label]');

  const setTheme = (theme) => {
    body.dataset.theme = theme;
    try {
      localStorage.setItem('wsb.theme', theme);
    } catch {
    }

    if (themeLabel) {
      themeLabel.textContent = theme === 'light' ? 'Modo escuro' : 'Modo claro';
    }
  };

  const setOpen = (open) => {
    if (open) {
      body.classList.add('sidebar-open');
      localStorage.setItem('wsb.sidebarOpen', '1');
    } else {
      body.classList.remove('sidebar-open');
      localStorage.setItem('wsb.sidebarOpen', '0');
    }
  };

  const isMobile = () => window.matchMedia('(max-width: 1024px)').matches;

  try {
    const persisted = localStorage.getItem('wsb.sidebarOpen');
    if (persisted === '1') setOpen(true);
    if (persisted === '0') setOpen(false);
  } catch {
  }

  try {
    const persistedTheme = localStorage.getItem('wsb.theme');
    if (persistedTheme === 'light' || persistedTheme === 'dark') {
      setTheme(persistedTheme);
    } else {
      setTheme('dark');
    }
  } catch {
    setTheme('dark');
  }

  if (toggleButton) {
    toggleButton.addEventListener('click', () => setOpen(!body.classList.contains('sidebar-open')));
  }

  if (themeToggle) {
    themeToggle.addEventListener('click', () => {
      const current = body.dataset.theme === 'light' ? 'light' : 'dark';
      setTheme(current === 'light' ? 'dark' : 'light');
    });
  }

  if (overlay) {
    overlay.addEventListener('click', () => setOpen(false));
  }

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') setOpen(false);
  });

  window.addEventListener('resize', () => {
    if (!isMobile()) setOpen(true);
    if (isMobile() && localStorage.getItem('wsb.sidebarOpen') !== '1') setOpen(false);
  });

  try {
    if (!isMobile()) setOpen(true);
  } catch {
  }

  const navLinks = document.querySelectorAll('.nav a[href]');
  if (navLinks.length) {
    const path = window.location.pathname || '';
    for (const link of navLinks) {
      const href = link.getAttribute('href') || '';
      if (href === '/admin' && path === '/admin') link.classList.add('active');
      else if (href !== '/admin' && href.length > 1 && path.startsWith(href)) link.classList.add('active');
    }
  }
})();
