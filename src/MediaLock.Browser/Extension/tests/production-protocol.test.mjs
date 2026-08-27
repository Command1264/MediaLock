import test from 'node:test';
import assert from 'node:assert/strict';
import {
  createInboundCommandGuard,
  deriveConnectionId,
  validateHelloAck,
  validateHostHello,
} from '../production-protocol.mjs';

const expected = Object.freeze({
  extensionId: 'kggfkkiifnclhhmibdglkbdfbacakemn',
  hostNonce: '11111111-1111-4111-8111-111111111111',
  extensionNonce: '22222222-2222-4222-8222-222222222222',
  browserFamily: 'brave',
  profileId: '33333333-3333-4333-8333-333333333333',
  capabilities: ['pause', 'play', 'seek'],
});

test('negotiates one exact profile-qualified v2 connection', async () => {
  const hello = validateHostHello({
    protocolVersion: 2,
    type: 'hostHello',
    hostNonce: expected.hostNonce,
    capabilities: expected.capabilities,
  });
  const connectionId = await deriveConnectionId(expected);
  const ack = await validateHelloAck({
    protocolVersion: 2,
    type: 'helloAck',
    hostNonce: expected.hostNonce,
    extensionNonce: expected.extensionNonce,
    connectionId,
    browserFamily: expected.browserFamily,
    profileId: expected.profileId,
    capabilities: expected.capabilities,
  }, expected);

  assert.equal(hello.hostNonce, expected.hostNonce);
  assert.equal(ack.connectionId, connectionId);
});

test('accepts one exact Page Binding command and rejects its replay', async () => {
  const connectionId = await deriveConnectionId(expected);
  const guard = createInboundCommandGuard();
  const command = {
    protocolVersion: 2,
    type: 'command',
    connectionId,
    sequence: 1,
    requestId: '44444444-4444-4444-8444-444444444444',
    target: {
      bindingId: 'page-binding',
      endpointId: 'media-0',
      scope: 'temporary',
      tabId: 7,
      frameId: 0,
      documentId: 'document-1',
      pageOrigin: 'https://example.com',
    },
    command: { name: 'pause' },
  };

  assert.equal(
    guard.validate(command, connectionId, expected.capabilities).target.bindingId,
    'page-binding',
  );
  assert.throws(
    () => guard.validate(command, connectionId, expected.capabilities),
    /stale|replay/i,
  );
});

test('rejects nested-frame authority and unknown fields', async () => {
  const connectionId = await deriveConnectionId(expected);
  const guard = createInboundCommandGuard();
  const command = {
    protocolVersion: 2,
    type: 'command',
    connectionId,
    sequence: 1,
    requestId: '55555555-5555-4555-8555-555555555555',
    target: {
      bindingId: 'page-binding',
      endpointId: 'media-0',
      scope: 'temporary',
      tabId: 7,
      frameId: 1,
      documentId: 'document-1',
      pageOrigin: 'https://example.com',
    },
    command: { name: 'pause' },
    arbitraryJavaScript: 'never',
  };

  assert.throws(
    () => guard.validate(command, connectionId, expected.capabilities),
    /unknown fields/i,
  );
  delete command.arbitraryJavaScript;
  assert.throws(
    () => guard.validate(command, connectionId, expected.capabilities),
    /authority/i,
  );
});
