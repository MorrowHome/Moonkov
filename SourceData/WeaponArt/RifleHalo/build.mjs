// Vector-authored artwork. No image synthesis, noise or random distressing.
// Run with Node and sharp (available in the Codex workspace runtime), from any cwd.
import { createRequire } from 'node:module';
const sharp = createRequire(import.meta.url)('sharp');
import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const source = path.dirname(fileURLToPath(import.meta.url));
const project = path.resolve(source, '../../..');
const output = path.join(project, 'Assets/DollSinger/Art/Rifle');
await mkdir(output, { recursive: true });
for (const name of ['Petal', 'OuterArc', 'InnerArc', 'Cell']) {
  await sharp(await readFile(path.join(source, `${name}.svg`))).png().toFile(path.join(output, `${name}.png`));
}
console.log('Rifle ink sprites exported. Import with Tools/Doll Singer/Install Rifle Iris Halo.');
