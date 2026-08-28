import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const extensionRoot = new URL('../', import.meta.url);

test('Popup actions use one spaced vertical action group', async () => {
  const [html, css] = await Promise.all([
    readFile(new URL('popup.html', extensionRoot), 'utf8'),
    readFile(new URL('popup.css', extensionRoot), 'utf8'),
  ]);

  assert.match(html, /<div class="actions">[\s\S]*authorize-temporary[\s\S]*authorize-site[\s\S]*<\/div>/);
  assert.match(css, /\.actions\s*\{[^}]*flex-direction:\s*column;[^}]*gap:\s*(?:10|12)px;/s);
});

test('opening the Popup reports an active Page Binding instead of Ready', async () => {
  const elements = createPopupElements();
  const messages = [];
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async sendMessage(tabId, message, options) {
        messages.push({ tabId, message, options });
        return { authorized: true, scope: 'temporary' };
      },
    },
    permissions: {
      async contains() {
        return true;
      },
      async request() {
        return true;
      },
    },
    runtime: {
      async sendMessage(message) {
        throw new Error(`Unexpected message: ${message?.type}`);
      },
    },
  };

  try {
    await import(`../popup.js?test=${Date.now()}`);
    await settleAsyncWork();

    assert.deepEqual(messages, [{
      tabId: 42,
      message: { type: 'getGenericEndpointStatus' },
      options: { frameId: 0 },
    }]);
    assert.equal(
      elements.status.textContent,
      'Authorized. Select this page in Media Lock to create Session Lock.',
    );
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

test('an exact-site permission without a current Page Binding reports trusted-site waiting', async () => {
  const elements = createPopupElements();
  const messages = [];
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async sendMessage(tabId, message, options) {
        messages.push({ tabId, message, options });
        throw new Error('No Page Binding is installed in this document.');
      },
    },
    permissions: {
      async contains() {
        return true;
      },
      async request() {
        return true;
      },
    },
    runtime: {
      async sendMessage(message) {
        throw new Error(`Unexpected message: ${message?.type}`);
      },
    },
  };

  try {
    await import(`../popup.js?test=unbound-${Date.now()}`);
    await settleAsyncWork();

    assert.deepEqual(messages, [{
      tabId: 42,
      message: { type: 'getGenericEndpointStatus' },
      options: { frameId: 0 },
    }]);
    assert.equal(
      elements.status.textContent,
      'This site is allowed. Waiting for one unambiguous media element.',
    );
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

test('a page without a Binding or exact-site permission reports Not authorized', async () => {
  const elements = createPopupElements();
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async sendMessage() {
        throw new Error('No Page Binding is installed in this document.');
      },
    },
    permissions: {
      async contains() {
        return false;
      },
      async request() {
        return true;
      },
    },
    runtime: {
      async sendMessage(message) {
        throw new Error(`Unexpected message: ${message?.type}`);
      },
    },
  };

  try {
    await import(`../popup.js?test=no-permission-${Date.now()}`);
    await settleAsyncWork();

    assert.equal(
      elements.status.textContent,
      'Not authorized. Authorize this page to create Session Lock.',
    );
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

test('Popup localizes internal authorization errors and shows a stable support code', async () => {
  const elements = createPopupElements();
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    tabs: {
      async query() {
        return [{ id: 42, url: 'http://media.example.test/watch' }];
      },
    },
    permissions: {
      async contains() {
        return false;
      },
      async request() {
        return false;
      },
    },
    runtime: {
      async sendMessage() {
        throw new Error('Unexpected Extension message.');
      },
    },
  };

  try {
    await import(`../popup.js?test=localized-error-${Date.now()}`);
    await settleAsyncWork();

    assert.equal(
      elements.status.textContent,
      'Unavailable: This page cannot be authorized. Open an HTTPS page with one media element. (ML-BR-001)',
    );
    assert.doesNotMatch(elements.status.textContent, /page-not-eligible/);
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

test('a revoked exact-site permission overrides a stale live document Binding', async () => {
  const elements = createPopupElements();
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async sendMessage() {
        return { authorized: true, scope: 'site' };
      },
    },
    permissions: {
      async contains() {
        return false;
      },
      async request() {
        return true;
      },
    },
    runtime: {
      async sendMessage(message) {
        throw new Error(`Unexpected message: ${message?.type}`);
      },
    },
  };

  try {
    await import(`../popup.js?test=revoked-site-${Date.now()}`);
    await settleAsyncWork();

    assert.equal(
      elements.status.textContent,
      'Not authorized. Authorize this page to create Session Lock.',
    );
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

test('Popup follows the browser Traditional Chinese locale', async () => {
  const elements = createPopupElements();
  const translations = {
    actionTitle: 'Media Lock 瀏覽器整合',
    popupTitle: 'Media Lock',
    popupDescription: '繁體中文說明',
    authorizeTemporary: '僅授權這個頁面一次',
    authorizeSite: '永遠允許這個網站',
    statusChecking: '正在檢查目前頁面…',
    statusAuthorized: '已授權。請在 Media Lock 中選取此頁面以建立工作階段鎖定。',
  };
  globalThis.document = createDocument(elements);
  globalThis.chrome = {
    i18n: {
      getUILanguage: () => 'zh-TW',
      getMessage: (key) => translations[key] ?? '',
    },
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async sendMessage() {
        return { authorized: true, scope: 'site' };
      },
    },
    permissions: {
      async contains() {
        return true;
      },
      async request() {
        return true;
      },
    },
    runtime: {
      async sendMessage(message) {
        throw new Error(`Unexpected message: ${message?.type}`);
      },
    },
  };

  try {
    await import(`../popup.js?test=zh-tw-${Date.now()}`);
    await settleAsyncWork();

    assert.equal(document.documentElement.lang, 'zh-TW');
    assert.equal(elements.description.textContent, '繁體中文說明');
    assert.equal(elements.temporary.textContent, '僅授權這個頁面一次');
    assert.equal(elements.site.textContent, '永遠允許這個網站');
    assert.equal(
      elements.status.textContent,
      '已授權。請在 Media Lock 中選取此頁面以建立工作階段鎖定。',
    );
  } finally {
    delete globalThis.chrome;
    delete globalThis.document;
  }
});

function createPopupElements() {
  return {
    title: createElement('Media Lock Browser Integration', 'actionTitle'),
    heading: createElement('Media Lock', 'popupTitle'),
    description: createElement('Description', 'popupDescription'),
    status: createElement('Ready.', 'statusChecking'),
    temporary: createElement('Authorize this page once', 'authorizeTemporary'),
    site: createElement('Always allow this exact site', 'authorizeSite'),
  };
}

function createElement(textContent = '', i18n) {
  return {
    textContent,
    dataset: i18n ? { i18n } : {},
    disabled: false,
    listeners: new Map(),
    addEventListener(type, listener) {
      this.listeners.set(type, listener);
    },
  };
}

function createDocument(elements) {
  const localizable = [
    elements.title,
    elements.heading,
    elements.description,
    elements.temporary,
    elements.site,
    elements.status,
  ];
  return {
    documentElement: { lang: 'en' },
    querySelector(selector) {
      return {
        title: elements.title,
        h1: elements.heading,
        '#description': elements.description,
        '#status': elements.status,
        '#authorize-temporary': elements.temporary,
        '#authorize-site': elements.site,
      }[selector];
    },
    querySelectorAll(selector) {
      if (selector === 'button') {
        return [elements.temporary, elements.site];
      }
      return selector === '[data-i18n]' ? localizable : [];
    },
  };
}

async function settleAsyncWork() {
  await new Promise((resolve) => setImmediate(resolve));
}
