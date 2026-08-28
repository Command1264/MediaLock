import test from 'node:test';
import assert from 'node:assert/strict';

import { createAuthorizedTargetLifecycle } from '../authorized-target-lifecycle.mjs';

const entry = (bindingId, endpointId = `endpoint-${bindingId}`) => ({
  target: {
    bindingId,
    endpointId,
    scope: 'site',
    tabId: 42,
    frameId: 0,
    documentId: `document-${bindingId}`,
    pageOrigin: 'https://media.example.test',
  },
  presentation: {
    sourceDisplayName: 'Video',
    playbackStatus: 'playing',
    playbackRate: 1,
    capabilities: ['pause', 'play', 'seek'],
    observedAt: '2026-08-27T00:00:00Z',
    timeline: null,
  },
});

test('reauthorizing the same tab removes the old binding before publishing the replacement', async () => {
  const events = [];
  const lifecycle = createAuthorizedTargetLifecycle({
    publishTarget: async (value) => events.push(['snapshot', value.target.bindingId]),
    publishTargetRemoved: (value, reason) => events.push(['removed', value.target.bindingId, reason]),
    clearTab: () => {},
  });

  await lifecycle.replace(entry('binding-one'));
  await lifecycle.replace(entry('binding-two'));

  assert.deepEqual(events, [
    ['snapshot', 'binding-one'],
    ['removed', 'binding-one', 'target-replaced'],
    ['snapshot', 'binding-two'],
  ]);
  assert.deepEqual([...lifecycle.values()].map((value) => value.target.bindingId), ['binding-two']);
});

test('page loading removes a site binding without automatically rebuilding it', async () => {
  const events = [];
  const cleared = [];
  const lifecycle = createAuthorizedTargetLifecycle({
    publishTarget: async (value) => events.push(['snapshot', value.target.bindingId]),
    publishTargetRemoved: (value, reason) => events.push(['removed', value.target.bindingId, reason]),
    clearTab: (tabId) => cleared.push(tabId),
  });
  await lifecycle.replace(entry('binding-site'));

  lifecycle.handleTabUpdated(42, { status: 'loading' });

  assert.deepEqual(events, [
    ['snapshot', 'binding-site'],
    ['removed', 'binding-site', 'document-replaced'],
  ]);
  assert.deepEqual(cleared, [42]);
  assert.deepEqual([...lifecycle.values()], []);
});

test('only an observation for the current exact target can update presentation', async () => {
  const snapshots = [];
  const lifecycle = createAuthorizedTargetLifecycle({
    publishTarget: async (value) => snapshots.push(value),
    publishTargetRemoved: () => {},
    clearTab: () => {},
  });
  const current = entry('binding-current');
  await lifecycle.replace(current);
  const stale = entry('binding-stale');

  assert.equal(await lifecycle.observe(stale.target, {
    ...stale.presentation,
    playbackStatus: 'paused',
  }), false);
  assert.equal(await lifecycle.observe(current.target, {
    ...current.presentation,
    playbackStatus: 'paused',
  }), true);

  assert.equal(snapshots.length, 2);
  assert.equal(snapshots[1].presentation.playbackStatus, 'paused');
});
