(function () {
  'use strict';

  var body = document.body;
  var toggleButton = document.querySelector('[data-sidebar-toggle]');
  var overlay = document.querySelector('[data-sidebar-overlay]');
  var themeToggle = document.querySelector('[data-theme-toggle]');
  var themeLabel = document.querySelector('[data-theme-label]');

  function setTheme(theme) {
    body.setAttribute('data-theme', theme);
    try {
      window.localStorage.setItem('wsb.theme', theme);
    } catch (ignore) {
    }

    if (themeLabel) {
      themeLabel.textContent = theme === 'light' ? 'Modo escuro' : 'Modo claro';
    }
  }

  function setOpen(open) {
    if (open) {
      body.classList.add('sidebar-open');
    } else {
      body.classList.remove('sidebar-open');
    }

    try {
      window.localStorage.setItem('wsb.sidebarOpen', open ? '1' : '0');
    } catch (ignore) {
    }
  }

  function isMobile() {
    return window.matchMedia && window.matchMedia('(max-width: 1024px)').matches;
  }

  try {
    var persisted = window.localStorage.getItem('wsb.sidebarOpen');
    if (persisted === '1') setOpen(true);
    if (persisted === '0') setOpen(false);
  } catch (ignore) {
  }

  try {
    var persistedTheme = window.localStorage.getItem('wsb.theme');
    setTheme(persistedTheme === 'light' || persistedTheme === 'dark' ? persistedTheme : 'dark');
  } catch (ignore) {
    setTheme('dark');
  }

  if (toggleButton) {
    toggleButton.addEventListener('click', function () {
      setOpen(!body.classList.contains('sidebar-open'));
    });
  }

  if (themeToggle) {
    themeToggle.addEventListener('click', function () {
      var current = body.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
      setTheme(current === 'light' ? 'dark' : 'light');
    });
  }

  if (overlay) {
    overlay.addEventListener('click', function () { setOpen(false); });
  }

  window.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' || event.keyCode === 27) setOpen(false);
  });

  window.addEventListener('resize', function () {
    var savedOpen = null;
    try {
      savedOpen = window.localStorage.getItem('wsb.sidebarOpen');
    } catch (ignore) {
    }
    if (!isMobile()) setOpen(true);
    if (isMobile() && savedOpen !== '1') setOpen(false);
  });

  if (!isMobile()) setOpen(true);

  var navLinks = document.querySelectorAll('.nav a[href]');
  var path = window.location.pathname || '';
  for (var navIndex = 0; navIndex < navLinks.length; navIndex += 1) {
    var link = navLinks[navIndex];
    var href = link.getAttribute('href') || '';
    if ((href === '/admin' && path === '/admin') ||
        (href !== '/admin' && href.length > 1 && path.indexOf(href) === 0)) {
      link.classList.add('active');
    }
  }

  var bucketSelect = document.querySelector('[data-aws-bucket-select]');
  var prefixSelect = document.querySelector('[data-aws-prefix-select]');
  if (bucketSelect && prefixSelect) {
    var endpoint = bucketSelect.getAttribute('data-prefix-endpoint') || '';
    var expectedAccountId = bucketSelect.getAttribute('data-expected-account-id') || '';
    var configurationId = bucketSelect.getAttribute('data-configuration-id') || '';
    var regionTargetSelector = bucketSelect.getAttribute('data-region-target') || '';
    var regionTarget = regionTargetSelector ? document.querySelector(regionTargetSelector) : null;

    function updatePrefixOptions() {
      var bucketName = bucketSelect.value || '';
      var currentPrefix = prefixSelect.value || prefixSelect.getAttribute('data-current-prefix') || '';
      if (!endpoint || !bucketName) return;

      var separator = endpoint.indexOf('?') >= 0 ? '&' : '?';
      var requestUrl = endpoint + separator +
        'configurationId=' + encodeURIComponent(configurationId) +
        '&expectedAccountId=' + encodeURIComponent(expectedAccountId) +
        '&bucketName=' + encodeURIComponent(bucketName);
      var request = new XMLHttpRequest();
      request.open('GET', requestUrl, true);
      request.setRequestHeader('X-Requested-With', 'XMLHttpRequest');
      request.onreadystatechange = function () {
        if (request.readyState !== 4 || request.status < 200 || request.status >= 300) return;

        var payload;
        try {
          payload = JSON.parse(request.responseText);
        } catch (ignore) {
          return;
        }
        if (!payload || !Array.isArray(payload.prefixes)) return;

        prefixSelect.innerHTML = '';
        var placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'Selecione um prefixo';
        prefixSelect.appendChild(placeholder);

        var seen = {};
        for (var prefixIndex = 0; prefixIndex < payload.prefixes.length; prefixIndex += 1) {
          var prefix = payload.prefixes[prefixIndex];
          if (!prefix || seen[prefix]) continue;
          var option = document.createElement('option');
          option.value = prefix;
          option.textContent = prefix;
          if (prefix === currentPrefix) option.selected = true;
          prefixSelect.appendChild(option);
          seen[prefix] = true;
        }

        if (currentPrefix && !seen[currentPrefix]) {
          var custom = document.createElement('option');
          custom.value = currentPrefix;
          custom.textContent = currentPrefix;
          custom.selected = true;
          prefixSelect.appendChild(custom);
        }

        if (regionTarget && typeof payload.bucketRegion === 'string' && payload.bucketRegion) {
          regionTarget.value = payload.bucketRegion;
        }
      };
      request.send(null);
    }

    bucketSelect.addEventListener('change', function () {
      prefixSelect.setAttribute('data-current-prefix', '');
      updatePrefixOptions();
    });
  }

  var autoRefreshHost = document.querySelector('[data-auto-refresh-seconds][data-auto-refresh-active="true"]');
  if (autoRefreshHost) {
    var seconds = parseInt(autoRefreshHost.getAttribute('data-auto-refresh-seconds') || '0', 10);
    if (isFinite(seconds) && seconds >= 5) {
      window.setTimeout(function () { window.location.reload(); }, seconds * 1000);
    }
  }

  var confirmationForms = document.querySelectorAll('form[data-confirm]');
  for (var formIndex = 0; formIndex < confirmationForms.length; formIndex += 1) {
    confirmationForms[formIndex].addEventListener('submit', function (event) {
      var message = this.getAttribute('data-confirm') || 'Confirma esta operacao?';
      if (!window.confirm(message)) event.preventDefault();
    });
  }
}());
