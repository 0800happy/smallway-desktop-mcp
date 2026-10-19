// Project Connections V1.0 (2026-10-20).
// Treat every Markdown byte as untrusted data. The local registry contains pointers,
// never passwords or tokens; no network request or shell command is derived from MD text.
import { lstat, readFile, readdir, realpath } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import type { DesktopPolicy } from './desktop-policy.js';

export type ConnectionKind = 'ssh-password' | 'ssh-key' | 'cloudflare-token' | 'wordpress-app-password';

export interface ApprovedConnection {
  readonly id: string;
  readonly project_path: string;
  readonly source_file: string;
  readonly section: string;
  readonly kind: ConnectionKind;
  readonly host: string;
  readonly username: string;
  readonly secret_label: string;
  readonly host_key_sha256: string;
  readonly approved_utc: string;
}
type Registry = { version: number; connections: ApprovedConnection[] };
export type ResolvedConnection = { entry: ApprovedConnection; secret: string; keyFile?: string };

const ID = /^[a-f0-9-]{36}$/i;
const SKIP = new Set(['.git', 'node_modules', '.venv', 'dist', 'build', 'runtime', '_backup_code', 'backups', 'backup', '.next', '.astro']);
const MAX_MD_BYTES = 256 * 1024;
const MAX_FILES = 150;
const MAX_SCAN_DEPTH = 3;
const MAX_REGISTRY_BYTES = 256 * 1024;

