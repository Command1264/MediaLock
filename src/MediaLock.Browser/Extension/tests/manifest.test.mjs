import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const extensionRoot = new URL('../', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('manifest.json', extensionRoot), 'utf8'));
const nativeHostManifest = JSON.parse(
  await readFile(new URL('../native-host-manifest.template.json', extensionRoot), 'utf8'),
);

function deriveExtensionId(manifestKey) {
  const hash = createHash('sha256').update(Buffer.from(manifestKey, 'base64')).digest();
  const alphabet = 'abcdefghijklmnop';
  return [...hash.subarray(0, 16)]
    .map((byte) => `${alphabet[byte >> 4]}${alphabet[byte & 0x0f]}`)
    .join('');
}

test('pins one stable candidate Extension ID in both manifests', () => {
  const extensionId = deriveExtensionId(manifest.key);

  assert.equal(extensionId, 'kggfkkiifnclhhmibdglkbdfbacakemn');
  assert.deepEqual(nativeHostManifest.allowed_origins, [`chrome-extension://${extensionId}/`]);
});

test('requests only the production-candidate user-authorized least-privilege surface', () => {
  assert.deepEqual(
    [...manifest.permissions].sort(),
    ['activeTab', 'nativeMessaging', 'scripting', 'storage', 'tabs'],
  );
  assert.equal(manifest.permissions.includes('clipboardRead'), false);
  assert.equal(manifest.permissions.includes('<all_urls>'), false);
  assert.equal(Object.hasOwn(manifest, 'host_permissions'), false);
  assert.deepEqual(manifest.optional_host_permissions, ['https://*/*']);
  assert.equal(manifest.optional_host_permissions.includes('<all_urls>'), false);
  assert.equal(Object.hasOwn(manifest, 'externally_connectable'), false);
  assert.equal(Object.hasOwn(manifest, 'content_scripts'), false);
  assert.equal(manifest.background.type, 'module');
  assert.equal(manifest.background.service_worker, 'production-service-worker.mjs');
});

test('localizes the Extension through the browser locale with an English fallback', async () => {
  assert.equal(manifest.default_locale, 'en');
  assert.equal(manifest.name, '__MSG_extensionName__');
  assert.equal(manifest.description, '__MSG_extensionDescription__');

  const english = JSON.parse(await readFile(
    new URL('_locales/en/messages.json', extensionRoot),
    'utf8',
  ));
  const traditionalChinese = JSON.parse(await readFile(
    new URL('_locales/zh_TW/messages.json', extensionRoot),
    'utf8',
  ));
  assert.equal(english.authorizeSite.message, 'Always allow this exact site');
  assert.equal(traditionalChinese.authorizeSite.message, '永遠允許這個網站');
  assert.deepEqual(Object.keys(traditionalChinese).sort(), Object.keys(english).sort());
});

test('localized Browser Integration failures keep unique stable support codes', async () => {
  const locales = await Promise.all(['en', 'zh_TW'].map(async (locale) => JSON.parse(
    await readFile(new URL(`_locales/${locale}/messages.json`, extensionRoot), 'utf8'),
  )));
  const errorKeys = Object.keys(locales[0]).filter((key) => key.startsWith('error'));
  const expectedCodes = errorKeys.map((key) => {
    const match = locales[0][key].message.match(/ML-BR-\d{3}/);
    assert.ok(match, `${key} must expose one stable support code.`);
    return match[0];
  });

  assert.equal(new Set(expectedCodes).size, errorKeys.length);
  for (const locale of locales) {
    assert.deepEqual(
      errorKeys.map((key) => locale[key].message.match(/ML-BR-\d{3}/)?.[0]),
      expectedCodes,
    );
  }
});
