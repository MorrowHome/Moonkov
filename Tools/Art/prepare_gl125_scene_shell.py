"""Prepare a scratch copy of the pre-GL125 scene without loading its old bake assets."""

import argparse
import hashlib
import re
from pathlib import Path


def prepare(scene, output):
    original = scene.read_bytes()
    source = original.decode("utf-8").replace("\r\n", "\n")
    blocks = re.split(r"(^--- !u!\d+ &-?\d+(?: stripped)?\n)", source, flags=re.M)
    documents = {}
    for header, body in zip(blocks[1::2], blocks[2::2]):
        kind, id_ = map(int, re.match(r"^--- !u!(\d+) &(-?\d+)", header).groups())
        if id_ in documents:
            raise ValueError(f"Duplicate scene ID {id_}")
        documents[id_] = (kind, header, body)

    old_root, = (body for kind, _, body in documents.values()
                 if kind == 1 and re.search(r"^  m_Name: Source World Static$", body, re.M))
    component_ids = r"component: \{fileID: (-?\d+)\}"
    transform, = (int(id_) for id_ in re.findall(component_ids, old_root) if documents[int(id_)][0] == 4)
    if not re.search(r"^  m_Father: \{fileID: 0\}$", documents[transform][2], re.M):
        raise ValueError("The old static source is no longer a scene root")

    pending, retired = [transform], set()
    while pending:
        current = pending.pop()
        kind, _, body = documents[current]
        if kind != 4 or "  m_PrefabInstance: {fileID: 0}" not in body:
            raise ValueError(f"Unexpected prefab or component in the old visual tree: {current}")
        game = int(re.search(r"^  m_GameObject: \{fileID: (-?\d+)\}$", body, re.M)[1])
        _, _, owner = documents[game]
        retired.add(game)
        retired.update(int(id_) for id_ in re.findall(component_ids, owner))
        children = body.split("  m_Children:", 1)[1].split("  m_Father:", 1)[0]
        pending.extend(map(int, re.findall(r"^  - \{fileID: (-?\d+)\}$", children, re.M)))

    scratch = [blocks[0]]
    roots = lighting = occlusion = 0
    for id_, (kind, header, body) in documents.items():
        if id_ in retired:
            continue
        if kind == 1660057539:
            root = f"  - {{fileID: {transform}}}\n"
            if body.count(root) != 1:
                raise ValueError("The source visual root is missing from SceneRoots")
            body = body.replace(root, "")
            roots += 1
        if kind == 114 and "::Unity.MP_FPS.Moon.MoonSceneLighting" in body:
            body, count = re.subn(r"^  sourceStaticLighting: .*guid:.*$", "  sourceStaticLighting: {fileID: 0}", body, flags=re.M)
            lighting += count
        if kind == 29:
            body, count = re.subn(r"^  m_OcclusionCullingData: .*guid:.*$", "  m_OcclusionCullingData: {fileID: 0}", body, flags=re.M)
            occlusion += count
        references = {int(id_) for id_ in re.findall(r"\{fileID: (-?\d+)\}", body)}
        if references & retired:
            raise ValueError(f"Scene document {id_} still references retired geometry")
        scratch.extend((header, body))
    if (roots, lighting, occlusion) != (1, 1, 1):
        raise ValueError("Pre-GL125 lighting or occlusion settings changed")
    output.mkdir(parents=True, exist_ok=True)
    (output / "FormalSceneShell.unity").write_text("".join(scratch), encoding="utf-8", newline="\n")
    (output / "FormalSceneSha.txt").write_text(hashlib.sha256(original).hexdigest(), encoding="ascii")
    print(f"Removed {len(retired)} obsolete visual records from the scratch copy")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scene", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    prepare(args.scene, args.output)
