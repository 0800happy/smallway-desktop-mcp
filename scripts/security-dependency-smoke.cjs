// Dependency compatibility check for the remediated Desktop Commander tree.
const assert = require('node:assert/strict');
const path = require('node:path');
const {createRequire} = require('node:module');
const commander = createRequire(path.resolve(__dirname, '..', 'node_modules', '@wonderwhy-er', 'desktop-commander', 'package.json'));
const mdToPdf = commander('md-to-pdf');
const pdf = createRequire(commander.resolve('md-to-pdf'));
const matter = pdf('gray-matter');
const parsed = matter('---\ntitle: DeskMCP test\n---\nHello');
assert.equal(parsed.data.title, 'DeskMCP test');
assert.equal(parsed.content.trim(), 'Hello');
assert.equal(typeof pdf('chokidar').watch, 'function');
const sharp = commander('sharp');
assert.ok(sharp.versions.sharp);
assert.equal(typeof mdToPdf.mdToPdf, 'function');
console.log('SECURITY_DEPENDENCY_SMOKE=PASS', JSON.stringify({sharp: sharp.versions.sharp, parsed: parsed.data.title}));
