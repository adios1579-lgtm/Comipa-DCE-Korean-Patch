using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using ComipaUpdate;
class UpdaterTests {
 static int passed=0;
 static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("통과: "+name);}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
 static string Write(string root,string path,string data){string p=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,data,new UTF8Encoding(false));return p;}
 static void Main(){string root=Path.Combine(Path.GetTempPath(),"코믹파티 업데이트 시험 "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   string a=Write(root,"사용설명서.txt","이전 번역"),keep=Write(root,"cpdata.000","사용자 세이브");
   var old=new State{schema=2,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{new Item{path="사용설명서.txt",size=new FileInfo(a).Length,sha256=Engine.Hash(a)}}};
   Write(root,"updates/installed.json",Engine.Json(old));
   string zip=Path.Combine(root,"fixture.zip"),stage=Write(root,"fixture/new.txt","수정 번역");
   using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))z.CreateEntryFromFile(stage,"사용설명서.txt");
   var item=new Item{path="사용설명서.txt",size=new FileInfo(stage).Length,sha256=Engine.Hash(stage),package="comipa-update-1.1.0-001.zip"};
   var m=new Manifest{schema=2,app_id=Engine.AppId,version="1.1.0",base_version="1.0.0",release_tag="v1.1.0",notes="시험",files=new List<Item>{item},inventory=new List<Item>{item},deleted=new List<string>(),packages=new List<Package>{new Package{name=item.package,size=new FileInfo(zip).Length,sha256=Engine.Hash(zip)}}};
   Action<Package,string> fetch=(p,d)=>File.Copy(zip,d);Action<string> quiet=s=>{};
   var original=File.ReadAllBytes(a);var saved=Engine.Hash(keep);
   Reject(()=>Engine.Apply(root,m,old,fetch,quiet,1),"교체 도중 오류 감지");
   Check(File.ReadAllBytes(a).SequenceEqual(original)&&Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates/installed.json"))).version=="1.0.0","실패 후 본문과 버전 복구");
   Engine.Apply(root,m,old,fetch,quiet);Check(Engine.Hash(a)==item.sha256,"한글·공백 경로 업데이트 성공");Check(Engine.Hash(keep)==saved,"사용자 세이브 보존");
   var now=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates/installed.json")));int calls=0;
   Engine.Apply(root,m,now,(p,d)=>{calls++;},quiet);Check(calls==0,"같은 버전 중복 다운로드 없음");
   foreach(string path in new[]{"../escape.dll","DAT/../../escape.dll","cpdata.000","사용설명서.txt:stream","DAT/CON.MES","/absolute.MES","사용설명서.txt.","device/../NSL.DLL","updates/settings.json"})Reject(()=>Engine.Safe(root,path),"보호 경로 차단 "+path);
   string oldPath=item.path;item.path="cpdata.000";Reject(()=>Engine.Validate(m,old,root),"원격 목록의 세이브 변경 차단");item.path=oldPath;
   m.files.Add(item);Reject(()=>Engine.Validate(m,old,root),"중복 ID/파일 경로 차단");m.files.RemoveAt(1);
   m.base_version="2.0.0";Reject(()=>Engine.Validate(m,old,root),"다른 배포 기준 버전 차단");m.base_version="1.0.0";
   File.WriteAllBytes(a,original);Write(root,"updates/installed.json",Engine.Json(old));
   string hash=m.packages[0].sha256;m.packages[0].sha256=new string('0',64);Reject(()=>Engine.Apply(root,m,old,fetch,quiet),"손상 다운로드 해시 차단");Check(File.ReadAllBytes(a).SequenceEqual(original),"손상 응답은 게임 미변경");m.packages[0].sha256=hash;
   File.WriteAllText(a,"사용자 개별 수정");Reject(()=>Engine.Apply(root,m,old,fetch,quiet),"설치 후 개별 수정 보존");File.WriteAllBytes(a,original);
   // 강제 종료를 모사: 저널과 복구본은 있고 대상만 교체된 상태.
   Write(root,".comipa-update/prior/사용설명서.txt","이전 번역");Write(root,".comipa-update/transaction.json",Engine.Json(new Journal{committed=false,changes=new List<Change>{new Change{path="사용설명서.txt",existed=true,before=Engine.TextHash("이전 번역").ToLowerInvariant(),after=Engine.TextHash("중간 교체").ToLowerInvariant()}}}));
   File.WriteAllText(a,"중간 교체");Engine.Recover(root);Check(File.ReadAllBytes(a).SequenceEqual(original),"재실행 시 중단된 교체 복구");
   // 0개 변경/삭제 없는 초기 릴리스도 정상 정보이다.
   m.files.Clear();m.packages.Clear();Reject(()=>Engine.Validate(m,old,root),"변경 파일 누락을 버전 완료로 처리하지 않음");
   m.inventory=old.files;Engine.Validate(m,old,root);Check(true,"빈 변경 목록 허용");
   m.version="1.2.0";m.release_tag="v1.2.0";
   Reject(()=>Engine.Validate(m,now,root),"예전 상태로 되돌리는 업데이트에도 실제 파일 요구");
   foreach(var status in new[]{WebExceptionStatus.ConnectFailure,WebExceptionStatus.NameResolutionFailure,WebExceptionStatus.Timeout,WebExceptionStatus.ProtocolError}) {
    int launch=0,notice=0;var answer=App.CheckRemote(()=>{throw new WebException("연결 실패 모의",status);},false,()=>launch++,e=>notice++);
    Check(answer==null&&launch==1&&notice==1,"연결 실패 시 현재 게임 실행 경로: "+status);
   }
   int manualLaunch=0;App.CheckRemote(()=>{throw new WebException("오프라인");},true,()=>manualLaunch++,e=>{});Check(manualLaunch==0,"수동 업데이트 실패는 게임을 임의 실행하지 않음");
   var baseFile=new Item{path="chip.ar1",size=5,sha256=Engine.TextHash("dummy").ToLowerInvariant()};
   Write(root,"updates/base-requirements.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile}}));
   Reject(()=>Engine.CheckBase(root),"본편 파일 없는 빈 폴더 감지");
   Write(root,"chip.ar1","dummy");Engine.CheckBase(root);Check(true,"본편 로컬 파일 확인 — 외부 통신 없음");
   Engine.CheckBase(root);Check(File.Exists(Path.Combine(root,"updates/base-check.json")),"본편 확인 캐시 재사용");
   Reject(()=>Engine.Safe(root,"chip.ar1"),"자동 업데이트로 본편 원본 배포 금지");
   Write(root,"chip.ar1","different");Reject(()=>Engine.CheckBase(root),"본편 파일 변경 감지");
   // 기존 원본 엔진에 나중에 패치가 추가되는 경우도 사용자 수정과 구별한다.
   Write(root,"chip.ar1","dummy");string core=Write(root,"updates/ComipaUpdater.dll","original-core");
   var coreOld=new Item{path="updates/ComipaUpdater.dll",size=new FileInfo(core).Length,sha256=Engine.Hash(core)};
   string req=Write(root,"updates/base-requirements.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile,coreOld}}));
   var coreState=new State{schema=2,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{new Item{path="updates/base-requirements.json",size=new FileInfo(req).Length,sha256=Engine.Hash(req)}}};
   Write(root,"updates/installed.json",Engine.Json(coreState));
   string coreNew=Write(root,"fixture/core.dll","patched-core"),reqNew=Write(root,"fixture/req.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile}}));
   string coreZip=Path.Combine(root,"core.zip");
   using(var z=ZipFile.Open(coreZip,ZipArchiveMode.Create)){z.CreateEntryFromFile(coreNew,"updates/ComipaUpdater.dll");z.CreateEntryFromFile(reqNew,"updates/base-requirements.json");}
   var coreItems=new List<Item>{new Item{path="updates/ComipaUpdater.dll",size=new FileInfo(coreNew).Length,sha256=Engine.Hash(coreNew),package="comipa-update-core.zip"},new Item{path="updates/base-requirements.json",size=new FileInfo(reqNew).Length,sha256=Engine.Hash(reqNew),package="comipa-update-core.zip"}};
   var coreManifest=new Manifest{schema=2,app_id=Engine.AppId,version="1.0.1",base_version="1.0.0",release_tag="v1.0.1",files=coreItems,inventory=coreItems,deleted=new List<string>(),packages=new List<Package>{new Package{name="comipa-update-core.zip",size=new FileInfo(coreZip).Length,sha256=Engine.Hash(coreZip)}}};
   Engine.Apply(root,coreManifest,coreState,(p,d)=>File.Copy(coreZip,d),quiet);Engine.CheckBase(root);
   Check(Engine.Hash(core)==Engine.Hash(coreNew),"원본 엔진에 후속 패치 추가 및 본편 조건 갱신");
   AdditionalTests.Run(root);
   Console.WriteLine("격리 업데이트 테스트 "+passed+"개 통과. 실제 GitHub 연결/게임 실행 시험 아님.");
  } finally { if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true); }
 }
}
