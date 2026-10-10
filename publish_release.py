"""Publish a validated patch release with the local GitHub CLI credentials."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import build_release as b

GH=Path(os.environ.get('COMIPA_GH',r'C:\Program Files\GitHub CLI\gh.exe'))

def command(*args):
    p=subprocess.run([str(a) for a in args],cwd=b.DIST,env=dict(os.environ,GIT_TERMINAL_PROMPT='0'),capture_output=True,text=True,encoding='utf-8')
    if p.returncode:raise RuntimeError(p.stderr.strip() or p.stdout.strip())
    return p.stdout.strip()

def publish(version):
    release=b.DIST/'releases'/version
    sums=b.load(release/'SHA256.json')
    for name,info in sums.items():
        p=release/name
        if b.sha(p)!=info['sha256'] or p.stat().st_size!=info['size']:raise ValueError('배포 파일 변경 감지: '+name)
    for mode in ['install','upgrade']:
        result=b.load(b.ROOT/'logs'/f'release_{mode}_100.json')
        if result['version']!=version or not all(result[k] for k in ['all_managed_files_match','live_unchanged','saves_and_settings_unchanged','temporary_files_clean']):raise ValueError('검증 미완료: '+mode)
    result=b.load(b.ROOT/'logs/release_upgrade_100.json')
    if result.get('manifest_sha256')!=b.sha(release/'update-manifest.json'):raise ValueError('검증 이후 manifest 변경')
    expected=b.signature([b.DIST/'src/Updater.cs',b.DIST/'src/Bootstrap.cs',b.PATCH/'ComiPaRT.ico'])
    if b.load(b.BUILD/'tools-signature.json')!=expected:raise ValueError('업데이트 엔진 소스 변경 후 재빌드 필요')
    account=json.loads(command(GH,'api','user','--jq','{login,id}'))
    if account['login']!=b.REPOSITORY.split('/')[0]:raise ValueError('GitHub 계정 불일치')
    if not (b.DIST/'.git').exists():command('git','init','-b','main')
    command('git','config','user.name',account['login'])
    command('git','config','core.quotepath','false')
    command('git','config','user.email',str(account['id'])+'+'+account['login']+'@users.noreply.github.com')
    command('git','config','credential.https://github.com.helper','')
    command('git','config','--add','credential.https://github.com.helper','!"'+str(GH).replace('\\','/')+'" auth git-credential')
    allowed=['.gitignore','README.md','release-notes.md','배포자_사용설명.md','사용설명서.txt','업데이트_만들기.bat','build_release.py','validate_release.py','publish_release.py','verify_public.py','src','history','baseline.json','VERIFICATION.md']
    for name in allowed:
        if (b.DIST/name).exists():command('git','add','--',name)
    staged=command('git','diff','--cached','--name-only').splitlines()
    for name in staged:
        if not (name in allowed or name.startswith(('src/','history/'))):raise ValueError('허용하지 않은 공개 파일: '+name)
        if Path(name).suffix.lower() in ['.exe','.dll','.ar1','.zip','.7z']:raise ValueError('소스 커밋에 바이너리 포함')
    if staged:command('git','commit','-m','Release ComiPa DCE Korean patch 1.00 with automatic updates')
    remote=subprocess.run([str(GH),'repo','view',b.REPOSITORY,'--json','nameWithOwner'],capture_output=True,text=True)
    if remote.returncode:
        command(GH,'repo','create',b.REPOSITORY,'--public','--description','코믹파티 DCE 스토리 모드 한글패치 · CG 한글화 · 자동 업데이트','--source',b.DIST,'--remote','origin','--push')
    else:
        remotes=command('git','remote').splitlines()
        if 'origin' not in remotes:command('git','remote','add','origin','https://github.com/'+b.REPOSITORY+'.git')
        if command('git','remote','get-url','origin')!='https://github.com/'+b.REPOSITORY+'.git':raise ValueError('Git 원격 주소 불일치')
        command('git','push','-u','origin','main')
    tag='v'+version
    existing=subprocess.run([str(GH),'release','view',tag,'--repo',b.REPOSITORY,'--json','isDraft,url'],capture_output=True,text=True,encoding='utf-8')
    if existing.returncode==0:
        state=json.loads(existing.stdout)
        if not state['isDraft']:raise ValueError('이미 공개된 버전입니다. 덮어쓰지 않습니다.')
        # A resumed draft is safe only when no uploads need replacement.
        listed=json.loads(command(GH,'release','view',tag,'--repo',b.REPOSITORY,'--json','assets'))['assets']
        have={a['name'] for a in listed}
        for path in [*(release/name for name in sums),release/'SHA256.json']:
            if path.name not in have:command(GH,'release','upload',tag,path,'--repo',b.REPOSITORY)
    else:
        command(GH,'release','create',tag,*[release/name for name in sums],release/'SHA256.json','--repo',b.REPOSITORY,'--draft','--target','main','--title','코믹파티 DCE 한글패치 1.00','--notes-file',b.DIST/'release-notes.md')
    draft=json.loads(command(GH,'release','view',tag,'--repo',b.REPOSITORY,'--json','assets,isDraft,url'))
    wanted=dict(sums,**{'SHA256.json':{'size':(release/'SHA256.json').stat().st_size,'sha256':b.sha(release/'SHA256.json')}})
    if set(a['name'] for a in draft['assets'])!=set(wanted):raise ValueError('초안 자산 목록 불일치')
    for a in draft['assets']:
        if a['size']!=wanted[a['name']]['size']:raise ValueError('초안 크기 불일치')
        digest=a.get('digest')
        if digest and digest!='sha256:'+wanted[a['name']]['sha256']:raise ValueError('초안 해시 불일치')
        if not digest:
            endpoint=a.get('apiUrl') or f'https://api.github.com/repos/{b.REPOSITORY}/releases/assets/{a["id"]}'
            if not endpoint.startswith(f'https://api.github.com/repos/{b.REPOSITORY}/releases/assets/'):raise ValueError('릴리스 자산 API 주소 불일치')
            process=subprocess.Popen([str(GH),'api',endpoint,'-H','Accept: application/octet-stream'],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            h=hashlib.sha256();size=0
            while chunk:=process.stdout.read(1024*1024):h.update(chunk);size+=len(chunk)
            error=process.stderr.read().decode('utf-8','replace');rc=process.wait()
            if rc or size!=wanted[a['name']]['size'] or h.hexdigest()!=wanted[a['name']]['sha256']:raise ValueError('초안 다운로드 대조 실패: '+error)
    command(GH,'release','edit',tag,'--repo',b.REPOSITORY,'--draft=false','--latest')
    published=json.loads(command(GH,'release','view',tag,'--repo',b.REPOSITORY,'--json','url,isDraft'))
    if published['isDraft']:raise ValueError('공개 전환 확인 실패')
    record=dict(version=version,display_version='1.00',repository=b.REPOSITORY,tag=tag,url=published['url'],assets=wanted,commit=command('git','rev-parse','HEAD'))
    b.dump(b.DIST/'published'/f'{version}.json',record)
    b.dump(b.DIST/'publication.json',record)
    print(json.dumps(record,ensure_ascii=False,indent=2))

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--version',default='1.0.0');publish(ap.parse_args().version)
