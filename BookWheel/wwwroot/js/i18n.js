(function () {
  const LOCALE_STORAGE_KEY = 'bookwheel-locale';
  const FALLBACK_LOCALE = 'en';

  // Carries this script's own `?v=<asset-version>` query string so the
  // locale catalogs it fetches are cache-busted/cached the same way the
  // server already treats every other static asset (see Program.cs's
  // immutable Cache-Control comment).
  const scriptSrc = document.currentScript ? document.currentScript.src : '';
  const versionQuery = scriptSrc.includes('?') ? scriptSrc.slice(scriptSrc.indexOf('?')) : '';

  function fetchJson(path) {
    return fetch(path + versionQuery).then(response => {
      if (!response.ok) {
        throw new Error(`Failed to load ${path}: ${response.status}`);
      }
      return response.json();
    });
  }

  let SUPPORTED_LOCALES = [FALLBACK_LOCALE];
  let DEFAULT_LOCALE = FALLBACK_LOCALE;
  let currentLocale = FALLBACK_LOCALE;

  const catalogCache = {};
  const catalogPromises = {};

  function loadLocaleCatalog(locale) {
    if (catalogCache[locale]) {
      return Promise.resolve(catalogCache[locale]);
    }
    if (!catalogPromises[locale]) {
      catalogPromises[locale] = fetchJson(`i18n/${locale}.json`).then(catalog => {
        catalogCache[locale] = catalog;
        return catalog;
      });
    }
    return catalogPromises[locale];
  }

  function detectInitialLocale() {
    const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
    if (SUPPORTED_LOCALES.includes(stored)) {
      return stored;
    }

    const browserLocales = navigator.languages && navigator.languages.length
      ? navigator.languages
      : [navigator.language];

    for (const browserLocale of browserLocales) {
      const shortCode = (browserLocale || '').slice(0, 2).toLowerCase();
      if (SUPPORTED_LOCALES.includes(shortCode)) {
        return shortCode;
      }
    }

    return DEFAULT_LOCALE;
  }

  function resolveCatalogValue(locale, key) {
    const segments = key.split('.');
    let node = catalogCache[locale];
    for (const segment of segments) {
      if (!node || typeof node !== 'object') {
        return undefined;
      }
      node = node[segment];
    }
    return typeof node === 'string' ? node : undefined;
  }

  function interpolate(template, params) {
    if (!params) {
      return template;
    }
    return template.replace(/\{(\w+)\}/g, (match, token) =>
      Object.prototype.hasOwnProperty.call(params, token) ? String(params[token]) : match
    );
  }

  const _pluralRulesCache = {};
  function _getPluralRules(locale) {
    if (!_pluralRulesCache[locale]) {
      _pluralRulesCache[locale] = new Intl.PluralRules(locale);
    }
    return _pluralRulesCache[locale];
  }

  function t(key, params) {
    let resolvedKey = key;
    if (params && typeof params.count === 'number') {
      const category = _getPluralRules(currentLocale).select(params.count);
      const suffix = category.charAt(0).toUpperCase() + category.slice(1);
      const candidate = key + suffix;
      if (resolveCatalogValue(currentLocale, candidate) !== undefined
          || resolveCatalogValue(DEFAULT_LOCALE, candidate) !== undefined) {
        resolvedKey = candidate;
      }
    }
    const value = resolveCatalogValue(currentLocale, resolvedKey)
      ?? resolveCatalogValue(DEFAULT_LOCALE, resolvedKey)
      ?? key;
    return interpolate(value, params);
  }

  function applyStaticTranslations(root) {
    const scope = root || document;

    scope.querySelectorAll('[data-i18n]').forEach(element => {
      element.textContent = t(element.getAttribute('data-i18n'));
    });
    scope.querySelectorAll('[data-i18n-placeholder]').forEach(element => {
      element.setAttribute('placeholder', t(element.getAttribute('data-i18n-placeholder')));
    });
    scope.querySelectorAll('[data-i18n-aria-label]').forEach(element => {
      element.setAttribute('aria-label', t(element.getAttribute('data-i18n-aria-label')));
    });
    scope.querySelectorAll('[data-i18n-title]').forEach(element => {
      element.setAttribute('title', t(element.getAttribute('data-i18n-title')));
    });
  }

  function getCurrentLocale() {
    return currentLocale;
  }

  function warmDefaultLocaleFallback() {
    // t() falls back to DEFAULT_LOCALE for a key missing from the active
    // catalog (e.g. a string added to en but not yet translated). Keep that
    // fallback catalog warm in the background for non-default locales so the
    // fallback actually has something to resolve against.
    if (currentLocale !== DEFAULT_LOCALE) {
      loadLocaleCatalog(DEFAULT_LOCALE).catch(() => {});
    }
  }

  function setLocale(locale) {
    if (!SUPPORTED_LOCALES.includes(locale)) {
      return Promise.resolve();
    }

    return loadLocaleCatalog(locale).then(() => {
      currentLocale = locale;
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      document.documentElement.setAttribute('lang', locale);
      applyStaticTranslations(document);
      window.dispatchEvent(new CustomEvent('bookwheel:locale-changed', { detail: { locale } }));
      warmDefaultLocaleFallback();
    });
  }

  const ready = fetchJson('i18n/locales.json')
    .then(locales => {
      if (Array.isArray(locales) && locales.length > 0) {
        SUPPORTED_LOCALES = locales;
        DEFAULT_LOCALE = locales[0];
      }
      currentLocale = detectInitialLocale();
      return loadLocaleCatalog(currentLocale);
    })
    .then(() => {
      document.documentElement.setAttribute('lang', currentLocale);
      applyStaticTranslations(document);
      warmDefaultLocaleFallback();
    })
    .catch(error => {
      console.error('BookWheel i18n failed to load; falling back to raw keys.', error);
    });

  window.BookWheelI18n = {
    t,
    setLocale,
    getCurrentLocale,
    applyStaticTranslations,
    get SUPPORTED_LOCALES() {
      return SUPPORTED_LOCALES;
    },
    ready
  };

  // app.js calls into BookWheelI18n synchronously from its very first line,
  // so it must not run until the initial locale's catalog has loaded —
  // loading it as a static <script> tag could only guarantee *document*
  // order, not that the async catalog fetch above had actually finished.
  ready.then(() => {
    const appScript = document.createElement('script');
    appScript.src = `js/app.js${versionQuery}`;
    document.body.appendChild(appScript);
  });
})();
