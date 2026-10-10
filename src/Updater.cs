using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ComipaUpdate {
 public class Settings { public string repository {get;set;} public string app_id {get;set;} }
 public class Item { public string path {get;set;} public string sha256 {get;set;} public long size {get;set;} public string package {get;set;} public bool generated {get;set;} }
 public class Package { public string name {get;set;} public string sha256 {get;set;} public long size {get;set;} }
 public class State { public int schema {get;set;} public string app_id {get;set;} public string version {get;set;} public string base_version {get;set;} public List<Item> files {get;set;} }
 public class Manifest { public int schema {get;set;} public string app_id {get;set;} public string version {get;set;} public string base_version {get;set;} public string release_tag {get;set;} public string notes {get;set;} public List<Item> files {get;set;} public List<Item> inventory {get;set;} public List<string> deleted {get;set;} public List<Package> packages {get;set;} }
 public class Change { public string path {get;set;} public bool existed {get;set;} public string before {get;set;} public string after {get;set;} }
 public class Journal { public bool committed {get;set;} public List<Change> changes {get;set;} }
 public class BaseInfo { public int schema {get;set;} public string app_id {get;set;} public List<Item> files {get;set;} }
 public class Stamp { public long size {get;set;} public long ticks {get;set;} }
 public class BaseCache { public string requirements {get;set;} public Dictionary<string,Stamp> files {get;set;} }

 public static class Engine {
  public const string AppId="comipa-dce-ko";
  public static readonly string[] GameFiles={"ComiPaRT.exe","script.ar1","EventFile.TBL","bg.ar1","bustup.ar1","event.ar1","frame.ar1","voice.ar1"};
  public static readonly string[] BaseFiles={"ALWAYS.EVS","C_MOVE.EVS","COMIPA.EVS","H_MOVE.EVS","H_TEL.EVS","EventLable.TBL","JIS.TBL","chip.ar1","se.ar1","bgm.dat","cmpeach1.v","cmpeach2.v","cmpeach3.v","cmpeach4.v","logo.v","opening.v"};
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLink(string name,string existing,IntPtr security);
  public static string DisplayVersion(string version) {var v=Ver(version);return v.Major+"."+v.Minor.ToString("00")+(v.Build==0?"":"."+v.Build);}
  public static void ValidateState(State state,string root) {
   if(state==null||state.schema!=2||state.app_id!=AppId||state.files==null)throw new Exception("설치 정보가 없거나 올바르지 않습니다. 한글패치 설치를 실행해 주세요.");
   Ver(state.version);Ver(state.base_version);
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in state.files){Safe(root,f.path);if(!seen.Add(f.path)||!Digest(f.sha256)||f.size<0)throw new Exception("설치 파일 목록 오류");}
  }
  public static void CheckInstalled(string root,State state) {
   ValidateState(state,root);
   foreach(string n in GameFiles.Concat(new[]{"updates/ComipaUpdater.dll","updates/PatchEngine.exe","updates/base-requirements.json","updates/originals.json"}))if(!state.files.Any(f=>f.path==n))throw new Exception("필수 설치 기록 누락: "+n);
   string cachePath=SafeInternal(root,"updates/installed-check.json");BaseCache cache=null;
   try{cache=Parse<BaseCache>(File.ReadAllText(cachePath));}catch{}
   string signature=TextHash(Json(state));var fresh=new BaseCache{requirements=signature,files=new Dictionary<string,Stamp>(StringComparer.OrdinalIgnoreCase)};
   foreach(var f in state.files){
    string path=Safe(root,f.path);if(!File.Exists(path))throw new Exception("설치 파일이 없습니다: "+f.path);
    var info=new FileInfo(path);Stamp prior=null;
    bool cached=cache!=null&&cache.requirements==signature&&cache.files!=null&&cache.files.TryGetValue(f.path,out prior)&&prior.size==info.Length&&prior.ticks==info.LastWriteTimeUtc.Ticks;
    if(info.Length!=f.size||(!cached&&Hash(path)!=f.sha256))throw new Exception("설치 파일이 변경됐거나 손상됐습니다. 파일을 보존하고 실행을 중단합니다: "+f.path);
    fresh.files[f.path]=new Stamp{size=info.Length,ticks=info.LastWriteTimeUtc.Ticks};
   }
   SaveJson(cachePath,fresh);
  }
  public static string SafeInternal(string root,string name) {
   // Check a fixed state-file path through the same ancestor/reparse checks.
   Safe(root,"updates/installed.json",true);
   var allowed=new[]{"updates/installed-check.json","updates/base-check.json","updates/initial-manifest.json","updates/settings.json","updates/last-check.txt"};
   if(!allowed.Contains(name))throw new Exception("내부 경로 오류");
   string path=Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar));
   if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("연결 상태 파일 거부");return path;
  }
  public static void EnsureOriginals(string root) {
   var originals=Parse<BaseInfo>(File.ReadAllText(Safe(root,"updates/originals.json"),Encoding.UTF8));
   if(originals.schema!=1||originals.app_id!=AppId||originals.files==null||originals.files.Count!=GameFiles.Length||originals.files.Select(f=>f.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=GameFiles.Length)throw new Exception("원본 기준 정보 오류");
   foreach(var f in originals.files){
    if(!GameFiles.Contains(f.path)||!Digest(f.sha256))throw new Exception("원본 기준 목록 오류");
    string path=Safe(root,f.path),original=path+".story_orig";
    if(File.Exists(original)) {if((File.GetAttributes(original)&FileAttributes.ReparsePoint)!=0||Hash(original)!=f.sha256)throw new Exception("원본 보관 파일이 기준과 다릅니다: "+f.path);}
    else {if(!File.Exists(path)||Hash(path)!=f.sha256)throw new Exception("재생성에 필요한 원본 보관 파일이 없습니다: "+f.path);if(!CreateHardLink(original,path,IntPtr.Zero))File.Copy(path,original,false);}
   }
  }
  public static void Install(string root,Action<string> progress) {
   if(Running(root))throw new Exception("게임을 종료한 뒤 설치해 주세요.");
   Recover(root);CheckBase(root);
   var m=Parse<Manifest>(File.ReadAllText(SafeInternal(root,"updates/initial-manifest.json"),Encoding.UTF8));
   var original=Parse<BaseInfo>(File.ReadAllText(Safe(root,"updates/originals.json"),Encoding.UTF8));
   var state=new State{schema=2,app_id=AppId,version="0.0.0",base_version=m.base_version,files=new List<Item>()};
   string installed=Safe(root,"updates/installed.json",true);
   if(File.Exists(installed)){
    var was=Parse<State>(File.ReadAllText(installed,Encoding.UTF8));ValidateState(was,root);
    if(Ver(was.version)>Ver(m.version))throw new Exception("더 최신 버전이 설치돼 있습니다. 수동 업데이트를 이용해 주세요.");
   }
   foreach(var f in m.inventory){
    string path=Safe(root,f.path);if(!File.Exists(path))throw new Exception("필요한 파일이 없습니다: "+f.path);
    string current=Hash(path);var orig=original.files.SingleOrDefault(i=>i.path==f.path);
    if(current!=f.sha256&&(orig==null||orig.sha256!=current))throw new Exception("기준과 다른 파일을 보존합니다: "+f.path+". 기존 버전은 해당 설치기의 원상복구 후 설치해 주세요.");
    state.files.Add(new Item{path=f.path,size=new FileInfo(path).Length,sha256=current,generated=f.generated});
   }
   Validate(m,state,root);EnsureOriginals(root);
   Apply(root,m,state,(p,d)=>{throw new Exception("설치 압축을 모두 풀어 주세요. 누락된 파일: "+p.name);},progress);
   CheckInstalled(root,Parse<State>(File.ReadAllText(installed,Encoding.UTF8)));
  }

  static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024, RecursionLimit=64 }; }
  public static T Parse<T>(string s) { return Serializer().Deserialize<T>(s); }
  public static string Json(object v) { return Serializer().Serialize(v); }
  public static string Hash(string file) { using(var s=File.OpenRead(file)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
  public static string TextHash(string s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-",""); }
  static bool Digest(string h) { return h!=null && Regex.IsMatch(h,"^[a-f0-9]{64}$"); }
  public static string Safe(string root,string rel,bool internalState=false,bool baseCheck=false) {
   if(String.IsNullOrEmpty(rel)||rel.Length>220||rel.Contains("\\")||rel.Contains(":")||rel.StartsWith("/")||rel.EndsWith("/")) throw new Exception("잘못된 업데이트 경로입니다.");
   var parts=rel.Split('/');
   foreach(var p in parts) if(p=="."||p==".."||p.TrimEnd(' ','.')!=p||p.Length==0||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(p,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)",RegexOptions.IgnoreCase)) throw new Exception("허용되지 않는 업데이트 경로입니다.");
   bool allowed=false;
   if(internalState && rel=="updates/installed.json") allowed=true;
   if(GameFiles.Contains(rel,StringComparer.OrdinalIgnoreCase)) allowed=true;
   if(new[]{"updates/PatchEngine.exe","updates/ComipaUpdater.dll","updates/base-requirements.json","updates/originals.json","사용설명서.txt"}.Contains(rel,StringComparer.OrdinalIgnoreCase)) allowed=true;
   if(baseCheck && BaseFiles.Contains(rel,StringComparer.OrdinalIgnoreCase))allowed=true;
   if(!allowed) throw new Exception("업데이트가 보호 영역을 포함합니다: "+rel);
   string full=Path.GetFullPath(Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar)));
   string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new Exception("설치 폴더 밖의 경로입니다.");
   string check=full;
   while(check!=null) {
    if((File.Exists(check)||Directory.Exists(check)) && (File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0) throw new Exception("연결된 폴더/파일에는 업데이트할 수 없습니다: "+rel);
    check=Path.GetDirectoryName(check);
   }
   return full;
  }
  public static Version Ver(string s) { Version v; if(s==null||!Regex.IsMatch(s,"^[0-9]+\\.[0-9]+\\.[0-9]+$")||!Version.TryParse(s,out v)) throw new Exception("버전 형식이 잘못되었습니다."); return v; }
  public static void Validate(Manifest m,State local,string root) {
   if(m==null||m.schema!=2||m.app_id!=AppId||local.app_id!=AppId||m.files==null||m.inventory==null||m.packages==null||m.deleted==null) throw new Exception("지원하지 않는 업데이트 정보입니다.");
   Ver(m.version);Ver(m.base_version);Ver(local.version);
   if(Ver(m.version)<Ver(m.base_version))throw new Exception("최신 버전이 기준 버전보다 낮습니다.");
   if(m.base_version!=local.base_version || (local.version!="0.0.0" && Ver(local.version)<Ver(m.base_version))) throw new Exception("이 설치본과 업데이트 기준 버전이 다릅니다. 최신 전체 배포본을 사용해 주세요.");
   if(m.release_tag!="v"+m.version) throw new Exception("릴리스 태그가 버전과 다릅니다.");
   var inv=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in m.inventory) { Safe(root,f.path); if(f.generated!=GameFiles.Contains(f.path,StringComparer.OrdinalIgnoreCase)||!Digest(f.sha256)||f.size<0||f.size>2L*1024*1024*1024||inv.ContainsKey(f.path)) throw new Exception("파일 목록에 중복/형식 오류가 있습니다."); inv.Add(f.path,f); }
   var pk=new Dictionary<string,Package>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in m.packages) { if(p.name==null||!Regex.IsMatch(p.name,"^comipa-update-[A-Za-z0-9.-]+\\.zip$")||!Digest(p.sha256)||p.size<=0||p.size>=2L*1024*1024*1024||pk.ContainsKey(p.name)) throw new Exception("업데이트 묶음 정보가 잘못되었습니다."); pk.Add(p.name,p); }
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in m.files) { Safe(root,f.path); Item i; if(!seen.Add(f.path)||!inv.TryGetValue(f.path,out i)||i.sha256!=f.sha256||i.size!=f.size||i.generated!=f.generated||(f.generated ? (!GameFiles.Contains(f.path,StringComparer.OrdinalIgnoreCase)||f.package!=null) : (f.package==null||!pk.ContainsKey(f.package)))) throw new Exception("변경 파일 대응 오류입니다."); }
   foreach(var n in m.deleted) { Safe(root,n); if(GameFiles.Contains(n,StringComparer.OrdinalIgnoreCase)||n=="updates/PatchEngine.exe"||n=="updates/ComipaUpdater.dll"||n=="updates/base-requirements.json"||n=="updates/originals.json")throw new Exception("필수 파일 삭제 거부"); if(inv.ContainsKey(n)||!seen.Add(n)) throw new Exception("삭제 목록이 변경 목록과 충돌합니다."); }
   ValidateState(local,root);
   var prior=local.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
   foreach(var pair in inv) {
    Item was;
    if((!prior.TryGetValue(pair.Key,out was)||was.sha256!=pair.Value.sha256||was.size!=pair.Value.size)&&!seen.Contains(pair.Key))throw new Exception("필요한 파일이 변경 묶음에 누락됐습니다: "+pair.Key);
   }
   foreach(var n in prior.Keys)if(!inv.ContainsKey(n)&&!seen.Contains(n))throw new Exception("삭제 파일이 목록에 누락됐습니다: "+n);
  }
  public static bool Running(string root) {
   foreach(var p in Process.GetProcessesByName("ComiPaRT")) { try { if(Path.GetFullPath(p.MainModule.FileName).Equals(Path.Combine(Path.GetFullPath(root),"ComiPaRT.exe"),StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; } finally { p.Dispose(); } }
   foreach(var p in Process.GetProcessesByName("PatchEngine")){try{if(Path.GetFullPath(p.MainModule.FileName).StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}finally{p.Dispose();}}
   return false;
  }
  public static void Atomic(string target,string source) {
   Directory.CreateDirectory(Path.GetDirectoryName(target));string tmp=target+".cpupdate-tmp";
   if(File.Exists(tmp)) {if((File.GetAttributes(tmp)&FileAttributes.ReparsePoint)!=0)throw new Exception("연결된 임시 파일 거부");File.Delete(tmp);}
   File.Copy(source,tmp,false);
   if(File.Exists(target)) File.Replace(tmp,target,null); else File.Move(tmp,target);
  }
  static void SaveJson(string path,object value) {
   Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".writing";
   if(File.Exists(tmp)&&(File.GetAttributes(tmp)&FileAttributes.ReparsePoint)!=0)throw new Exception("연결된 상태 파일 거부");
   File.WriteAllText(tmp,Json(value),new UTF8Encoding(false));
   if(File.Exists(path)) File.Replace(tmp,path,null);else File.Move(tmp,path);
  }
  public static void CheckBase(string root) {
   string requirement=Safe(root,"updates/base-requirements.json");
   if(!File.Exists(requirement))throw new Exception("본편 확인 정보가 없습니다. 한글패치를 다시 풀어 주세요.");
   var info=Parse<BaseInfo>(File.ReadAllText(requirement,Encoding.UTF8));
   if(info.schema!=1||info.app_id!=AppId||info.files==null||info.files.Count==0)throw new Exception("본편 확인 정보 형식 오류입니다.");
   string signature=Hash(requirement),cachePath=SafeInternal(root,"updates/base-check.json");BaseCache cache=null;
   try{cache=Parse<BaseCache>(File.ReadAllText(cachePath,Encoding.UTF8));}catch{}
   var fresh=new BaseCache{requirements=signature,files=new Dictionary<string,Stamp>(StringComparer.OrdinalIgnoreCase)};
   foreach(var f in info.files) {
    string path=Safe(root,f.path,false,true);
    if(!File.Exists(path))throw new Exception("이 파일은 본편이 포함되지 않은 한글패치입니다.\n이용자가 준비한 코믹파티 DCE 본편 설치 폴더에 패치를 덮어 풀어 주세요.\n없는 파일: "+f.path);
    var stat=new FileInfo(path);Stamp old=null;
    bool cached=cache!=null&&cache.requirements==signature&&cache.files!=null&&cache.files.TryGetValue(f.path,out old)&&old.size==stat.Length&&old.ticks==stat.LastWriteTimeUtc.Ticks;
    if(stat.Length!=f.size||(!cached&&Hash(path)!=f.sha256))throw new Exception("본편 파일이 이 패치의 기준과 다르거나 손상됐습니다.\n다른 패치가 없는 본편을 확인해 주세요.\n확인할 파일: "+f.path);
    fresh.files[f.path]=new Stamp{size=stat.Length,ticks=stat.LastWriteTimeUtc.Ticks};
   }
   SaveJson(cachePath,fresh);
  }
  static string Work(string root) {
   Safe(root,"updates/installed.json",true);
   string p=Path.Combine(root,".comipa-update");
   var pending=new Stack<string>();if(Directory.Exists(p))pending.Push(p);
   while(pending.Count>0){string d=pending.Pop();if((File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)throw new Exception("업데이트 임시 폴더에 연결 경로가 있습니다.");foreach(string child in Directory.EnumerateFileSystemEntries(d)){var attr=File.GetAttributes(child);if((attr&FileAttributes.ReparsePoint)!=0)throw new Exception("업데이트 임시 폴더에 연결 경로가 있습니다.");if((attr&FileAttributes.Directory)!=0)pending.Push(child);}}
   return p;
  }
  public static void Recover(string root) {
   string work=Work(root),jp=Path.Combine(work,"transaction.json");
   if(!File.Exists(jp)) return;
   if(Running(root)) throw new Exception("미완료 업데이트 복구를 위해 게임을 먼저 종료해 주세요.");
   var j=Parse<Journal>(File.ReadAllText(jp));
   if(j==null||j.changes==null||j.changes.Select(c=>c.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=j.changes.Count)throw new Exception("복구 기록 형식 오류");
   if(j.committed){foreach(var c in j.changes)Safe(root,c.path,c.path=="updates/installed.json");Directory.Delete(work,true);return;}
   foreach(var c in j.changes) {
    string dest=Safe(root,c.path,c.path=="updates/installed.json");
    if(c.existed) {string prior=Path.Combine(work,"prior",c.path);if(!Digest(c.before)||!File.Exists(prior)||Hash(prior)!=c.before)throw new Exception("복구 이전본 검사 실패: "+c.path);}
    string current=File.Exists(dest)?Hash(dest):null;
    if(!j.committed && current!=c.before && current!=c.after)throw new Exception("미완료 교체 이후 변경된 파일을 보존합니다: "+c.path);
   }
   if(!j.committed) foreach(var c in j.changes) {
    string dest=Safe(root,c.path,c.path=="updates/installed.json");
    if(c.existed) { string prior=Path.Combine(work,"prior",c.path); if(!File.Exists(prior)) throw new Exception("복구 파일이 없습니다. .comipa-update 폴더를 보존해 주세요.");Atomic(dest,prior); }
    else if(File.Exists(dest)) File.Delete(dest);
   }
   foreach(var c in j.changes) {
    string tmp=Safe(root,c.path,c.path=="updates/installed.json")+".cpupdate-tmp";
    if(File.Exists(tmp)){if((File.GetAttributes(tmp)&FileAttributes.ReparsePoint)!=0)throw new Exception("연결 임시 파일 거부");File.Delete(tmp);}
   }
   Directory.Delete(work,true);
  }
  public static void Download(string url,string dest,long maxBytes,Action<string> progress) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var req=(HttpWebRequest)WebRequest.Create(url);req.UserAgent="Comipa-DCE-Korean-Patch-Updater/1";req.Timeout=10000;req.ReadWriteTimeout=20000;
   using(var response=(HttpWebResponse)req.GetResponse()) {
    if(response.ResponseUri.Scheme!="https"||!(response.ResponseUri.Host=="github.com"||response.ResponseUri.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase))) throw new Exception("안전하지 않은 다운로드 주소입니다.");
    if(response.ContentLength>maxBytes) throw new Exception("다운로드 크기가 안내와 다릅니다.");
    using(var input=response.GetResponseStream()) using(var output=new FileStream(dest,FileMode.Create,FileAccess.Write,FileShare.None)) {
     byte[] buf=new byte[131072];long total=0;int n;var tick=Stopwatch.StartNew();
     while((n=input.Read(buf,0,buf.Length))>0) {total+=n;if(total>maxBytes)throw new Exception("다운로드 크기 초과");output.Write(buf,0,n);if(tick.ElapsedMilliseconds>250){progress("다운로드 중: "+(total/1048576)+" MB");tick.Restart();}}
     output.Flush(true);
    }
   }
  }
  public static Manifest Fetch(Settings s) {
   if(s==null||s.app_id!=AppId||s.repository==null||!Regex.IsMatch(s.repository,"^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$")) throw new Exception("업데이트 저장소 설정이 없습니다.");
   string tmp=Path.GetTempFileName();
   try {Download("https://github.com/"+s.repository+"/releases/latest/download/update-manifest.json",tmp,16*1024*1024,delegate{});return Parse<Manifest>(File.ReadAllText(tmp,Encoding.UTF8));}finally{File.Delete(tmp);}
  }
  public static void Apply(string root,Manifest m,State local,Action<Package,string> fetch,Action<string> progress,int failAfter=-1) {
   try{ApplyCore(root,m,local,fetch,progress,failAfter);}
   finally{string work=Work(root);if(Directory.Exists(work)&&!File.Exists(Path.Combine(work,"transaction.json"))&&!Running(root))Directory.Delete(work,true);}
  }
  static void ApplyCore(string root,Manifest m,State local,Action<Package,string> fetch,Action<string> progress,int failAfter) {
   Validate(m,local,root);
   if(Ver(m.version)<=Ver(local.version)) return;
   if(Running(root)) throw new Exception("게임을 종료한 뒤 업데이트를 실행해 주세요.");
   string work=Work(root);Recover(root);if(Directory.Exists(work))Directory.Delete(work,true);Directory.CreateDirectory(work);
   var old=local.files.ToDictionary(f=>f.path,f=>f,StringComparer.OrdinalIgnoreCase);
   var originalBase=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
   string basePath=Path.Combine(root,"updates","base-requirements.json");
   if(File.Exists(basePath))originalBase=Parse<BaseInfo>(File.ReadAllText(basePath,Encoding.UTF8)).files.ToDictionary(f=>f.path,f=>f,StringComparer.OrdinalIgnoreCase);
   var changed=new List<Item>();var removals=new List<string>();
   foreach(var f in m.files) {
    string dest=Safe(root,f.path);bool exists=File.Exists(dest);string current=exists?Hash(dest):null;
    if(current==f.sha256)continue;
    Item previous;
    if(exists) {
     bool known=old.TryGetValue(f.path,out previous);
     if(!known)known=originalBase.TryGetValue(f.path,out previous);
     if(!known||previous.sha256!=current)throw new Exception("개별 수정 파일이 있어 덮어쓰지 않았습니다: "+f.path);
    }
    changed.Add(f);
   }
   foreach(var n in m.deleted) {string dest=Safe(root,n);Item previous;if(File.Exists(dest)){if(!old.TryGetValue(n,out previous))continue;if(Hash(dest)!=previous.sha256)throw new Exception("개별 수정 파일을 삭제하지 않았습니다: "+n);removals.Add(n);}}
   var baseline=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in m.inventory){string dest=Safe(root,f.path);baseline[f.path]=File.Exists(dest)?Hash(dest):null;}
   foreach(var n in removals)baseline[n]=Hash(Safe(root,n));
   string statePath=Safe(root,"updates/installed.json",true);baseline["updates/installed.json"]=File.Exists(statePath)?Hash(statePath):null;
   long oldBytes=changed.Select(f=>f.path).Concat(removals).Sum(n=>File.Exists(Safe(root,n))?new FileInfo(Safe(root,n)).Length:0);
   long needed=checked(changed.Sum(f=>f.size)+oldBytes+(changed.Count==0?0:changed.Max(f=>f.size))+m.packages.Where(p=>changed.Any(f=>f.package==p.name)).Sum(p=>p.size)+64L*1024*1024);
   if(File.Exists(statePath))needed+=new FileInfo(statePath).Length*2;
   if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<needed)throw new Exception("업데이트에 필요한 임시 공간이 부족합니다.");
   var staged=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in m.packages.Where(p=>changed.Any(f=>f.package==p.name))) {
    progress("묶음 받는 중: "+p.name);string zip=Path.Combine(work,p.name);fetch(p,zip);
    if(new FileInfo(zip).Length!=p.size||Hash(zip)!=p.sha256)throw new Exception("다운로드 검사에 실패했습니다: "+p.name);
    var expected=m.files.Where(f=>!f.generated&&f.package==p.name).ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using(var archive=ZipFile.OpenRead(zip)) foreach(var e in archive.Entries) {
     Safe(root,e.FullName);Item f;
     if(!seen.Add(e.FullName)||!expected.TryGetValue(e.FullName,out f)||e.Length!=f.size)throw new Exception("압축 파일 목록이 안내와 다릅니다.");
     if(!changed.Any(c=>c.path==f.path)) {
      using(var input=e.Open())using(var h=SHA256.Create())if(BitConverter.ToString(h.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=f.sha256)throw new Exception("묶음 내부 파일 검사 실패");
      continue;
     }
     string dest=Path.Combine(work,"new",f.path);Directory.CreateDirectory(Path.GetDirectoryName(dest));e.ExtractToFile(dest);
     if(Hash(dest)!=f.sha256)throw new Exception("파일 검사 실패: "+f.path);staged.Add(f.path,dest);
    }
    if(seen.Count!=expected.Count)throw new Exception("압축 파일에 누락된 항목이 있습니다.");File.Delete(zip);
   }
   var generated=changed.Where(f=>f.generated).ToList();
   if(generated.Count>0) {
    string engine=staged.ContainsKey("updates/PatchEngine.exe")?staged["updates/PatchEngine.exe"]:Safe(root,"updates/PatchEngine.exe");
    Item expectedEngine=m.inventory.SingleOrDefault(f=>f.path=="updates/PatchEngine.exe");
    if(expectedEngine==null||!File.Exists(engine)||Hash(engine)!=expectedEngine.sha256)throw new Exception("패치 생성 엔진 검사 실패");
    Directory.CreateDirectory(Path.Combine(work,"new"));string request=Path.Combine(work,"generate.json");
    File.WriteAllText(request,Json(generated.Select(f=>f.path).ToArray()),new UTF8Encoding(false));
    progress("한글패치 파일 생성 중… 원본 파일 크기에 따라 몇 분 걸릴 수 있습니다.");
    var info=new ProcessStartInfo(engine,"--prepare \""+Path.GetFullPath(root).TrimEnd('\\')+"\" \""+Path.Combine(work,"new")+"\" \""+request+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root};
    using(var process=Process.Start(info)){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("패치 파일 생성 실패. 원본 보관 파일을 확인해 주세요.");}
    foreach(var f in generated){string output=Path.Combine(work,"new",f.path);if(!File.Exists(output)||new FileInfo(output).Length!=f.size||Hash(output)!=f.sha256)throw new Exception("생성된 파일 검사 실패: "+f.path);staged.Add(f.path,output);}
   }
   foreach(var f in m.inventory)if(!staged.ContainsKey(f.path)) {string current=Safe(root,f.path);if(!File.Exists(current)||new FileInfo(current).Length!=f.size||Hash(current)!=f.sha256)throw new Exception("설치 파일 검사 실패: "+f.path);}
   foreach(var pair in baseline) {string current=Safe(root,pair.Key,pair.Key=="updates/installed.json");if((File.Exists(current)?Hash(current):null)!=pair.Value)throw new Exception("준비 중 파일이 변경됐습니다: "+pair.Key);}
   if(staged.Count!=changed.Count)throw new Exception("필요한 변경 파일이 모두 도착하지 않았습니다.");
   var next=new State{schema=2,app_id=AppId,version=m.version,base_version=m.base_version,files=m.inventory};
   string newState=Path.Combine(work,"state.json");File.WriteAllText(newState,Json(next),new UTF8Encoding(false));staged.Add("updates/installed.json",newState);
   var journal=new Journal{committed=false,changes=new List<Change>()};
   foreach(string n in staged.Keys.Concat(removals)) {
    string dest=Safe(root,n,n=="updates/installed.json");bool exists=File.Exists(dest);
    if(exists){string backup=Path.Combine(work,"prior",n);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(dest,backup);}
    journal.changes.Add(new Change{path=n,existed=exists,before=exists?Hash(dest):null,after=staged.ContainsKey(n)?Hash(staged[n]):null});
   }
   string jp=Path.Combine(work,"transaction.json");SaveJson(jp,journal);
   try {
    if(Running(root))throw new Exception("게임이 실행돼 교체를 중단했습니다.");int done=0;
    foreach(var pair in staged.Where(p=>p.Key!="updates/installed.json")) {progress("적용 중: "+pair.Key);if((File.Exists(Safe(root,pair.Key))?Hash(Safe(root,pair.Key)):null)!=baseline[pair.Key])throw new Exception("교체 직전 파일 변경 감지");Atomic(Safe(root,pair.Key),pair.Value);if(++done==failAfter)throw new Exception("모의 교체 중단");}
    foreach(string n in removals){if((File.Exists(Safe(root,n))?Hash(Safe(root,n)):null)!=baseline[n])throw new Exception("삭제 직전 파일 변경 감지");File.Delete(Safe(root,n));}
    if((File.Exists(statePath)?Hash(statePath):null)!=baseline["updates/installed.json"])throw new Exception("설치 기록 동시 변경 감지");
    Atomic(Safe(root,"updates/installed.json",true),newState);
    journal.committed=true;SaveJson(jp,journal);Directory.Delete(work,true);
   } catch {Recover(root);throw;}
  }
 }
 public static class App {
  public static Manifest CheckRemote(Func<Manifest> check,bool manual,Action launch,Action<Exception> notify) {
   try{return check();}catch(Exception error){notify(error);if(!manual)launch();return null;}
  }
  static void Note(string root,string message){try{File.WriteAllText(Engine.SafeInternal(root,"updates/last-check.txt"),DateTime.Now+" "+message,Encoding.UTF8);}catch{}}
  public static ProcessStartInfo LaunchInfo(string root){return new ProcessStartInfo(Path.Combine(Path.GetFullPath(root),"ComiPaRT.exe")){WorkingDirectory=Path.GetFullPath(root),UseShellExecute=true};}
  static void Launch(string root){if(!Engine.Running(root))Process.Start(LaunchInfo(root));}
  static void Perform(Action<Action<string>> action){
   Exception failure=null;
   using(var form=new Form{Text="코믹파티 DCE 한글패치",Width=580,Height=160,StartPosition=FormStartPosition.CenterScreen,ControlBox=false,FormBorderStyle=FormBorderStyle.FixedDialog}){
    var label=new Label{Left=20,Top=20,Width=530,Height=55,Text="준비 중…"};form.Controls.Add(label);
    form.Controls.Add(new ProgressBar{Left=20,Top=85,Width=525,Style=ProgressBarStyle.Marquee});
    Action<string> progress=s=>{if(form.IsHandleCreated)form.BeginInvoke(new Action(()=>label.Text=s));};
    form.Shown+=async delegate{await Task.Run(()=>{try{action(progress);}catch(Exception e){failure=e;}});form.Close();};form.ShowDialog();
   }
   if(failure!=null)throw failure;
  }
  public static void Run(string root,string mode){
   Application.EnableVisualStyles();root=Path.GetFullPath(root);bool fresh;
   using(var mutex=new Mutex(true,"Local\\ComipaUpdate-"+Engine.TextHash(root.TrimEnd('\\').ToLowerInvariant()).Substring(0,24),out fresh)){
    if(!fresh){MessageBox.Show("다른 설치기나 업데이트가 진행 중입니다.","코믹파티 DCE");return;}
    try{
     if(mode=="install"){
      if(MessageBox.Show("이 폴더에 코믹파티 DCE 한글패치를 설치합니다.\n세이브와 설정은 유지됩니다.\n\n설치를 시작할까요?","한글패치 설치",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
      Perform(progress=>Engine.Install(root,progress));MessageBox.Show("설치가 완료됐습니다.\n코믹파티_DCE_한글판.exe로 실행해 주세요.","한글패치 설치");return;
     }
     bool manual=mode=="update";
     Engine.Recover(root);Engine.CheckBase(root);
     var local=Engine.Parse<State>(File.ReadAllText(Engine.Safe(root,"updates/installed.json",true),Encoding.UTF8));Engine.CheckInstalled(root,local);
     var settings=Engine.Parse<Settings>(File.ReadAllText(Engine.SafeInternal(root,"updates/settings.json"),Encoding.UTF8));
     bool network=true;try{network=System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();}catch{}
     if(!manual&&!network){Note(root,"네트워크 없음: 현재 버전 실행");Launch(root);return;}
     var m=CheckRemote(()=>{var found=Engine.Fetch(settings);Engine.Validate(found,local,root);Note(root,"업데이트 확인: "+found.version);return found;},manual,()=>Launch(root),e=>{Note(root,"확인 실패: "+e.Message);if(manual)MessageBox.Show("업데이트를 확인하지 못했습니다.\n"+e.Message,"한글패치 업데이트");});
     if(m==null)return;
     if(Engine.Ver(m.version)<=Engine.Ver(local.version)){if(manual)MessageBox.Show("현재 최신 버전입니다. ("+Engine.DisplayVersion(local.version)+")","한글패치 업데이트");else Launch(root);return;}
     string notes=m.notes??"";if(notes.Length>1200)notes=notes.Substring(0,1200)+"…";
     if(MessageBox.Show("새 한글패치가 있습니다.\n"+Engine.DisplayVersion(local.version)+" → "+Engine.DisplayVersion(m.version)+"\n\n"+notes+"\n\n지금 업데이트할까요?\n세이브는 유지됩니다.","한글패치 업데이트",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes){if(!manual)Launch(root);return;}
     if(Engine.Running(root))throw new Exception("게임을 완전히 종료한 뒤 수동 업데이트를 실행해 주세요.");
     Perform(progress=>Engine.Apply(root,m,local,(p,d)=>Engine.Download("https://github.com/"+settings.repository+"/releases/download/"+m.release_tag+"/"+p.name,d,p.size,progress),progress));
     Note(root,"적용 완료: "+m.version);if(manual)MessageBox.Show("업데이트가 완료됐습니다. ("+Engine.DisplayVersion(m.version)+")","한글패치 업데이트");else Launch(root);
    }catch(Exception e){Note(root,"작업 중단: "+e.Message);MessageBox.Show("작업을 진행할 수 없습니다.\n"+e.Message,"코믹파티 DCE",MessageBoxButtons.OK,MessageBoxIcon.Error);}
   }
  }
 }
}