function within(parent: string, child: string): boolean {
  const rel = path.relative(parent, child);
  return rel === '' || (rel !== '..' && !rel.startsWith('..' + path.sep) && !path.isAbsolute(rel));
}
// Local identity paths must stay in the approved project or dynamically resolved Home.
// Resolve the canonical target to prevent symlink/junction traversal (2026-10-20).
export async function resolveApprovedIdentityFile(entry: ApprovedConnection, identityValue: string): Promise<string> {
  const target = path.resolve(path.dirname(entry.source_file), identityValue);
  const home = process.env.USERPROFILE || process.env.HOME;
  if (!home || !path.isAbsolute(home)) throw new Error('Home directory is unavailable.');
  const canonical = await realpath(target);
  const roots = await Promise.all([entry.project_path, home].map(r => realpath(r)));
  if (!roots.some(r => within(r, canonical)) || !roots.some(r => within(r, target))) {
    throw new Error('SSH identity file escapes the approved project/home boundary.');
  }
  const info = await lstat(target);
  if (!info.isFile() || info.isSymbolicLink() || info.size > 65536) throw new Error('SSH identity file is invalid.');
  return canonical;
}
function clean(s: string): string {
  return s.trim().replace(/^["'\u0060]+|["'\u0060]+$/g, '').trim();
}
function normalized(s: string): string {
  return s.toLowerCase().replace(/[\s_\-:：\u0060*]/g, '');
}
function hostOnly(s: string): string {
  const v = clean(s);
  try { return new URL(v.includes('://') ? v : 'https://' + v).hostname.toLowerCase().replace(/\.$/, ''); }
  catch { return ''; }
}
export function safeHost(raw: string): string {
  const host = hostOnly(raw);
  if (!host || host.length > 253 || !/^[a-z0-9.:-]+$/i.test(host) || host.startsWith('-')) {
    throw new Error('Approved host is invalid.');
  }
  return host;
}
function mdFields(text: string): { section: string; key: string; value: string; line: number }[] {
  const result: { section: string; key: string; value: string; line: number }[] = [];
  let section = '';
  let fenced = false;
  const lines = text.replace(/\r/g, '').split('\n');
  for (let n = 0; n < lines.length; n++) {
    const raw = lines[n]!;
    const heading = /^\s*#{1,6}\s+(.+?)\s*#*\s*$/.exec(raw);
    if (heading && !fenced) { section = clean(heading[1]!); continue; }
    if (/^\s*(?:\u0060{3}|~{3})/.test(raw)) { fenced = !fenced; continue; }
    let parts: string[] = [];
    if (/^\s*\|/.test(raw)) {
      parts = raw.split('|').slice(1, -1).map(clean);
      if (parts.length < 2 || /^:?-{2,}:?$/.test(parts[0]!)) continue;
    } else {
      const match = /^\s*(?:[-*]\s*)?(?:["']?)([^:=：|]{2,70}?)\s*(?::|：|=)\s*(.*?)\s*$/.exec(raw);
      if (match) parts = [clean(match[1]!), clean(match[2]!)];
    }
    if (parts.length >= 2 && parts[0] && parts[1] && result.length < 600) {
      result.push({ section, key: parts[0]!, value: parts[1]!, line: n + 1 });
    }
  }
  return result;
}
function isSecret(kind: ConnectionKind, label: string): boolean {
  const k = normalized(label);
  if (kind === 'ssh-key') return /^(identityfile|privatekey|privatekeypath|sshkey|sshkeypath|私鑰路徑|私鑰檔案|金鑰路徑)$/.test(k);
  if (kind === 'ssh-password') return /(?:password|passwd|密碼|登入密碼|sshpass)/.test(k) && !/(?:key|api|token|app|應用程式)/.test(k);
  if (kind === 'wordpress-app-password') return /(?:applicationpassword|apppassword|應用程式密碼)/.test(k);
  return /(?:apitoken|api[_-]?key|access[_-]?token|cloudflaretoken|cloudflarekey|金鑰|權杖)/.test(k);
}
function isHost(label: string): boolean {
  return /^(?:sshhost|hostname|host|server|serverip|ip|ipaddress|主機|主機ip|伺服器|伺服器ip|網址|網域|domain|url|siteurl|wordpressurl)$/i.test(normalized(label));
}
function isUser(label: string): boolean {
  return /^(?:sshuser|sshusername|username|user|login|loginuser|帳號|使用者|使用者名稱|登入帳號)$/i.test(normalized(label));
}
export function parseCredential(
  text: string,
  entry: Pick<ApprovedConnection, 'kind'|'section'|'secret_label'|'host'|'username'>
): { secret: string } {
  const fields = mdFields(text).filter(field => !entry.section || normalized(field.section) === normalized(entry.section));
  if (fields.length === 0) throw new Error('Approved Markdown section is missing or has no fields.');
  const hosts = fields.filter(f => isHost(f.key)).map(f => hostOnly(f.value)).filter(Boolean);
  const users = fields.filter(f => isUser(f.key)).map(f => clean(f.value).toLowerCase());
  if (entry.kind.startsWith('ssh-') || entry.kind === 'wordpress-app-password') {
    if (!hosts.includes(safeHost(entry.host))) throw new Error('Approved host no longer matches the Markdown source.');
    if (!users.includes(entry.username.trim().toLowerCase())) throw new Error('Approved account no longer matches the Markdown source.');
  }
  const candidates = fields.filter(f =>
    entry.secret_label
      ? normalized(f.key) === normalized(entry.secret_label)
      : isSecret(entry.kind, f.key)
  );
  if (candidates.length !== 1 || !candidates[0]!.value) {
    throw new Error('Credential label missing or ambiguous. Reconfirm mapping locally.');
  }
  const secret = clean(candidates[0]!.value);
  if (!secret || secret.length > 8192) throw new Error('Credential has an invalid length.');
  if (/\$\{|\{\{/.test(secret)) throw new Error('Credential appears to contain an unresolved template.');
  return { secret };
}

export function redacted(text: string, secrets: readonly string[]): string {
  let v = text;
  for (const secret of secrets) {
    if (secret.length > 0) v = v.split(secret).join('[REDACTED]');
  }
  return v.replace(/(authorization\s*[:=]\s*(?:bearer|basic)\s+)\S+/gi, '$1[REDACTED]')
    .replace(/((?:api[_-]?token|password|secret)\s*[:=]\s*)[^\s,"}]+/gi, '$1[REDACTED]');
}
function registryPath(): string {
  const base = process.env.LOCALAPPDATA;
  if (!base || !path.isAbsolute(base)) throw new Error('Local application data directory is unavailable.');
  return path.join(base, 'DesktopMCP', 'project-connections', 'registry.json');
}
export async function getApprovedConnections(policy: DesktopPolicy): Promise<ApprovedConnection[]> {
  let data: Buffer;
  try {
    const filename = registryPath();
    const info = await lstat(filename);
    if (!info.isFile() || info.size > MAX_REGISTRY_BYTES) throw new Error('Invalid local connection registry.');
    data = await readFile(filename);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw error;
  }
  if (data.length > MAX_REGISTRY_BYTES) throw new Error('Local connection registry exceeds size limit.');
  const registry: unknown = JSON.parse(data.toString('utf8'));
  if (!registry || typeof registry !== 'object' || (registry as Registry).version !== 1 ||
      !Array.isArray((registry as Registry).connections)) throw new Error('Invalid local connection registry format.');
  const results: ApprovedConnection[] = [];
  const unique = new Set<string>();
  for (const e of (registry as Registry).connections) {
    if (!e || typeof e !== 'object' || !ID.test(e.id) || unique.has(e.id) ||
        typeof e.project_path !== 'string' || typeof e.source_file !== 'string' ||
        !['ssh-password', 'ssh-key', 'cloudflare-token', 'wordpress-app-password'].includes(e.kind) ||
        typeof e.host !== 'string' || typeof e.username !== 'string' ||
        typeof e.section !== 'string' || typeof e.secret_label !== 'string' ||
        typeof e.host_key_sha256 !== 'string') throw new Error('Invalid local connection entry.');
    unique.add(e.id);
    const project = await policy.resolveReadPath(e.project_path);
    const md = await policy.resolveReadPath(e.source_file);
    if (!within(project, md) || path.extname(md).toLowerCase() !== '.md') {
      throw new Error('Registered Markdown file escapes its approved project.');
    }
    safeHost(e.host);
    if (e.kind.startsWith('ssh-') && !/^SHA256:[A-Za-z0-9+/]{43}$/.test(e.host_key_sha256)) {
      throw new Error('SSH connection is missing a pinned SHA-256 host key.');
    }
    results.push(e);
    if (results.length > 100) throw new Error('Too many registered project connections.');
  }
  return results;
}
export async function loadApprovedCredential(
  policy: DesktopPolicy, id: string
): Promise<ResolvedConnection> {
  const entry = (await getApprovedConnections(policy)).find(e => e.id === id);
  if (!entry) throw new Error('Connection is not locally approved or has been revoked.');
  const source = await policy.resolveReadPath(entry.source_file);
  const info = await lstat(source);
  if (!info.isFile() || info.size > MAX_MD_BYTES) throw new Error('Approved Markdown source is invalid or too large.');
  const content = await readFile(source, 'utf8');
  const parsed = parseCredential(content, entry);
  // Password rotation is permitted; target host and account remain pinned to the approval.
  return { entry, secret: parsed.secret };
}
export async function scanProjectMarkdown(policy: DesktopPolicy, projectPath: string) {
  const root = await policy.resolveReadPath(projectPath);
  if (!(await lstat(root)).isDirectory()) throw new Error('Project path is not a directory.');
  const items: { file: string; sections: string[]; types: string[] }[] = [];
  const queue = [{ dir: root, depth: 0 }];
  while (queue.length && items.length < MAX_FILES) {
    const item = queue.shift()!;
    for (const dirent of await readdir(item.dir, { withFileTypes: true })) {
      if (SKIP.has(dirent.name.toLowerCase())) continue;
      const filename = path.join(item.dir, dirent.name);
      const stat = await lstat(filename);
      if (stat.isSymbolicLink()) continue; // No directory junction or symlink traversal.
      if (stat.isDirectory()) {
        if (item.depth < MAX_SCAN_DEPTH) {
          const canonical = await realpath(filename);
          if (within(root, canonical) && queue.length < 150) queue.push({ dir: canonical, depth: item.depth + 1 });
        }
        continue;
      }
      if (!stat.isFile() || path.extname(filename).toLowerCase() !== '.md' || stat.size > MAX_MD_BYTES) continue;
      const canonical = await policy.resolveReadPath(filename);
      if (!within(root, canonical)) continue;
      const fields = mdFields(await readFile(canonical, 'utf8'));
      const types = new Set<ConnectionKind>();
      for (const field of fields) {
        for (const kind of ['ssh-password','ssh-key','cloudflare-token','wordpress-app-password'] as const) {
          if (isSecret(kind, field.key)) types.add(kind);
        }
      }
      if (types.size) items.push({
        file: path.relative(root, canonical),
        sections: [...new Set(fields.filter(f => [...types].some(k => isSecret(k, f.key))).map(f => f.section))].slice(0, 20),
        types: [...types]
      });
      if (items.length >= MAX_FILES) break;
    }
  }
  return { project: root, candidates: items, scanned_limit: MAX_FILES, note: 'Values are never included. Approve connection details locally in DeskMCP Settings.' };
}
export function safeConnectionSummary(entry: ApprovedConnection) {
  return { id: entry.id, kind: entry.kind, project: entry.project_path, source_file: entry.source_file,
    section: entry.section, host: entry.host, username: entry.username,
    approved_utc: entry.approved_utc, host_key_pinned: Boolean(entry.host_key_sha256) };
}
export function newApprovalId(): string { return randomUUID(); }
export function localRegistryFile(): string { return registryPath(); }
