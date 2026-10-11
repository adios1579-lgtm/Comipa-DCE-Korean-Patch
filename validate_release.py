"""Exercise the shipped DLL/EXE against the existing disposable test installation."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile
import build_release as b

ROOT=b.ROOT
GAME=ROOT/'testgame/patched'
LIVE=Path('D:/AQUAPLUS/ComiPaDCE')

def protected(folder):
    return {p.name:b.sha(p) for p in folder.iterdir() if p.is_file() and (p.name.startswith('cpdata.') or p.suffix.lower() in ['.ini','.cfg','.config'])}

def run_check(*args):
    b.run([b.BUILD/'ReleaseCheck.exe',*args])

def setup_tools():
    b.run([b.CSC,'/nologo','/out:'+str(b.BUILD/'ReleaseCheck.exe'),'/r:'+str(b.PAYLOAD/'updates/ComipaUpdater.dll'),*[('/r:'+r) for r in b.REFS],b.DIST/'src/ReleaseCheck.cs'])
    shutil.copyfile(b.PAYLOAD/'updates/ComipaUpdater.dll',b.BUILD/'ComipaUpdater.dll')

def validate(version, mode, packages):
    assert GAME.resolve()==(ROOT/'testgame/patched').resolve()
    setup_tools()
    release=b.DIST/'releases'/version
    manifest=b.load(release/'update-manifest.json')
    live={p:b.sha(LIVE/p) for p in b.GAME}
    saves={'live':protected(LIVE),'test':protected(GAME)}
    report=dict(version=version,mode=mode,manifest_sha256=b.sha(release/'update-manifest.json'),engine_sha256=b.sha(b.PAYLOAD/'updates/ComipaUpdater.dll'),actual_game_run=False,protected_before=saves,live_before=live)
    if mode=='install':
        run_check('check',GAME)
        # Reuse this test installation. Hardlinks avoid copying unmodified game data.
        for name in b.GAME:
            original=ROOT/'testgame/vanilla'/name
            temp=GAME/(name+'.release-test-link')
            if temp.exists():raise ValueError('남아 있는 시험 준비 파일')
            os.link(original,temp);os.replace(temp,GAME/name)
        full=next(release.glob('Comipa_DCE_Korean_Patch_*.zip'))
        allowed=set(b.BOOTSTRAPS+b.RUNTIME+['updates/initial-manifest.json','updates/settings.json'])
        with zipfile.ZipFile(full) as z:
            if set(z.namelist())!=allowed:raise ValueError('배포 ZIP 허용 목록 불일치')
            for name in z.namelist():
                dest=GAME/name
                dest.parent.mkdir(parents=True,exist_ok=True)
                with z.open(name) as source,open(dest,'wb') as target:shutil.copyfileobj(source,target)
        run_check('install',GAME)
        run_check('check',GAME)
        # The already-installed migration is idempotent, including pre-updater 0.80 content.
        run_check('install',GAME)
        run_check('check',GAME)
        report['clean_install_and_same_content_registration']=True
    elif mode=='upgrade':
        state=b.load(GAME/'updates/installed.json')
        if tuple(map(int,state['version'].split('.'))) >= tuple(map(int,version.split('.'))):
            raise ValueError('업데이트 검사는 실제 이전 버전의 시험본에서 시작해야 합니다.')
        run_check('check',GAME)
        report['predecessor_version']=state['version']
        report['predecessor_files']={f['path']:b.sha(GAME/f['path']) for f in state['files']}
        run_check('upgrade',GAME,release/'update-manifest.json',packages or release)
        report['published_predecessor_upgrade']=True
    for f in manifest['inventory']:
        path=GAME/f['path']
        if b.sha(path)!=f['sha256'] or path.stat().st_size!=f['size']:raise ValueError('최종 파일 불일치: '+f['path'])
    assert live=={p:b.sha(LIVE/p) for p in b.GAME},'실제 게임 변경 감지'
    assert saves=={'live':protected(LIVE),'test':protected(GAME)},'세이브/개인 설정 변경 감지'
    assert not (GAME/'.comipa-update').exists(),'임시 파일 정리 실패'
    report.update(all_managed_files_match=True,live_unchanged=True,saves_and_settings_unchanged=True,temporary_files_clean=True)
    b.dump(b.verification_path(version,mode),report)
    print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('mode',choices=['install','upgrade']);ap.add_argument('--version',required=True);ap.add_argument('--packages',type=Path);a=ap.parse_args();validate(a.version,a.mode,a.packages)
