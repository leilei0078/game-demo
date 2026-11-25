import sharp from 'sharp';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const folder = fileURLToPath(new URL('../output/frontier-study-01/', import.meta.url));
const width = 1584, height = 1260;
const panels = [
  ['shelter', '01', '营地木屋', 'SPLIT-SHAKE SHELTER', '分层木瓦 · 粗石脚基 · 门前布篷'],
  ['crib', '02', '露天原木堆场', 'OPEN LOG CRIB', '横枕与立桩 · 朝路一侧露出木料端面'],
  ['fence', '03', '拼接木栅栏', 'TIMBER FENCE', '复用现有组件 · 单节一米'],
  ['standard', '04', '暗红路旗', 'MADDER STANDARD', '以布色提示堆场位置 · 无需发光'],
];
const ink = '#2c3733', muted = '#626d65', paper = '#deddd2', copper = '#87553d';
const letters = (left, top, text, size, color, font = 'PingFang SC, sans-serif', spacing = 0) => `<text x="${left}" y="${top}" font-family="${font}" font-size="${size}" letter-spacing="${spacing}" fill="${color}">${text}</text>`;
let page = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"><rect width="100%" height="100%" fill="${paper}"/><rect x="52" y="43" width="35" height="5" fill="${copper}"/>`;
page += letters(104, 51, 'FRONTIER  /  MATERIAL STUDIES', 16, muted, 'Helvetica Neue, sans-serif', 3);
page += letters(49, 119, '木寨营地 · 资产手记', 52, ink, 'Songti SC, serif');
page += letters(1210, 69, '迷雾大陆     /     视觉开发', 19, ink) + letters(1233, 110, 'FIELD NOTES     01—04', 16, muted, 'Helvetica Neue, sans-serif', 2);
page += '<path d="M52 155 H1532 M52 1189 H1532" stroke="#a0a698" fill="none"/>';
const layers = [];
for (const [idx, [key, num, name, latin, notes]] of panels.entries()) {
  const left = 52 + (idx % 2) * 754, top = 182 + Math.floor(idx / 2) * 498;
  const tile = await sharp(join(folder, `${key}.png`)).resize(726, 365, { fit: 'cover', position: 'center' }).png().toBuffer();
  layers.push({ input: tile, left, top });
  page += letters(left, top + 403, num, 24, copper, 'Georgia, serif') + letters(left + 57, top + 403, name, 28, ink, 'Songti SC, serif');
  page += letters(left, top + 435, latin, 13, muted, 'Helvetica Neue, sans-serif', 2) + letters(left, top + 463, notes, 17, muted);
}
page += letters(52, 1229, '斜俯视样板   /   同场景模型拆件；各格独立取景，比例不统一。', 18, muted);
page += letters(1300, 1229, 'OUTPOST    /    01', 16, copper, 'Helvetica Neue, sans-serif', 2) + '</svg>';
await sharp(Buffer.from(page)).composite(layers).png().toFile(join(folder, 'kit-sheet.png'));
