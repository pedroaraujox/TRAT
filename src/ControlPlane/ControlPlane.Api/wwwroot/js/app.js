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

  const bucketSelect = document.querySelector('[data-aws-bucket-select]');
  const prefixSelect = document.querySelector('[data-aws-prefix-select]');
  if (bucketSelect && prefixSelect) {
    const endpoint = bucketSelect.getAttribute('data-prefix-endpoint') || '';
    const expectedAccountId = bucketSelect.getAttribute('data-expected-account-id') || '';
    const regionTargetSelector = bucketSelect.getAttribute('data-region-target') || '';
    const regionTarget = regionTargetSelector ? document.querySelector(regionTargetSelector) : null;

    const updatePrefixOptions = async () => {
      const bucketName = bucketSelect.value || '';
      const currentPrefix = prefixSelect.value || prefixSelect.getAttribute('data-current-prefix') || '';
      if (!endpoint || !bucketName) {
        return;
      }

      const url = new URL(endpoint, window.location.origin);
      url.searchParams.set('expectedAccountId', expectedAccountId);
      url.searchParams.set('bucketName', bucketName);

      try {
        const response = await fetch(url.toString(), {
          headers: {
            'X-Requested-With': 'XMLHttpRequest'
          }
        });
        if (!response.ok) {
          return;
        }

        const payload = await response.json();
        if (!payload || !Array.isArray(payload.prefixes)) {
          return;
        }

        prefixSelect.innerHTML = '';
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'Selecione um prefixo';
        prefixSelect.appendChild(placeholder);

        const seen = new Set();
        for (const prefix of payload.prefixes) {
          if (!prefix || seen.has(prefix)) continue;
          const option = document.createElement('option');
          option.value = prefix;
          option.textContent = prefix;
          if (prefix === currentPrefix) option.selected = true;
          prefixSelect.appendChild(option);
          seen.add(prefix);
        }

        if (currentPrefix && !seen.has(currentPrefix)) {
          const custom = document.createElement('option');
          custom.value = currentPrefix;
          custom.textContent = currentPrefix;
          custom.selected = true;
          prefixSelect.appendChild(custom);
        }

        if (regionTarget && typeof payload.bucketRegion === 'string' && payload.bucketRegion) {
          regionTarget.value = payload.bucketRegion;
        }
      } catch {
      }
    };

    bucketSelect.addEventListener('change', () => {
      prefixSelect.setAttribute('data-current-prefix', '');
      updatePrefixOptions();
    });
  }

  const autoRefreshHost = document.querySelector('[data-auto-refresh-seconds][data-auto-refresh-active="true"]');
  if (autoRefreshHost) {
    const seconds = Number.parseInt(autoRefreshHost.getAttribute('data-auto-refresh-seconds') || '0', 10);
    if (Number.isFinite(seconds) && seconds >= 5) {
      window.setTimeout(() => window.location.reload(), seconds * 1000);
    }
  }
})();
