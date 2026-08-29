import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createGrantedSiteBindingHandler,
  createTrustedSiteReconciler,
} from '../site-permission-binding.mjs';

test('a first exact-site grant binds matching completed tabs without a second popup click', async () => {
  const queries = [];
  const bound = [];
  const handleGrant = createGrantedSiteBindingHandler({
    tabs: {
      async query(query) {
        queries.push(query);
        return [
          { id: 42, status: 'complete', url: 'https://media.example.test/watch' },
          { id: 43, status: 'loading', url: 'https://media.example.test/loading' },
          { id: 44, status: 'complete', url: 'https://other.example.test/watch' },
        ];
      },
    },
    async bindCompletedTab(tab) {
      bound.push(tab.id);
      return { accepted: true };
    },
  });

  await handleGrant({ origins: ['https://media.example.test/*'] });

  assert.deepEqual(queries, [{}]);
  assert.deepEqual(bound, [42]);
});

test('broad malformed or unrelated permission additions cannot bind a tab', async () => {
  let queryCount = 0;
  let bindCount = 0;
  const handleGrant = createGrantedSiteBindingHandler({
    tabs: {
      async query() {
        queryCount += 1;
        return [{ id: 42, status: 'complete', url: 'https://media.example.test/watch' }];
      },
    },
    async bindCompletedTab() {
      bindCount += 1;
    },
  });

  await handleGrant({ origins: [
    'https://*.example.test/*',
    'http://media.example.test/*',
    '<all_urls>',
    'not-a-pattern',
  ] });

  assert.equal(queryCount, 0);
  assert.equal(bindCount, 0);
});

test('multiple newly granted origins bind each matching completed tab at most once', async () => {
  const bound = [];
  const handleGrant = createGrantedSiteBindingHandler({
    tabs: {
      async query() {
        return [
          { id: 42, status: 'complete', url: 'https://first.example.test/watch' },
          { id: 43, status: 'complete', url: 'https://second.example.test/watch' },
        ];
      },
    },
    async bindCompletedTab(tab) {
      bound.push(tab.id);
    },
  });

  await handleGrant({ origins: [
    'https://first.example.test/*',
    'https://first.example.test/*',
    'https://second.example.test/*',
  ] });

  assert.deepEqual(bound, [42, 43]);
});

test('trusted-site reconciliation binds only completed exact HTTPS tabs with retained permission', async () => {
  const checked = [];
  const bound = [];
  const reconcile = createTrustedSiteReconciler({
    tabs: {
      async query(query) {
        assert.deepEqual(query, {});
        return [
          { id: 41, status: 'complete', url: 'https://allowed.example.test/watch' },
          { id: 42, status: 'loading', url: 'https://allowed.example.test/loading' },
          { id: 43, status: 'complete', url: 'http://allowed.example.test/insecure' },
          { id: 44, status: 'complete', url: 'https://denied.example.test/watch' },
          { id: 45, status: 'complete', url: 'not-a-url' },
        ];
      },
    },
    async hasSitePermission(origin) {
      checked.push(origin);
      return origin === 'https://allowed.example.test';
    },
    async bindCompletedTab(tab) {
      bound.push(tab.id);
      return { accepted: true };
    },
  });

  const result = await reconcile();

  assert.deepEqual(checked, [
    'https://allowed.example.test',
    'https://denied.example.test',
  ]);
  assert.deepEqual(bound, [41]);
  assert.equal(result.eligibleCount, 1);
});
