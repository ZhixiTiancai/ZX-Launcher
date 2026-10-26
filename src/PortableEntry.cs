using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
namespace FoolLauncher {
 public static class PortableEntry {
  [STAThread] public static int Main(string[] args){
   string root=AppDomain.CurrentDomain.BaseDirectory;
   try{
    bool acquired;using(var gate=new Mutex(true,"ZhixiLayout-"+root.ToLowerInvariant().GetHashCode(),out acquired)){
     if(!acquired)return 0;
     PortableLayout.Prepare(root);
     try{ShortcutIcons.Refresh(root);}catch{/* Icon maintenance must not block launching. */}
     if(args.Length==1&&args[0]=="--migrate-only")return 0;
     if(PortableLayout.UiRunning(root))return 0;
     string app=Path.Combine(PortableLayout.ProgramFolder(root),"LauncherApp.exe");
     using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Zhixi.LauncherApp.exe"))using(var memory=new MemoryStream()){
      if(payload==null)throw new IOException("缺少启动器程序，请重新下载完整更新包。");payload.CopyTo(memory);byte[] bytes=memory.ToArray();bool same=false;
      if(File.Exists(app))using(var sha=SHA256.Create())same=Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(app)))==Convert.ToBase64String(sha.ComputeHash(bytes));
      if(!same){string temp=app+".new";File.WriteAllBytes(temp,bytes);if(File.Exists(app))File.Replace(temp,app,null);else File.Move(temp,app);}
     }
     string arguments=String.Join(" ",Array.ConvertAll(args,Quote));Process.Start(new ProcessStartInfo(app,arguments){UseShellExecute=false,WorkingDirectory=PortableLayout.Content(root)});
    }return 0;
   }catch(Exception ex){try{string log=Path.Combine(PortableLayout.ProgramFolder(root),"startup-error.txt");Directory.CreateDirectory(Path.GetDirectoryName(log));File.WriteAllText(log,ex.ToString());}catch{}MessageBox.Show("启动未完成："+ex.Message,"智熄的启动器");return 1;}
  }
  static string Quote(string text){if(text.Contains("\""))throw new ArgumentException("参数包含无效引号。");return "\""+text.TrimEnd('\\')+"\"";}
 }
}
