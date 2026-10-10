using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
class Bootstrap {
 [STAThread] static void Main(string[] args) {
  try {
   string root=AppDomain.CurrentDomain.BaseDirectory;
   string name=Path.GetFileNameWithoutExtension(Application.ExecutablePath);
   string mode=name.Contains("설치")?"install":name.Contains("업데이트")?"update":"launch";
   if(Array.IndexOf(args,"--update")>=0)mode="update";
   var assembly=Assembly.Load(File.ReadAllBytes(Path.Combine(root,"updates","ComipaUpdater.dll")));
   assembly.GetType("ComipaUpdate.App").GetMethod("Run").Invoke(null,new object[]{root,mode});
  } catch(Exception e) {
   MessageBox.Show((e.InnerException??e).Message,"코믹파티 DCE 한글패치",MessageBoxButtons.OK,MessageBoxIcon.Error);
  }
 }
}
