import test from 'node:test';
import assert from 'node:assert/strict';

import { dispatchBoundCommand } from '../browser-dispatch.mjs';

const request = Object.freeze({
  target: Object.freeze({
    tabId: 42,
    frameId: 0,
    documentId: '33333333-3333-4333-8333-333333333333',
    pageOrigin: 'https://music.youtube.com',
  }),
  command: Object.freeze({ name: 'pause' }),
});

test('fails closed when the bound tab closes before exact-document dispatch', async () => {
  const calls = [];
  const result = await dispatchBoundCommand({
    tabs: {
      async sendMessage(...args) {
        calls.push(args);
        throw new Error('No tab with id: 42.');
      },
    },
    documentRegistry: { matches: () => true },
    request,
  });

  assert.deepEqual(result, { accepted: false, errorCode: 'target-unavailable' });
  assert.deepEqual(calls, [[42, request, { documentId: request.target.documentId }]]);
});
