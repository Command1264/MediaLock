import test from 'node:test';
import assert from 'node:assert/strict';

import { createAuthorizedPageBindingCoordinator } from '../authorized-page-binding.mjs';

test('a completed trusted-site document automatically creates one new exact target', async () => {
  const events = [];
  const autoBinding = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    async hasSitePermission(origin) {
      events.push(['permission', origin]);
      return true;
    },
    async bindTab(tab) {
      events.push(['bind', tab.id]);
      return { accepted: true, target: { tabId: tab.id } };
    },
    async commitBinding(result) {
      events.push(['commit', result.target.tabId]);
    },
    discardBinding() {},
  });

  await Promise.all([
    autoBinding.handleTabUpdated(42, { status: 'complete' }, {
      id: 42,
      url: 'https://media.example.test/watch',
    }),
    autoBinding.handleTabUpdated(42, { status: 'complete' }, {
      id: 42,
      url: 'https://media.example.test/watch',
    }),
  ]);

  assert.deepEqual(events, [
    ['permission', 'https://media.example.test'],
    ['bind', 42],
    ['permission', 'https://media.example.test'],
    ['commit', 42],
  ]);
});

test('temporary or ineligible pages never auto-bind', async () => {
  const bound = [];
  const autoBinding = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    async hasSitePermission() {
      return false;
    },
    async bindTab(tab) {
      bound.push(tab.id);
      return { accepted: true };
    },
    async commitBinding() {},
    discardBinding() {},
  });

  await autoBinding.handleTabUpdated(42, { status: 'complete' }, {
    id: 42,
    url: 'https://media.example.test/watch',
  });
  await autoBinding.handleTabUpdated(43, { status: 'complete' }, {
    id: 43,
    url: 'http://media.example.test/watch',
  });

  assert.deepEqual(bound, []);
});

test('a newer loading generation cancels an older pending auto-bind', async () => {
  let resolvePermission;
  const permission = new Promise((resolve) => {
    resolvePermission = resolve;
  });
  let bindCount = 0;
  const autoBinding = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    hasSitePermission: () => permission,
    async bindTab() {
      bindCount += 1;
      return { accepted: true };
    },
    async commitBinding() {},
    discardBinding() {},
  });

  const pending = autoBinding.handleTabUpdated(42, { status: 'complete' }, {
    id: 42,
    url: 'https://media.example.test/old-document',
  });
  await new Promise((resolve) => setImmediate(resolve));
  await autoBinding.handleTabUpdated(42, { status: 'loading' }, {
    id: 42,
    url: 'https://media.example.test/new-document',
  });
  resolvePermission(true);
  await pending;

  assert.equal(bindCount, 0);
});

test('loading invalidation discards a binding that finishes after its document was replaced', async () => {
  let resolveBinding;
  const binding = new Promise((resolve) => {
    resolveBinding = resolve;
  });
  const committed = [];
  const discarded = [];
  const autoBinding = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    async hasSitePermission() {
      return true;
    },
    bindTab: () => binding,
    async commitBinding(result) {
      committed.push(result.target.bindingId);
    },
    discardBinding(target) {
      discarded.push(target.bindingId);
    },
  });

  const pending = autoBinding.handleTabUpdated(42, { status: 'complete' }, {
    id: 42,
    url: 'https://media.example.test/old-document',
  });
  await new Promise((resolve) => setImmediate(resolve));
  await autoBinding.handleTabUpdated(42, { status: 'loading' }, {
    id: 42,
    url: 'https://media.example.test/new-document',
  });
  resolveBinding({
    accepted: true,
    target: { tabId: 42, bindingId: 'stale-binding' },
  });

  assert.deepEqual(await pending, {
    accepted: false,
    errorCode: 'document-replaced',
  });
  assert.deepEqual(committed, []);
  assert.deepEqual(discarded, ['stale-binding']);
});

