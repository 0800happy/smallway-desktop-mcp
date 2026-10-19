// Project Connections V1.0 (2026-10-20).
// Secrets are resolved and used ONLY inside the local Gateway; tool inputs/outputs
// and audit targets contain connection IDs rather than credential material.
import { Client } from 'ssh2';
import { mkdir, readFile, rename, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import type { McpServer } from '@modelcontextprotocol/server';
import * as z from 'zod/v4';
import type { AuditLogger } from './audit.js';
import { DesktopPolicy, PolicyDeniedError } from './desktop-policy.js';
import {
  getApprovedConnections, loadApprovedCredential, localRegistryFile,
  redacted, safeConnectionSummary, scanProjectMarkdown, resolveApprovedIdentityFile,
  type ResolvedConnection
} from './project-connections.js';

const ID = /^[0-9a-f-]{36}$/i;
const READ_SSH = /^(?:pwd|id|whoami|uptime|uname -a|df -h|df -hT|free -m|free -h|systemctl (?:status|is-active) [a-zA-Z0-9_.@-]+)$/;
type Pending = {
  id: string; connection_id: string; type: 'ssh' | 'api'; command: string;
  method: string; api_path: string; body: string; created: string;
  approved: boolean; approved_utc: string;
};
const MAX_OUTPUT = 16384;
const PENDING_LIFETIME_MS = 15 * 60 * 1000;
function result(o: unknown) {
  return { content: [{ type: 'text' as const, text: JSON.stringify(o, null, 2) }] };
}
function failure(e: unknown) {
  // Never return error stacks, request bodies, or exception objects.
  const message = e instanceof PolicyDeniedError ? e.message :
    'Project connection operation failed. Inspect local DeskMCP diagnostic log; no credentials returned.';
  return { isError: true as const, content: [{ type: 'text' as const, text: message }] };
}
function requireFull(policy: DesktopPolicy): void {
  if (policy.profile !== 'full-control' && policy.profile !== 'fully-unlocked') {
    throw new PolicyDeniedError('Project connection operations require session-only Full Control.');
  }
}
function pendingRoot() { return path.join(path.dirname(localRegistryFile()), 'pending'); }
function pendingFile(id: string) {
  if (!ID.test(id)) throw new Error('Invalid request ID.');
  return path.join(pendingRoot(), id + '.json');
}
async function loadPending(id: string): Promise<Pending> {
  const file = pendingFile(id);
  const info = await stat(file);
  if (!info.isFile() || info.size > 20000) throw new Error('Invalid pending operation.');
  const req: Pending = JSON.parse(await readFile(file, 'utf8')) as Pending;
  if (req.id !== id || !ID.test(req.connection_id) ||
      !['ssh','api'].includes(req.type) || typeof req.created !== 'string' ||
      typeof req.approved !== 'boolean') throw new Error('Invalid pending operation.');
  if (!Number.isFinite(Date.parse(req.created)) ||
      Date.now() - Date.parse(req.created) > PENDING_LIFETIME_MS ||
      Date.now() < Date.parse(req.created) - 30000) throw new Error('Pending operation expired.');
  return req;
}
async function requestApproval(connectionId: string, type: 'ssh' | 'api',
  command: string, method: string, apiPath: string, body: string) {
  await mkdir(pendingRoot(), { recursive: true });
  const pending: Pending = {
    id: randomUUID(), connection_id: connectionId, type, command, method,
    api_path: apiPath, body, created: new Date().toISOString(),
    approved: false, approved_utc: ''
  };
  await writeFile(pendingFile(pending.id), JSON.stringify(pending, null, 2), { encoding: 'utf8', flag: 'wx' });
  return { request_id: pending.id, requires_local_approval: true,
    expires_minutes: 15, preview: type === 'ssh'
      ? { type, command }
      : { type, method, path: apiPath, body_bytes: Buffer.byteLength(body) },
    note: 'Open DeskMCP Settings > Project Connections > Pending approvals. Review locally and approve this exact operation, then call execute with request_id.' };
}
function pinnedHostKey(value: string, expected: string): boolean {
  if (!/^[0-9a-f]{64}$/i.test(value)) return false;
  const actual = 'SHA256:' + Buffer.from(value, 'hex').toString('base64').replace(/=+$/,'');
  return actual === expected;
}
async function runSsh(resolved: ResolvedConnection, command: string) {
  if (!resolved.entry.kind.startsWith('ssh-')) throw new Error('Not an SSH connection.');
  if (!command || command.length > 3000 || /\0|\r|\n/.test(command)) throw new Error('SSH command is invalid.');
  const { entry } = resolved;
  const config: Parameters<Client['connect']>[0] = {
    host: entry.host, port: 22, username: entry.username,
    readyTimeout: 15000, keepaliveInterval: 0,
    hostHash: 'sha256', hostVerifier: (hash: string) => pinnedHostKey(hash, entry.host_key_sha256)
  };
  const protectedSecrets = [resolved.secret];
  if (entry.kind === 'ssh-password') config.password = resolved.secret;
  else {
    // Approved Markdown may contain a key filename. Read the key only locally,
    // and never allow a referenced path outside the approved project or Home.
    const keyPath = await resolveApprovedIdentityFile(entry, resolved.secret);
    config.privateKey = await readFile(keyPath);
    // Only unencrypted keys supported via ssh2 in V1.0; never print their contents.
  }
  const client = new Client();
  return await new Promise<{ exit_code: number | null; output: string; stderr: string }>((resolve, reject) => {
    let done = false;
    let timer: NodeJS.Timeout | undefined;
    const finish = (error?: unknown, response?: { exit_code: number | null; output: string; stderr: string }) => {
      if (done) return;
      done = true;
      if (timer) clearTimeout(timer);
      client.end();
      if (error) reject(new Error('SSH connection or remote command failed.'));
      else resolve(response!);
    };
    timer = setTimeout(() => finish(new Error('SSH command timeout')), 35000);
    client.on('error', () => finish(new Error('SSH authentication/host key failed')));
    client.once('ready', () => {
      client.exec(command, (error, stream) => {
        if (error) { finish(error); return; }
        let stdout = ''; let stderr = '';
        stream.on('data', (chunk: Buffer) => { if (stdout.length < MAX_OUTPUT) stdout += chunk.toString('utf8').slice(0, MAX_OUTPUT); });
        stream.stderr.on('data', (chunk: Buffer) => { if (stderr.length < MAX_OUTPUT) stderr += chunk.toString('utf8').slice(0, MAX_OUTPUT); });
        stream.on('close', (code: number | null) => finish(undefined, {
          exit_code: code,
          output: redacted(stdout.slice(0, MAX_OUTPUT), protectedSecrets),
          stderr: redacted(stderr.slice(0, MAX_OUTPUT), protectedSecrets)
        }));
      });
    });
    client.connect(config);
  });
}
async function runApi(resolved: ResolvedConnection, method: string, apiPath: string, body = '') {
  const { entry, secret } = resolved;
  if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(method) ||
      !apiPath || apiPath.length > 2048 || body.length > 16384) throw new Error('Invalid API operation.');
  if (!['cloudflare-token','wordpress-app-password'].includes(entry.kind))
    throw new Error('Not an API connection.');
  const base = entry.kind === 'cloudflare-token'
    ? 'https://api.cloudflare.com'
    : 'https://' + entry.host;
  const target = new URL(apiPath, base);
  const expectedOrigin = new URL(base).origin;
  if (target.origin !== expectedOrigin || target.username || target.password || target.hash) {
    throw new Error('API origin differs from approved host.');
  }
  const prefix = entry.kind === 'cloudflare-token' ? '/client/v4/' : '/wp-json/wp/v2/';
  if (!target.pathname.startsWith(prefix)) throw new Error('API endpoint outside approved service path.');
  if ([...target.searchParams.keys()].some(k => /(?:token|key|password|secret|auth)/i.test(k)))
    throw new Error('Secret-bearing query parameters are not supported.');
  const authorization = entry.kind === 'cloudflare-token'
    ? 'Bearer ' + secret
    : 'Basic ' + Buffer.from(entry.username + ':' + secret).toString('base64');
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), 20000);
  try {
    const response = await fetch(target, {
      method, redirect: 'error', signal: abort.signal,
      headers: { Authorization: authorization, Accept: 'application/json',
        ...(method !== 'GET' ? { 'Content-Type': 'application/json' } : {}) },
      ...(method !== 'GET' && body ? { body } : {})
    });
    const responseText = (await response.text()).slice(0, MAX_OUTPUT);
    return { http_status: response.status, ok: response.ok,
      response: redacted(responseText, [secret, authorization]),
      truncated: responseText.length >= MAX_OUTPUT };
  } finally { clearTimeout(timer); }
}
export function registerProjectConnectionTools(server: McpServer, policy: DesktopPolicy, audit: AuditLogger): void {
  server.registerTool('desktop_project_connections', {
    title: 'Locally Approved Project SSH and API Connections',
    description: 'Scan Markdown candidates in ONE specified project folder without returning secrets, list connections approved using local DeskMCP UI, use pinned SSH/API credentials locally without revealing them, and request per-operation local approval for non-read-only actions. This is for user-authorized maintenance, not a platform-policy bypass. Only explicit local approvals authorize credentials. No automatic Codex schedules.',
    inputSchema: z.object({
      action: z.enum(['scan','list','ssh_read','api_get','request_ssh','request_api','execute']),
      project_path: z.string().min(1).max(4096).optional(),
      connection_id: z.string().regex(ID).optional(),
      command: z.string().max(3000).optional(),
      method: z.enum(['POST','PUT','PATCH','DELETE']).optional(),
      api_path: z.string().max(2048).optional(),
      json_body: z.string().max(16384).optional(),
      request_id: z.string().regex(ID).optional()
    }),
    annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: false, openWorldHint: true }
  }, async input => {
    let op;
    try { op = await audit.begin('desktop_project_connections', 'process', policy.profile, input.connection_id ?? input.action); }
    catch (error) { return failure(error); }
    try {
      if (input.action === 'scan') {
        if (!input.project_path) throw new Error('project_path is required.');
        const details = await scanProjectMarkdown(policy, input.project_path);
        await audit.finish(op, 'allow');
        return result(details);
      }
      if (input.action === 'list') {
        const entries = await getApprovedConnections(policy);
        await audit.finish(op, 'allow');
        return result({ connections: entries.map(safeConnectionSummary) });
      }
      requireFull(policy);
      if (input.action === 'execute') {
        if (!input.request_id) throw new Error('request_id is required.');
        const pending = await loadPending(input.request_id);
        if (!pending.approved || !pending.approved_utc) throw new PolicyDeniedError('Local operation approval is required.');
        // An approved operation is consumed before network I/O to prevent repeats.
        await rename(pendingFile(pending.id), pendingFile(pending.id) + '.consumed');
        const credential = await loadApprovedCredential(policy, pending.connection_id);
        const output = pending.type === 'ssh'
          ? await runSsh(credential, pending.command)
          : await runApi(credential, pending.method, pending.api_path, pending.body);
        await audit.finish(op, 'allow');
        return result({ request_id: pending.id, connection_id: pending.connection_id, result: output });
      }
      if (!input.connection_id) throw new Error('connection_id is required.');
      const credential = await loadApprovedCredential(policy, input.connection_id);
      if (input.action === 'ssh_read') {
        if (!input.command || !READ_SSH.test(input.command)) throw new PolicyDeniedError('Only listed fixed diagnostics are auto-approved. Use request_ssh for any other command.');
        const output = await runSsh(credential, input.command);
        await audit.finish(op, 'allow');
        return result({ connection_id: input.connection_id, result: output });
      }
      if (input.action === 'api_get') {
        if (!input.api_path) throw new Error('api_path is required.');
        const output = await runApi(credential, 'GET', input.api_path);
        await audit.finish(op, 'allow');
        return result({ connection_id: input.connection_id, result: output });
      }
      let output: unknown;
      if (input.action === 'request_ssh') {
        if (!input.command) throw new Error('command is required.');
        if (!credential.entry.kind.startsWith('ssh-')) throw new Error('Connection kind mismatch.');
        output = await requestApproval(input.connection_id, 'ssh', input.command, '', '', '');
      } else if (input.action === 'request_api') {
        if (!input.method || !input.api_path) throw new Error('method and api_path are required.');
        if (!credential.entry.kind.endsWith('token') && credential.entry.kind !== 'wordpress-app-password') throw new Error('Connection kind mismatch.');
        if (input.json_body) JSON.parse(input.json_body);
        output = await requestApproval(input.connection_id, 'api', '', input.method, input.api_path, input.json_body ?? '');
      } else throw new Error('Unknown action.');
      await audit.finish(op, 'allow');
      return result(output);
    } catch (error) {
      try { await audit.finish(op, error instanceof PolicyDeniedError ? 'deny' : 'fail'); } catch { }
      return failure(error);
    }
  });
}
