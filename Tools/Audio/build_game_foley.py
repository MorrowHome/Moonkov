#!/usr/bin/env python3
"""Build short original game excerpts and their single human-readable source list."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceData/Audio/GameFoley'
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/GameFoley'
RATE = 48000


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sources = json.loads((SOURCE / 'Sources.json').read_text())
    records = []
    for row in sources:
        path = ROOT / row['file']
        if sha(path) != row['sha256']:
            raise ValueError('Source hash mismatch: ' + str(path))
        filters = ['-af', 'acompressor=threshold=0.025:ratio=4:attack=0.1:release=40:detection=peak'] if row.get('compress', True) else []
        raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(path)] + filters + [
                                       '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
        a = np.frombuffer(raw, '<f4').astype(float).copy()
        if 'source_file_excerpt_seconds' in row:
            start, end = row['source_file_excerpt_seconds']
            if not 0 <= start < end <= len(a) / RATE:
                raise ValueError('Invalid excerpt: ' + row['name'])
            a = a[round(start * RATE):round(end * RATE)]
        a -= a.mean()
        if row.get('loop'):
            # Rotate the splice away from the loop boundary and crossfade its overlap.
            n = round(.04 * RATE)
            blend = np.linspace(0, 1, n)
            a = np.concatenate((a[n:-n], a[-n:] * (1 - blend) + a[:n] * blend))
        else:
            n = min(round(.006 * RATE), len(a) // 8)
            a[:n] *= np.linspace(0, 1, n); a[-n:] *= np.linspace(1, 0, n)
        a *= min(row.get('target_rms', .13) / max(np.sqrt(np.mean(a * a)), 1e-9),
                 .65 / max(max(abs(a)), 1e-9))
        out = OUT / (row['name'] + '.wav')
        with wave.open(str(out), 'wb') as w:
            w.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
            w.writeframes(np.round(a * 32767).astype('<i2').tobytes())
        records.append(dict(asset=str(out.relative_to(ROOT)), source=row,
                            seconds=len(a) / RATE, peak=float(max(abs(a))),
                            rms=float(np.sqrt(np.mean(a*a))), sha256=sha(out)))
    manifest = dict(processing='Mono 48 kHz PCM16, DC removal, linear gain, 6 ms edge fades; '
                               'consumables: peak compression (threshold 0.025, ratio 4, attack 0.1 ms, release 40 ms); '
                               'loop only: 40 ms crossfade. Original pitch/speed, no synthesis.', clips=records)
    (ROOT / 'Assets/Audio/Moonkov/GameFoleySources.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    lines = ['# 游戏原声音频来源', '', '每行对应一条实际成品。时间为来源视频中的秒数。', '',
             '这些是原游戏录音。版权归原权利人。用户指定用于个人非商用作品；此记录不声明额外授权。', '',
             '成品统一为 48 kHz 单声道 PCM。保留原音高和速度。做剪辑、去直流、音量和边缘淡化；塔科夫片段另压低瞬时尖峰以提高主体音量，循环片段加 40 毫秒交叉淡化。猎杀针剂不做压缩。', '',
             '| 成品文件 | 游戏内用途 | 原游戏 / 权利人 | 录音出处 | 原片段 |',
             '| --- | --- | --- | --- | --- |',
             '| `Clips/Medical/InjectorHuntShot.wav` | 扎入、推注、拔针 | Hunt: Showdown v0.309 / Crytek GmbH | [Kylan Klein Lenderink / Noise Nerd Audio](https://www.youtube.com/watch?v=i3-9okS87S8) | 8.64–8.94、9.22–10.34、11.25–11.62 秒；依次放在成品 0、0.24、1.00 秒 |']
    for r in sources:
        excerpt = r.get('original_file') or '–'.join(f'{v:.3f}' for v in r['source_excerpt_seconds']) + ' 秒'
        if 'source_file_excerpt_seconds' in r:
            excerpt += '；取 ' + '–'.join(f'{v:.3f}' for v in r['source_file_excerpt_seconds']) + ' 秒'
        lines.append(f"| `Clips/GameFoley/{r['name']}.wav` | {r['use']} | {r['game']} / {r['copyright']} | [{r['uploader']}]({r['page']}) | {excerpt} |")
    lines += ['', '路径前缀为 `Assets/Audio/Moonkov/`。视频截录保留原录背景，不作为解包无损文件标注。', '',
              '原始短片段和哈希：`SourceData/Audio/GameFoley/Sources.json`；成品校验和：`Assets/Audio/Moonkov/GameFoleySources.json`。',
              '猎杀针剂详表：`SourceData/Audio/MedicalFoley/Sources.json` 与 `Assets/Audio/Moonkov/MedicalFoleySources.json`。',
              '重建：`python3 Tools/Audio/build_game_foley.py`，然后在 Unity 执行 `GameFoleySetup.Install()`、`AirlockButtonAudioSetup.Install()` 、`TacticalFoleySetup.Install()` 与 `MovementFoleySetup.Install()`。',
              '本表登记所有当前主动引入的其他游戏原声。既有 CC0、CC BY 和 Sonniss 素材保留各自原署名文件。', '']
    breath = json.loads((ROOT / 'Assets/Audio/Moonkov/StaminaBreathSources.json').read_text())
    lines += ['## 真人急喘录音', '', '以下为 CC0 真人录音。保存的是 Freesound 高质量 MP3 预览，不是游戏原声或无损原文件。', '',
              '| 成品文件 | 用途 | 作者 / 授权 | 录音出处 | 原片段 |', '| --- | --- | --- | --- | --- |']
    for clip in breath['clips']:
        excerpt = '–'.join(f"{v:.2f}" for v in clip['source_excerpt_seconds'])
        lines.append(f"| `{clip['asset']}` | 低体力与耗尽体力急喘 | {breath['author']} / {breath['license']} | [woman run and breath.wav]({breath['source_page']}) | {excerpt} 秒 |")
    lines += ['', '急喘重建：`python3 Tools/Audio/build_stamina_breath.py`，然后执行 `MoonkovRaidAudioSetup.InstallBreathing()`。', '']
    lines += ['## 恢复的搜刮提示', '',
              '| 成品文件 | 用途 | 录音出处 | 原文件 |', '| --- | --- | --- | --- |',
              '| `Clips/Interaction/Container.wav` | 搜刮成功时的原开箱提示 | Jake Fielding / Sonniss GDC 2024，[原包授权](https://sonniss.com/gdc-bundle-license/) | `Jake Fielding - Train Onboards/DOORHydr_Train Door Close_JF_Train Tracks .wav` |', '',
              '此文件恢复原有音色。不是其他游戏原声，也不是 CC0。保留既有 `Assets/Audio/Moonkov/Sources.json` 的来源与哈希记录。', '']
    (ROOT / 'Assets/StreamingAssets/MoonkovGameAudioSources.md').write_text('\n'.join(lines))
    print(json.dumps(dict(clips=len(records), seconds=sum(r['seconds'] for r in records)), ensure_ascii=False))


if __name__ == '__main__':
    main()
