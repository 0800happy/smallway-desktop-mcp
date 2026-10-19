// Project Connections V1.0 (2026-10-20): synthetic test credentials only.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {
  parseCredential, redacted, scanProjectMarkdown, getApprovedConnections,
  loadApprovedCredential, safeConnectionSummary
} from '../src/project-connections.js';
import { DesktopPolicy } from '../src/desktop-policy.js';

const base = { section: 'Cloudways', kind: 'ssh-password' as const,
  host: 'server.example.test', username: 'demo', secret_label: '' };

test('Chinese/English Markdown tables and password rotation stay local', () => {
  const md1 = [
    '## Cloudways', '| SSH Host | server.example.test |',
    '| SSH Username | demo |', '| SSH Password | fake-one-ONLY-FOR-TEST |'
  ].join('\n');
  assert.equal(parseCredential(md1, base).secret, 'fake-one-ONLY-FOR-TEST');
  const md2 = md1.replace('fake-one-ONLY-FOR-TEST', 'fake-two-ONLY-FOR-TEST');
  assert.equal(parseCredential(md2, base).secret, 'fake-two-ONLY-FOR-TEST');
  assert.throws(() => parseCredential(md1.replace('server.example.test', 'evil.example.test'), base),
    /host no longer matches/);
  assert.throws(() => parseCredential(md1.replace('demo |', 'other |'), base),
    /account no longer matches/);
});

test('Multiple matching secret fields are not guessed; section pinning isolates accounts', () => {
  const md = [
    '## Account A',
    'Host: server.example.test', 'Username: demo',
    'Password: fake-a', 'Password: fake-b',
    '## Account B', 'Host: server.example.test', 'Username: demo',
    'Password: fake-credential-b'
  ].join('\n');
  assert.throws(() => parseCredential(md, { ...base, section: 'Account A' }), /ambiguous/);
  assert.equal(parseCredential(md, { ...base, section: 'Account B' }).secret, 'fake-credential-b');
  assert.throws(() => parseCredential(md, { ...base, section: 'Not Present' }), /section is missing/);
});

test('Cloudflare token and WordPress application-password are label based', () => {
  assert.equal(parseCredential('## Cloudflare\nCloudflare API Token: fake-token-ONLY-FOR-TEST',
    { ...base, kind: 'cloudflare-token', section: 'Cloudflare',
      host: 'api.cloudflare.com', username: '' }).secret, 'fake-token-ONLY-FOR-TEST');
  const wordpress = '## WordPress\nSite URL: https://blog.example.test\nUsername: editor\nApplication Password: fake-wp-ONLY-FOR-TEST';
  assert.equal(parseCredential(wordpress, { ...base, kind: 'wordpress-app-password',
    section: 'WordPress', host: 'blog.example.test', username: 'editor' }).secret,
  'fake-wp-ONLY-FOR-TEST');
});

test('Tool response redaction covers plaintext and authorization headers', () => {
  const text = 'response fake-token-ONLY-FOR-TEST Authorization: Bearer bbbbbbb\nPassword: visible';
  const safe = redacted(text, ['fake-token-ONLY-FOR-TEST']);
  assert.doesNotMatch(safe, /fake-token|bbbbbbb|visible/);
  assert.match(safe, /\[REDACTED\]/);
});

test('Scan is confined to a project folder; approval registry keeps no plaintext secrets', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'dmc-project-test-'));
  const prior = process.env.LOCALAPPDATA;
  try {
    process.env.LOCALAPPDATA = root;
    const project = path.join(root, 'my-project');
    const excluded = path.join(project, '.git');
    await mkdir(excluded, { recursive: true });
    const md = path.join(project, 'server.md');
    const mdText = '## Server\nSSH Host: server.example.test\nSSH Username: demo\nSSH Password: fake-test-password';
    await writeFile(md, mdText);
    await writeFile(path.join(excluded, 'secrets.md'), 'Cloudflare API Token: fake-hidden');
    const policy = await DesktopPolicy.create({ profile: 'full-control', allowedRoots: [root] });
    const scan = await scanProjectMarkdown(policy, project);
    assert.equal(scan.candidates.length, 1);
    assert.equal(scan.candidates[0]?.file, 'server.md');
    const scannedText = JSON.stringify(scan);
    assert.doesNotMatch(scannedText, /fake-test-password|fake-hidden/);
    const registryRoot = path.join(root, 'DesktopMCP', 'project-connections');
    await mkdir(registryRoot, { recursive: true });
    const approved = { id: '11111111-1111-4111-8111-111111111111',
      project_path: project, source_file: md, section: 'Server', kind: 'ssh-password',
      host: 'server.example.test', username: 'demo', secret_label: 'SSH Password',
      host_key_sha256: 'SHA256:' + Buffer.alloc(32).toString('base64').replace(/=+$/, ''),
      approved_utc: new Date().toISOString() };
    await writeFile(path.join(registryRoot, 'registry.json'),
      JSON.stringify({ version: 1, connections: [approved] }));
    const entries = await getApprovedConnections(policy);
    assert.equal(entries.length, 1);
    assert.doesNotMatch(JSON.stringify(entries.map(safeConnectionSummary)), /fake-test-password/);
    const loaded = await loadApprovedCredential(policy, approved.id);
    assert.equal(loaded.secret, 'fake-test-password');
    await writeFile(md, mdText.replace('fake-test-password', 'fake-rotated'));
    assert.equal((await loadApprovedCredential(policy, approved.id)).secret, 'fake-rotated');
    await writeFile(md, mdText.replace('server.example.test', 'attacker.example.test'));
    await assert.rejects(loadApprovedCredential(policy, approved.id), /host no longer matches/);
  } finally {
    if (prior === undefined) delete process.env.LOCALAPPDATA;
    else process.env.LOCALAPPDATA = prior;
    await rm(root, { recursive: true, force: true });
  }
});
