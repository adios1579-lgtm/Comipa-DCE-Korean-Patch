"""Offline patch recipe runner. Never writes game files or starts the game."""
import json
import os
from pathlib import Path
import sys
import traceback

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'patch'))
import apply_story_mode as patch


def prepare(root, stage, request):
    root, stage, request = Path(root).resolve(), Path(stage).resolve(), Path(request).resolve()
    if stage != root / '.comipa-update' / 'new' or request != stage.parent / 'generate.json':
        raise ValueError('잘못된 패치 준비 경로')
    patch.HERE = getattr(sys, '_MEIPASS', str(Path(__file__).resolve().parents[2] / 'patch'))
    p = patch.load_patch()
    p['_images'], p['_voices'] = patch.load_images(), patch.load_voices()
    recipes = {name: (digest, fn) for name, digest, fn in patch.targets(p)}
    names = json.loads(request.read_text(encoding='utf-8-sig'))
    if not isinstance(names, list) or len(names) != len(set(names)) or not set(names) <= recipes.keys():
        raise ValueError('허용되지 않는 패치 대상')
    for name in names:
        digest, fn = recipes[name]
        data = fn(patch.original_bytes(str(root), name, digest), p, str(root))
        destination = stage / name
        if destination.exists():
            raise ValueError('이미 존재하는 준비 파일: ' + name)
        destination.write_bytes(data)


if __name__ == '__main__':
    try:
        if len(sys.argv) != 5 or sys.argv[1] != '--prepare':
            raise ValueError('이 파일은 한글패치 설치기에서 호출합니다.')
        prepare(*sys.argv[2:])
    except Exception:
        # A headless worker reports errors to its parent without opening any window.
        if len(sys.argv) == 5:
            try:
                root = Path(sys.argv[2]).resolve()
                if Path(sys.argv[3]).resolve() == root / '.comipa-update' / 'new':
                    (root / '.comipa-update' / 'worker-error.txt').write_text(traceback.format_exc(), encoding='utf-8')
            except Exception:
                pass
        sys.exit(1)
