// Smallway Desktop MCP: deterministic MIT-licensed upstream compatibility patch.
// The installed upstream version 0.2.47 calls a Zod-3-only JSON Schema converter.
// Zod 4's native conversion preserves MCP input schemas when Zod 3 is removed.
// This patch is intentionally fail-closed and must NEVER run against an unknown build.
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const root = path.resolve(process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
const vendor = path.join(root, 'node_modules', '@wonderwhy-er', 'desktop-commander');
const packagePath = path.join(vendor, 'package.json');
const serverPath = path.join(vendor, 'dist', 'server.js');
const pkg = JSON.parse(readFileSync(packagePath, 'utf8'));
if (pkg.name !== '@wonderwhy-er/desktop-commander' || pkg.version !== '0.2.47') {
  throw new Error('Unsupported Desktop Commander package: ' + pkg.name + '@' + pkg.version);
}
const requireVendor = createRequire(packagePath);
const zod = requireVendor('zod');
if (typeof zod.toJSONSchema !== 'function') {
  throw new Error('Zod 4 native toJSONSchema is unavailable; refusing incompatible runtime.');
}
const zodPackage = requireVendor('zod/package.json');
if (zodPackage.version !== '4.6.7') {
  throw new Error('Expected approved Zod 4.6.7, got ' + zodPackage.version);
}

const hash = text => createHash('sha256').update(text).digest('hex');
const stockHash = '3bc0962a60ff74b5c8a95e1627cb1e8fcb87e5a1bee19426c9e73f5107f84428';
const patchedHash = '68512aaff39ce63b85852516894e8c4362f8aef6cd16566c0169fabc4bb8776d';
const oldImport = 'import { zodToJsonSchema } from "zod-to-json-schema";';
const newConverter = [
  'import { z } from "zod";',
  '// Smallway compatibility patch: use Zod 4 native JSON Schema for MCP inputs.',
  'const zodToJsonSchema = (schema) => {',
  '    const json = z.toJSONSchema(schema, { io: "input", unrepresentable: "any" });',
  '    delete json.$schema;',
  '    return json;',
  '};'
].join('\n');
const source = readFileSync(serverPath, 'utf8');
const current = hash(source);
if (current === patchedHash) {
  console.log('DESKTOP_COMMANDER_ZOD4_PATCH=ALREADY_VALID sha256=' + current);
} else {
  if (current !== stockHash) {
    throw new Error('Upstream server SHA256 changed; refusing unreviewed patch. sha256=' + current);
  }
  if (source.split(oldImport).length !== 2) {
    throw new Error('Expected exactly one upstream zod-to-json-schema import.');
  }
  const patched = source.replace(oldImport, newConverter);
  if (hash(patched) !== patchedHash) {
    throw new Error('Patched server hash mismatch; incompatible upstream formatting.');
  }
  writeFileSync(serverPath, patched, 'utf8');
  console.log('DESKTOP_COMMANDER_ZOD4_PATCH=PASS sha256=' + hash(readFileSync(serverPath)));
}

// Exercise Desktop Commander's real MCP ListToolsRequestSchema handler, not merely
// the shape of a hand-built Zod sample. Any lost argument types block packaging.
const { server } = await import(pathToFileURL(serverPath).href);
const listTools = server._requestHandlers?.get('tools/list');
if (typeof listTools !== 'function') {
  throw new Error('Upstream MCP tools/list handler is unavailable.');
}
const response = await listTools({ method: 'tools/list', params: {} });
const tools = response?.tools ?? [];
if (tools.length !== 26) {
  throw new Error('Unexpected upstream MCP tool count ' + tools.length + '; expected 26.');
}
for (const tool of tools) {
  const schema = tool.inputSchema;
  if (!schema || schema.type !== 'object' || !schema.properties || Array.isArray(schema.properties)) {
    throw new Error('Broken MCP input schema: ' + tool.name);
  }
}
for (const [toolName, field] of [['read_file', 'path'], ['start_process', 'command']]) {
  const tool = tools.find(item => item.name === toolName);
  if (tool?.inputSchema?.properties?.[field]?.type !== 'string') {
    throw new Error('Required MCP tool argument missing: ' + toolName + '.' + field);
  }
}
console.log('DESKTOP_COMMANDER_MCP_INPUT_SCHEMAS=PASS count=' + tools.length);
// Importing upstream server may initialize optional telemetry modules;
// this verifier never starts a transport and exits promptly on completion.
process.exit(0);
