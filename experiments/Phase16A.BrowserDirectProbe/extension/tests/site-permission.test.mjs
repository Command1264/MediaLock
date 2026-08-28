import test from 'node:test';
import assert from 'node:assert/strict';

import {
  prepareExactActiveSitePermission,
  requestPreparedSitePermission,
} from '../site-permission.mjs';

test('one popup gesture requests only the exact active HTTPS origin', async () => {
  const requests = [];
  const prepared = await prepareExactActiveSitePermission({
    async query() {
      return [{ id: 42, url: 'https://media.example.test/watch?private=token' }];
    },
  });
  const result = await requestPreparedSitePermission({
    prepared,
    requestPermission: async (request) => {
      requests.push(request);
      return true;
    },
  });

  assert.deepEqual(result, { accepted: true });
  assert.deepEqual(requests, [{ origins: ['https://media.example.test/*'] }]);
});

test('an ineligible active page is rejected before requesting permission', async () => {
  let requestCount = 0;
  const prepared = await prepareExactActiveSitePermission({
    async query() {
      return [{ id: 42, url: 'chrome://settings' }];
    },
  });
  const result = await requestPreparedSitePermission({
    prepared,
    requestPermission: async () => {
      requestCount += 1;
      return true;
    },
  });

  assert.deepEqual(result, { accepted: false, errorCode: 'page-not-eligible' });
  assert.equal(requestCount, 0);
});
