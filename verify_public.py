"""Verify the anonymous public release, streaming large assets without extra copies."""
import hashlib
import json
import subprocess
import tempfile
from pathlib import Path
import urllib.request
import build_release as b

def normalized(value):
    # JavaScriptSerializer materializes absent optional Item.package as null.
    if isinstance(value,dict):return {k:normalized(v) for k,v in value.items() if v is not None}
    if isinstance(value,list):return [normalized(v) for v in value]
    return value

def main():
    record=b.load(b.DIST/'publication.json')
    result={}
    for name,info in record['assets'].items():
        url=f'https://github.com/{b.REPOSITORY}/releases/download/{record["tag"]}/{name}'
        request=urllib.request.Request(url,headers={'User-Agent':'Comipa-Release-Verification/1','Accept-Encoding':'identity'})
        h=hashlib.sha256();size=0
        with urllib.request.urlopen(request,timeout=60) as response:
            while chunk:=response.read(1024*1024):h.update(chunk);size+=len(chunk)
        if size!=info['size'] or h.hexdigest()!=info['sha256']:raise ValueError('공개 파일 불일치: '+name)
        result[name]=dict(size=size,sha256=h.hexdigest(),anonymous_download_matches=True)
        print('공개 다운로드 일치: '+name,flush=True)
    # The actual shipped engine fetches latest and downloads/verifies the ZIP.
    # Keep the required temporary download only for the duration of this check.
    with tempfile.TemporaryDirectory(prefix='public-verify-',dir=b.BUILD) as folder:
        output=Path(folder)
        if not output.resolve().is_relative_to(b.BUILD.resolve()):raise ValueError('검증 임시 경로 오류')
        b.run([b.BUILD/'ReleaseCheck.exe','download',b.ROOT/'testgame/patched',output])
        fetched=b.load(output/'fetched-manifest.json')
        if normalized(fetched)!=normalized(b.load(b.DIST/'releases'/record['version']/'update-manifest.json')):raise ValueError('latest manifest 불일치')
    record.update(public_assets_verified=result,latest_verified_by_shipped_engine=True)
    b.dump(b.DIST/'publication.json',record)
    b.dump(b.DIST/'published'/f'{record["version"]}.json',record)
    print('PUBLIC_VERIFIED '+record['url'],flush=True)

if __name__=='__main__':main()
