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
