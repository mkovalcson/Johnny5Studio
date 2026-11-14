import { readFile, writeFile } from 'node:fs/promises';

const stamp = new URL('../../ServoAnimator/Build/PROJECT_VERSION.json', import.meta.url);
const { version } = JSON.parse((await readFile(stamp, 'utf8')).replace(/^\uFEFF/, ''));
if (typeof version !== 'string' || !/^\d+\.\d+\.\d+$/.test(version)) {
  throw new Error('Invalid Animation Editor version stamp');
}

for (const [relativePath, key, value] of [
  ['../package.json', 'version', version],
  ['../com.johnny5.animationeditor.sdPlugin/manifest.json', 'Version', version + '.0'],
]) {
  const file = new URL(relativePath, import.meta.url);
  const source = await readFile(file, 'utf8');
  const pattern = new RegExp('("' + key + '"\\s*:\\s*")[0-9.]+(")');
  const updated = source.replace(pattern, (_, prefix, suffix) => prefix + value + suffix);
  if (!pattern.test(source)) throw new Error('Missing version in ' + file.pathname);
  if (updated !== source) await writeFile(file, updated);
}
