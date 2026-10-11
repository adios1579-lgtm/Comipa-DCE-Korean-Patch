"""Build only the ComiPa DCE patch, never distribute original game archives."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

DIST = Path(__file__).resolve().parent
ROOT = DIST.parent
PATCH = ROOT / 'patch'
BUILD = DIST / 'build'
PAYLOAD = BUILD / 'payload'
CSC = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
REPOSITORY = 'adios1579-lgtm/Comipa-DCE-Korean-Patch'
APP_ID = 'comipa-dce-ko'
BASE_VERSION = '1.0.0'
GAME = ['ComiPaRT.exe','script.ar1','EventFile.TBL','bg.ar1','bustup.ar1','event.ar1','frame.ar1','voice.ar1']
BASE_FILES = ['ALWAYS.EVS','C_MOVE.EVS','COMIPA.EVS','H_MOVE.EVS','H_TEL.EVS','EventLable.TBL','JIS.TBL','chip.ar1','se.ar1','bgm.dat','cmpeach1.v','cmpeach2.v','cmpeach3.v','cmpeach4.v','logo.v','opening.v']
RUNTIME = ['updates/PatchEngine.exe','updates/ComipaUpdater.dll','updates/base-requirements.json','updates/originals.json','사용설명서.txt']
BOOTSTRAPS = ['코믹파티_DCE_한글판.exe','한글패치_업데이트.exe','한글패치_설치.exe']
REFS = ['System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll']

def display_version(version):
    major, minor, patch = version.split('.')
    return major+'.'+minor.zfill(2)+('.'+patch if patch!='0' else '')

def verification_path(version, mode):
    return ROOT/'logs'/('release_'+mode+'_'+version.replace('.', '_')+'.json')

def sha(path):
    with open(path,'rb') as f:
        return hashlib.file_digest(f,'sha256').hexdigest()

def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def item(path, rel, generated=False):
    return dict(path=rel,size=path.stat().st_size,sha256=sha(path),generated=generated)

def run(args, **kwargs):
    subprocess.run([str(a) for a in args],check=True,**kwargs)

def signature(paths):
    return hashlib.sha256(json.dumps([(p.name,sha(p)) for p in paths]).encode()).hexdigest()

def compile_tools():
    BUILD.mkdir(exist_ok=True)
    (PAYLOAD/'updates').mkdir(parents=True,exist_ok=True)
    source = DIST/'src'
    sources = [source/'Updater.cs',source/'Bootstrap.cs',PATCH/'ComiPaRT.ico']
    marker=BUILD/'tools-signature.json'
    stamp=signature(sources)
    if not marker.exists() or load(marker)!=stamp or not (PAYLOAD/'updates/ComipaUpdater.dll').exists() or not all((PAYLOAD/n).exists() for n in BOOTSTRAPS):
        run([CSC,'/nologo','/target:library','/out:'+str(PAYLOAD/'updates/ComipaUpdater.dll'),*['/r:'+r for r in REFS],source/'Updater.cs'])
        exe=PAYLOAD/BOOTSTRAPS[0]
        run([CSC,'/nologo','/target:winexe','/platform:x86','/out:'+str(exe),'/win32icon:'+str(PATCH/'ComiPaRT.ico'),'/r:System.Windows.Forms.dll',source/'Bootstrap.cs'])
        for name in BOOTSTRAPS[1:]:shutil.copyfile(exe,PAYLOAD/name)
        dump(marker,stamp)
    data=[PATCH/n for n in ['story_mode_patch.json','story_mode_images.json','story_mode_voices.json']]
    workerSources=[source/'patch_worker.py',PATCH/'apply_story_mode.py',PATCH/'pc_afterstory.py',*data,PATCH/'ComiPaRT.ico']
    marker=BUILD/'worker-signature.json';stamp=signature(workerSources)
    if not marker.exists() or load(marker)!=stamp or not (PAYLOAD/'updates/PatchEngine.exe').exists():
        env=dict(os.environ)
        dependency=Path(os.environ.get('COMIPA_PYINSTALLER',ROOT.parent/'pyinstaller_dep'))
        if dependency.exists():env['PYTHONPATH']=str(dependency)
        args=[sys.executable,'-m','PyInstaller','--noconfirm','--clean','--onefile','--windowed','--noupx','--name','PatchEngine','--icon',PATCH/'ComiPaRT.ico','--paths',PATCH,'--hidden-import','apply_story_mode','--hidden-import','pc_afterstory','--distpath',PAYLOAD/'updates','--workpath',BUILD/'pyinstaller','--specpath',BUILD]
        for p in data:args.extend(['--add-data',str(p)+os.pathsep+'.'])
        args.append(source/'patch_worker.py')
        run(args,env=env)
        dump(marker,stamp)

def target_metadata(original):
    sources=[PATCH/n for n in ['story_mode_patch.json','story_mode_images.json','story_mode_voices.json','apply_story_mode.py','pc_afterstory.py']]
    stamp=signature(sources);cache=BUILD/'target-inventory.json'
    if cache.exists() and load(cache)['signature']==stamp:return load(cache)
    sys.path.insert(0,str(PATCH))
    import apply_story_mode as patch
    p=patch.load_patch();p['_images']=patch.load_images();p['_voices']=patch.load_voices()
    entries=[];originals=[]
    for name,digest,fn in patch.targets(p):
        path=original/name
        if sha(path)!=digest.lower():raise ValueError('지원 원본 해시 불일치: '+name)
        print('원본 재빌드 검증: '+name,flush=True)
        data=fn(path.read_bytes(),p,str(original))
        entries.append(dict(path=name,size=len(data),sha256=hashlib.sha256(data).hexdigest(),generated=True))
        originals.append(item(path,name))
        del data
    result=dict(signature=stamp,inventory=entries,originals=originals)
    dump(cache,result);return result

def verify_zip(path, expected):
    with zipfile.ZipFile(path) as z:
        if len(z.namelist())!=len(set(n.casefold() for n in z.namelist())) or set(z.namelist())!=set(expected):raise ValueError('ZIP 파일 목록 불일치')
        for n in z.namelist():
            with z.open(n) as f:
                h=hashlib.file_digest(f,'sha256').hexdigest()
            if h!=expected[n]['sha256'] or z.getinfo(n).file_size!=expected[n]['size']:raise ValueError('ZIP 내용 불일치: '+n)

def build(version, notes, original):
    from re import fullmatch
    if not fullmatch(r'\d+\.\d+\.\d+',version):raise ValueError('내부 버전은 x.y.z')
    if (DIST/'published'/f'{version}.json').exists():raise ValueError('이미 게시한 버전입니다. 새 번호로 배포해 주세요.')
    compile_tools();metadata=target_metadata(original)
    requirements=dict(schema=1,app_id=APP_ID,files=[item(original/n,n) for n in BASE_FILES])
    dump(PAYLOAD/'updates/base-requirements.json',requirements)
    dump(PAYLOAD/'updates/originals.json',dict(schema=1,app_id=APP_ID,files=metadata['originals']))
    dump(PAYLOAD/'updates/settings.json',dict(app_id=APP_ID,repository=REPOSITORY))
    shutil.copyfile(DIST/'사용설명서.txt',PAYLOAD/'사용설명서.txt')
    runtime=[item(PAYLOAD/n,n) for n in RUNTIME]
    inventory=metadata['inventory']+runtime
    old=[]
    for p in (DIST/'history').glob('*.json'):
        if p.stem!=version:old.append(load(p)['inventory'])
    names={i['path'] for i in inventory}
    deleted=sorted({i['path'] for previous in old for i in previous}-names)
    generated=[dict(f) for f in metadata['inventory'] if not old or any(not any(w['path']==f['path'] and w['sha256']==f['sha256'] and w['size']==f['size'] for w in previous) for previous in old)]
    out=DIST/'releases'/version;out.mkdir(parents=True,exist_ok=True)
    package=f'comipa-update-{version}-001.zip'
    with zipfile.ZipFile(out/package,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for f in runtime:z.write(PAYLOAD/f['path'],f['path'])
    verify_zip(out/package,{f['path']:f for f in runtime})
    if (out/package).stat().st_size>=2*1024**3:raise ValueError('릴리스 파일 2GiB 초과')
    packages=[dict(name=package,size=(out/package).stat().st_size,sha256=sha(out/package))]
    files=generated+[dict(f,package=package) for f in runtime]
    manifest=dict(schema=2,app_id=APP_ID,version=version,base_version=BASE_VERSION,release_tag='v'+version,notes=notes,files=files,inventory=inventory,deleted=deleted,packages=packages)
    dump(out/'update-manifest.json',manifest)
    initial=dict(manifest,files=[dict(f) for f in metadata['inventory']]+[dict(f,package=package) for f in runtime],deleted=[])
    dump(PAYLOAD/'updates/initial-manifest.json',initial)
    display=display_version(version)
    full=out/f'Comipa_DCE_Korean_Patch_{display}.zip'
    included=BOOTSTRAPS+RUNTIME+['updates/initial-manifest.json','updates/settings.json']
    with zipfile.ZipFile(full,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for n in included:z.write(PAYLOAD/n,n)
    verify_zip(full,{n:item(PAYLOAD/n,n) for n in included})
    hist=dict(version=version,base_version=BASE_VERSION,inventory=inventory)
    prior=DIST/'history'/f'{version}.json'
    if prior.exists() and load(prior)!=hist and (DIST/'publication.json').exists():
        published=load(DIST/'publication.json')
        if published.get('version')==version:raise ValueError('게시한 버전 변경 금지: 새 버전 번호 필요')
    dump(prior,hist)
    if version==BASE_VERSION:dump(DIST/'baseline.json',hist)
    sums={p.name:dict(size=p.stat().st_size,sha256=sha(p)) for p in [full,out/package,out/'update-manifest.json']}
    dump(out/'SHA256.json',sums)
    print(json.dumps(dict(version=version,display_version=display,repository=REPOSITORY,assets=sums),ensure_ascii=False,indent=2),flush=True)

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--version',required=True);ap.add_argument('--notes',required=True);ap.add_argument('--original',type=Path,default=ROOT/'testgame/vanilla');args=ap.parse_args()
    build(args.version,args.notes,args.original)
