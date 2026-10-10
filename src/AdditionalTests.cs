using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using ComipaUpdate;
class AdditionalTests {
 static int count;
 static void Check(bool yes,string label){if(!yes)throw new Exception(label);count++;Console.WriteLine("추가 통과: "+label);}
 static void Reject(Action f,string label){bool rejected=false;try{f();}catch{rejected=true;}Check(rejected,label);}
 static string Put(string root,string name,string text){var path=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text,new UTF8Encoding(false));return path;}
 static Item Entry(string root,string name){var p=Path.Combine(root,name);return new Item{path=name,size=new FileInfo(p).Length,sha256=Engine.Hash(p)};}
 public static void Run(string parent){
  string root=Path.Combine(parent,"추가 시험");Directory.CreateDirectory(root);
  string target=Put(root,"사용설명서.txt","old");var before=Entry(root,"사용설명서.txt");
  var state=new State{schema=2,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{before}};
  Put(root,"updates/installed.json",Engine.Json(state));
  string changed=Put(root,"fixtures/new","new");var next=new Item{path=before.path,size=3,sha256=Engine.Hash(changed),package="comipa-update-1.1.0-001.zip"};
  var pack=new Package{name=next.package};
  var m=new Manifest{schema=2,app_id=Engine.AppId,version="1.1.0",base_version="1.0.0",release_tag="v1.1.0",files=new List<Item>{next},inventory=new List<Item>{next},packages=new List<Package>{pack},deleted=new List<string>()};
  string zip=Path.Combine(root,"fixture.zip");
  Action<string[]> make=names=>{if(File.Exists(zip))File.Delete(zip);using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var name in names)z.CreateEntryFromFile(changed,name);pack.sha256=Engine.Hash(zip);pack.size=new FileInfo(zip).Length;};
  Action apply=()=>Engine.Apply(root,m,state,(p,d)=>File.Copy(zip,d),s=>{});
  make(new[]{"사용설명서.txt","updates/PatchEngine.exe"});Reject(apply,"목록 밖 ZIP 항목 차단");
  make(new[]{"사용설명서.txt","사용설명서.txt"});Reject(apply,"ZIP 중복 항목 차단");
  make(new string[0]);Reject(apply,"ZIP 항목 누락 차단");
  make(new[]{"사용설명서.txt"});
  m.app_id="other-game";Reject(apply,"다른 게임 app_id 차단");m.app_id=Engine.AppId;
  m.release_tag="v9.9.9";Reject(apply,"태그 불일치 차단");m.release_tag="v1.1.0";
  m.inventory.Add(new Item{path="사용설명서.TXT",size=3,sha256=next.sha256});Reject(apply,"대소문자 중복 차단");m.inventory.RemoveAt(1);
  m.deleted.Add("ComiPaRT.exe");Reject(apply,"필수 게임 파일 삭제 차단");m.deleted.Clear();
  next.generated=true;Reject(apply,"생성 파일 분류 위조 차단");next.generated=false;
  foreach(string n in new[]{"ComiPaRT.exe.story_orig","ComiPaRT.exe.le.config","cpdata.100","cpdata.001","updates/installed.json","C:/escape","사용설명서.txt ","updates/../PatchEngine.exe"})Reject(()=>Engine.Safe(root,n),"원본·설정·해금 보호: "+n);
  bool changedDuringFetch=false;
  Reject(()=>Engine.Apply(root,m,state,(p,d)=>{File.Copy(zip,d);File.WriteAllText(target,"external");changedDuringFetch=true;},s=>{}),"다운로드 중 사용자 변경 감지");
  Check(changedDuringFetch&&File.ReadAllText(target)=="external","동시 변경된 파일 보존");Put(root,"사용설명서.txt","old");
  Check(!Directory.Exists(Path.Combine(root,".comipa-update")),"실패한 다운로드 임시 파일 정리");
  // A non-game sleeper named like the game verifies the process guard without launching the game.
  var start=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ComiPaRT.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
  File.Copy(start.FileName,Path.Combine(root,"ComiPaRT.exe"));start.FileName=Path.Combine(root,"ComiPaRT.exe");
  using(var process=Process.Start(start)){try{Check(Engine.Running(root),"동일 설치 폴더 실행 중 감지");Reject(apply,"게임 실행 중 교체 거부");}finally{if(!process.HasExited)process.Kill();process.WaitForExit();}}
  string lockName="Local\\ComipaUpdate-"+Engine.TextHash(Path.GetFullPath(root).TrimEnd('\\').ToLowerInvariant()).Substring(0,24);bool first,second;
  using(var a=new Mutex(true,lockName,out first))using(var b=new Mutex(false,lockName,out second))Check(first&&!second,"설치 폴더별 중복 실행 잠금");
  // Revert to the baseline after an intermediate version; source history must retain it.
  apply();var middle=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates/installed.json")));
  m.version="1.2.0";m.release_tag="v1.2.0";Put(root,"fixtures/new","old");next.sha256=before.sha256;make(new[]{"사용설명서.txt"});Engine.Apply(root,m,middle,(p,d)=>File.Copy(zip,d),s=>{});
  Check(Engine.Hash(target)==before.sha256,"중간 버전에서 초기 내용으로 되돌리기");
  // Rollback is conservative if another program edits a file after an interrupted transaction.
  Put(root,".comipa-update/prior/사용설명서.txt","old");Put(root,".comipa-update/transaction.json",Engine.Json(new Journal{changes=new List<Change>{new Change{path=before.path,existed=true,before=before.sha256,after=Engine.TextHash("new").ToLowerInvariant()}}}));Put(root,"사용설명서.txt","user");
  Reject(()=>Engine.Recover(root),"중단 이후 개별 수정이 있으면 복구 덮어쓰기 차단");Check(File.ReadAllText(target)=="user"&&File.Exists(Path.Combine(root,".comipa-update/transaction.json")),"복구 실패 시 수정 파일과 복구 자료 보존");
  Put(root,"사용설명서.txt","new");Engine.Recover(root);Check(Engine.Hash(target)==before.sha256,"정상 복구 재시도");
  Put(root,".comipa-update/transaction.json",Engine.Json(new Journal{committed=true,changes=new List<Change>{new Change{path=before.path,existed=true,before=before.sha256,after=before.sha256}}}));Engine.Recover(root);Check(!Directory.Exists(Path.Combine(root,".comipa-update")),"완료 후 정리 중 종료돼 이전본이 없어도 정리 재개");
  string junctionRoot=Path.Combine(parent,"junction-check"),outside=Path.Combine(parent,"junction-target");Directory.CreateDirectory(junctionRoot);Directory.CreateDirectory(outside);string junction=Path.Combine(junctionRoot,"updates");
  var linkProcess=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"New-Item -ItemType Junction -Path '"+junction.Replace("'","''")+"' -Target '"+outside.Replace("'","''")+"' | Out-Null\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
  using(var process=Process.Start(linkProcess)){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("정션 검사 준비 실패: "+process.StandardError.ReadToEnd());}
  try{Reject(()=>Engine.Safe(junctionRoot,"updates/PatchEngine.exe"),"설치 대상 정션 경로 차단");}finally{Directory.Delete(junction);}
  Check(Engine.DisplayVersion("1.0.0")=="1.00"&&Engine.Ver("1.0.10")>Engine.Ver("1.0.9"),"1.00 표시 및 숫자 버전 비교");
  Console.WriteLine("추가 검사 "+count+"개 통과");
 }
}