test('permission loss during binding discards the uncommitted trusted-site target', async () => {
  let resolveBinding;
  const binding = new Promise((resolve) => {
    resolveBinding = resolve;
  });
  let permissionGranted = true;
  const committed = [];
  const discarded = [];
  const autoBinding = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    async hasSitePermission() {
      return permissionGranted;
    },
    bindTab: () => binding,
    async commitBinding(result) {
      committed.push(result.target.bindingId);
    },
    discardBinding(target) {
      discarded.push(target.bindingId);
    },
  });

  const pending = autoBinding.handleTabUpdated(42, { status: 'complete' }, {
    id: 42,
    url: 'https://media.example.test/watch',
  });
  await new Promise((resolve) => setImmediate(resolve));
  permissionGranted = false;
  resolveBinding({
    accepted: true,
    target: { tabId: 42, bindingId: 'revoked-binding' },
  });

  assert.deepEqual(await pending, {
    accepted: false,
    errorCode: 'permission-denied',
  });
  assert.deepEqual(committed, []);
  assert.deepEqual(discarded, ['revoked-binding']);
});

test('page loading during explicit authorization discards the uncommitted target', async () => {
  let resolveBinding;
  const binding = new Promise((resolve) => {
    resolveBinding = resolve;
  });
  const committed = [];
  const discarded = [];
  const coordinator = createAuthorizedPageBindingCoordinator({
    hasTarget: () => false,
    async hasSitePermission() {
      return true;
    },
    bindTab: () => binding,
    async commitBinding(result) {
      committed.push(result.target.bindingId);
    },
    discardBinding(target) {
      discarded.push(target.bindingId);
    },
  });

  const pending = coordinator.authorizeTab({
    scope: 'temporary',
    tab: { id: 42, url: 'https://media.example.test/watch' },
  });
  await new Promise((resolve) => setImmediate(resolve));
  coordinator.invalidate(42);
  resolveBinding({
    accepted: true,
    target: { tabId: 42, bindingId: 'stale-explicit-binding' },
  });

  assert.deepEqual(await pending, {
    accepted: false,
    errorCode: 'document-replaced',
  });
  assert.deepEqual(committed, []);
  assert.deepEqual(discarded, ['stale-explicit-binding']);
});

test('a late stale bind cannot overwrite its newer document successor', async () => {
  let resolveStaleBinding;
  const staleBinding = new Promise((resolve) => {
    resolveStaleBinding = resolve;
  });
  let bindCount = 0;
  let registeredTarget;
  const committed = [];
  const discarded = [];
  const coordinator = createAuthorizedPageBindingCoordinator({
    hasTarget: () => committed.length > 0,
    async hasSitePermission() {
      return true;
    },
    async bindTab(tab) {
      bindCount += 1;
      const result = bindCount === 1
        ? await staleBinding
        : {
          accepted: true,
          target: { tabId: tab.id, bindingId: 'successor-binding' },
        };
      registeredTarget = result.target;
      return result;
    },
    async commitBinding(result) {
      committed.push(result.target.bindingId);
    },
    discardBinding(target) {
      discarded.push(target.bindingId);
      if (registeredTarget?.bindingId === target.bindingId) {
        registeredTarget = undefined;
      }
    },
  });

  const stale = coordinator.authorizeTab({
    scope: 'temporary',
    tab: { id: 42, url: 'https://media.example.test/old-document' },
  });
  await new Promise((resolve) => setImmediate(resolve));
  await coordinator.handleTabUpdated(42, { status: 'loading' }, {
    id: 42,
    url: 'https://media.example.test/new-document',
  });
  const successor = coordinator.handleTabUpdated(42, { status: 'complete' }, {
    id: 42,
    url: 'https://media.example.test/new-document',
  });
  await new Promise((resolve) => setImmediate(resolve));
  resolveStaleBinding({
    accepted: true,
    target: { tabId: 42, bindingId: 'stale-binding' },
  });

  assert.deepEqual(await stale, {
    accepted: false,
    errorCode: 'document-replaced',
  });
  assert.deepEqual(await successor, { accepted: true });
  assert.equal(registeredTarget?.bindingId, 'successor-binding');
  assert.deepEqual(committed, ['successor-binding']);
  assert.deepEqual(discarded, ['stale-binding']);
});
