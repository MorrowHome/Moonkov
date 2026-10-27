# LR06 恢复说明与校验

已父级图审接受为两灯条暖色校准工作版；不是全场景概念完成或 Unity 验收。

完整文件：`Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend`
大小：341657959 B
SHA256：`cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054`

仅将下面四片放入同一空目录，保存 Python 代码为 restore.py，用 Python 3 执行 `python restore.py`。不要直接在 Blender 打开分片，不要覆盖旧版本。

- `Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part01` — 94371840 B — SHA256 `851f80550ec753b8ccfadb25cac0746be10da665c81f175faf9916d31b27bb54`
- `Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part02` — 94371840 B — SHA256 `314e5b86a1b2e8d2268562f12bad60d227bb092c5ea6842b667d4e5727a1de66`
- `Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part03` — 94371840 B — SHA256 `66c5e51cb5bdb01d07d9385eba3ec71774ac3ffe1184df06d4d2488cafbe7a4d`
- `Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part04` — 58542439 B — SHA256 `8676139487334ba3a8e2bc2ec55cba09973a52785b3632e749d8737c714506ee`

## Python 恢复

```python
from pathlib import Path
import hashlib
parts = [('Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part01', 94371840, '851f80550ec753b8ccfadb25cac0746be10da665c81f175faf9916d31b27bb54'), ('Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part02', 94371840, '314e5b86a1b2e8d2268562f12bad60d227bb092c5ea6842b667d4e5727a1de66'), ('Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part03', 94371840, '66c5e51cb5bdb01d07d9385eba3ec71774ac3ffe1184df06d4d2488cafbe7a4d'), ('Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend.part04', 58542439, '8676139487334ba3a8e2bc2ec55cba09973a52785b3632e749d8737c714506ee')]
out = Path('Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend')
if out.exists():
    raise FileExistsError('Refusing to overwrite an existing blend')
for name, size, expected in parts:
    p = Path(name)
    assert p.stat().st_size == size, name
    assert hashlib.sha256(p.read_bytes()).hexdigest() == expected, name
with out.open('xb') as dst:
    for name, _, _ in parts:
        with Path(name).open('rb') as src:
            while chunk := src.read(4 * 1024 * 1024):
                dst.write(chunk)
assert out.stat().st_size == 341657959
assert hashlib.sha256(out.read_bytes()).hexdigest() == 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
print('LR06_REASSEMBLY_SHA256_PASS')
```

## 验证范围

此处 Python 恢复代码已经从说明提取并实际执行，本地实物重组大小与 SHA256 均通过。PowerShell 未运行；不宣称已验证 PowerShell。Drive 上传后仅核对远端名称、大小、父目录及权限 metadata，不等于远端下载字节的 SHA256 验证。

## 保留与回退

Library 保留同一完整文件身份的 LR05 v4 历史版本；此前 Drive LR05 分片和恢复说明不删除。LR05 完整大小 341637697 B，SHA256 `131c9abf438117bc20f61564e07ae82538c5dc171fb463a71540e33cc2c8daab`。HU01 v7 与作者版不动。整体图仍 LR04、中景图仍 LR03，均不是 LR06 新图。LR05 同机位基线复用，不重复上传。
