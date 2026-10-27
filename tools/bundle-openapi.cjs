#!/usr/bin/env node
// bundle-openapi.cjs <input.yaml> <output.json>
//
// Produces a single OpenAPI document from a service contract by moving every external
// component it references into the root `components` section and rewriting the
// references to local ones (`#/components/<type>/<name>`).
//
// Why: ng-openapi-gen only resolves local `#/components/...` references. Contracts keep
// their shared components in `contracts/openapi/_shared/components.yaml`, referenced as
// `_shared/components.yaml#/components/<type>/<name>`. Inlining the whole file with a
// generic bundler produces JSON pointers into `paths/...` that ng-openapi-gen cannot
// resolve, so the components are copied under their original names instead, which keeps
// the generated model names stable.
//
// Only `#/components/<type>/<name>` pointers are supported; anything else fails loudly.
// The YAML parser is the one ng-openapi-gen depends on, resolved from web/node_modules.
const path = require('node:path');
const fs = require('node:fs');

const [input, output] = process.argv.slice(2);
if (!input || !output) {
  console.error('usage: bundle-openapi.cjs <input.yaml> <output.json>');
  process.exit(2);
}

const webDir = path.resolve(__dirname, '..', 'web');
const RefParser = require(require.resolve('@apidevtools/json-schema-ref-parser', { paths: [webDir] }));

const rootFile = path.resolve(input);
const parsed = new Map();
const claimed = new Map(); // "<type>/<name>" -> absolute file it was copied from

async function load(file) {
  if (!parsed.has(file)) parsed.set(file, await RefParser.parse(file));
  return parsed.get(file);
}

function unescapePointer(segment) {
  return segment.replace(/~1/g, '/').replace(/~0/g, '~');
}

async function walk(node, base, out) {
  if (Array.isArray(node)) {
    for (let i = 0; i < node.length; i++) node[i] = await walk(node[i], base, out);
    return node;
  }
  if (node === null || typeof node !== 'object') return node;

  if (typeof node.$ref === 'string') {
    const hash = node.$ref.indexOf('#');
    const filePart = hash === -1 ? node.$ref : node.$ref.slice(0, hash);
    const pointer = hash === -1 ? '' : node.$ref.slice(hash + 1);
    const target = filePart === '' ? base : path.resolve(path.dirname(base), filePart);
    const { $ref, ...siblings } = node;

    if (target === rootFile) return { ...siblings, $ref: `#${pointer}` };

    const parts = pointer.split('/').filter((p) => p !== '').map(unescapePointer);
    if (parts.length !== 3 || parts[0] !== 'components') {
      throw new Error(`unsupported external $ref "${node.$ref}" in ${base} (only #/components/<type>/<name>)`);
    }
    const [, type, name] = parts;
    const key = `${type}/${name}`;
    const local = `#/components/${type}/${name}`;

    if (claimed.has(key)) {
      if (claimed.get(key) !== target) {
        throw new Error(`component ${key} is defined in both ${claimed.get(key)} and ${target}`);
      }
      return { ...siblings, $ref: local };
    }

    const source = await load(target);
    const definition = source.components?.[type]?.[name];
    if (definition === undefined) throw new Error(`${node.$ref} (referenced from ${base}) does not exist`);
    if (out.components?.[type]?.[name] !== undefined) {
      throw new Error(`component ${key} from ${target} clashes with a component of the root document`);
    }

    claimed.set(key, target);
    out.components ??= {};
    out.components[type] ??= {};
    out.components[type][name] = null; // placeholder while the copy is walked (cycles)
    out.components[type][name] = await walk(structuredClone(definition), target, out);
    return { ...siblings, $ref: local };
  }

  for (const key of Object.keys(node)) {
    node[key] = await walk(node[key], base, out);
  }
  return node;
}

(async () => {
  const doc = await load(rootFile);
  const out = structuredClone(doc);
  await walk(out, rootFile, out);
  fs.writeFileSync(output, JSON.stringify(out, null, 2));
})().catch((err) => {
  console.error(`ERROR: cannot bundle ${input}: ${err.message}`);
  process.exit(1);
});
