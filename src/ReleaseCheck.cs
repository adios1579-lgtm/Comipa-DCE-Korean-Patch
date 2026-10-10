using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using ComipaUpdate;
class ReleaseCheck {
 static void Main(string[] args){
  string mode=args[0],root=Path.GetFullPath(args[1]);Action<string> log=s=>Console.WriteLine(s);
  if(mode=="install"){Engine.Install(root,log);Console.WriteLine("INSTALL_OK");return;}
  if(mode=="check"){
   Engine.CheckBase(root);var state=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates","installed.json"),Encoding.UTF8));Engine.CheckInstalled(root,state);
   var p=App.LaunchInfo(root);if(p.FileName!=Path.Combine(root,"ComiPaRT.exe")||p.WorkingDirectory!=root||p.Arguments!="")throw new Exception("게임 실행 경로 오류");
   Console.WriteLine("CHECK_OK "+state.version);return;
  }
  if(mode=="download"){
   var settings=Engine.Parse<Settings>(File.ReadAllText(Path.Combine(root,"updates","settings.json"),Encoding.UTF8));
   var m=Engine.Fetch(settings);var state=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates","installed.json"),Encoding.UTF8));Engine.Validate(m,state,root);
   string output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
   foreach(var package in m.packages){string path=Path.Combine(output,package.name);Engine.Download("https://github.com/"+settings.repository+"/releases/download/"+m.release_tag+"/"+package.name,path,package.size,log);if(new FileInfo(path).Length!=package.size||Engine.Hash(path)!=package.sha256)throw new Exception("공개 다운로드 불일치");}
   File.WriteAllText(Path.Combine(output,"fetched-manifest.json"),Engine.Json(m),new UTF8Encoding(false));Console.WriteLine("DOWNLOAD_OK "+m.version);return;
  }
  if(mode=="upgrade-fixture"){
   // A test-only predecessor uses real older game bytes and their actual hashes.
   // The published release stays 1.0.0; only this local manifest advances for the test.
   var m=Engine.Parse<Manifest>(File.ReadAllText(args[2],Encoding.UTF8));m.version="1.1.0";m.release_tag="v1.1.0";
   var local=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates","installed.json"),Encoding.UTF8));
   foreach(var f in local.files)if(f.path=="event.ar1"||f.path=="updates/ComipaUpdater.dll"){string file=Path.Combine(root,f.path);f.size=new FileInfo(file).Length;f.sha256=Engine.Hash(file);}
   File.WriteAllText(Path.Combine(root,"updates","installed.json"),Engine.Json(local),new UTF8Encoding(false));
   string packageFolder=Path.GetFullPath(args[3]);int downloads=0;
   Engine.Apply(root,m,local,(p,d)=>{downloads++;File.Copy(Path.Combine(packageFolder,p.name),d);},log);
   var state=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates","installed.json"),Encoding.UTF8));Engine.CheckInstalled(root,state);
   Engine.Apply(root,m,state,(p,d)=>{throw new Exception("동일 버전 재다운로드");},log);
   if(downloads!=1)throw new Exception("필요한 패키지 개수 불일치");
   Console.WriteLine("UPGRADE_FIXTURE_OK");return;
  }
  throw new Exception("알 수 없는 검증 모드");
 }
}
