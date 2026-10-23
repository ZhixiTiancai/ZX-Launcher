using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace FoolLauncher {
 // Shared by the portable entry point and updater. Update names remain compatible
 // with signed packages understood by older launchers.
 public static class PortableLayout {
  public const string EntryName="智熄的启动器.exe";
  public static string Content(string root){return Path.Combine(root,"minecraft");}
  public static string ProgramFolder(string root){return Path.Combine(Content(root),"launcher");}
  public static bool IsModern(string root){return File.Exists(Path.Combine(ProgramFolder(root),"layout-v2"));}
  public static string InstallRoot(string content){return Path.GetFileName(content.TrimEnd(Path.DirectorySeparatorChar)).Equals("minecraft",StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(content,"launcher","layout-v2"))?Directory.GetParent(content.TrimEnd(Path.DirectorySeparatorChar)).FullName:content;}
  public static string UpdateTarget(string root,string name){return !IsModern(root)||name==EntryName?Path.Combine(root,name):Path.Combine(ProgramFolder(root),name);}
  public static string StateFolder(string root){return IsModern(root)?ProgramFolder(root):root;}
  public static bool UiRunning(string root){foreach(var p in Process.GetProcessesByName("LauncherApp"))using(p)try{if(p.MainModule.FileName.Equals(Path.Combine(ProgramFolder(root),"LauncherApp.exe"),StringComparison.OrdinalIgnoreCase))return true;}catch{}return false;}
  public static bool Running(string root){
   int current=Process.GetCurrentProcess().Id;
   foreach(var p in Process.GetProcesses())using(p)try{string exe=p.MainModule.FileName;string[] paths={Path.Combine(root,EntryName),Path.Combine(root,"愚者启动器.exe"),Path.Combine(ProgramFolder(root),"LauncherApp.exe"),Path.Combine(root,"engine","prismlauncher.exe"),Path.Combine(ProgramFolder(root),"engine","prismlauncher.exe")};if(p.Id!=current&&(paths.Any(x=>x.Equals(exe,StringComparison.OrdinalIgnoreCase))||exe.StartsWith(Path.Combine(Content(root),"runtime")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||exe.StartsWith(Path.Combine(root,"runtime")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))return true;}catch{}
   return false;
  }
  static void Check(string root,string path){string full=Path.GetFullPath(path);if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("目录超出启动器范围。");for(var d=new DirectoryInfo(full);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("目录含有文件链接，请先移除链接后重试。");if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("文件含有链接。");}
  public static void Prepare(string root){
   root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);string content=Content(root),program=ProgramFolder(root);
   var plan=new List<KeyValuePair<string,string>>();
   foreach(string source in Directory.EnumerateFileSystemEntries(root)){
    string name=Path.GetFileName(source);if(name.Equals("minecraft",StringComparison.OrdinalIgnoreCase)||name.Equals(EntryName,StringComparison.OrdinalIgnoreCase))continue;
    string target=new[]{"data","runtime","exports"}.Contains(name,StringComparer.OrdinalIgnoreCase)?Path.Combine(content,name):Path.Combine(program,name);
    if(name=="愚者启动器.exe")target=Path.Combine(program,"legacy",name);
    Check(root,source);Check(root,target);
    if(File.Exists(target)||Directory.Exists(target)){
     throw new IOException("新旧目录存在同名文件，请保留两边数据并联系维护者："+source);
    }
    plan.Add(new KeyValuePair<string,string>(source,target));
   }
   if(plan.Count>0&&Running(root))throw new IOException("请先关闭这个目录中的启动器、Prism 和游戏，再重新打开以整理文件夹。");
   var done=new List<KeyValuePair<string,string>>();
   try{foreach(var item in plan){Directory.CreateDirectory(Path.GetDirectoryName(item.Value));if(Directory.Exists(item.Key))Directory.Move(item.Key,item.Value);else File.Move(item.Key,item.Value);done.Add(item);}}
   catch{foreach(var item in Enumerable.Reverse(done))try{if(Directory.Exists(item.Value))Directory.Move(item.Value,item.Key);else File.Move(item.Value,item.Key);}catch{}throw;}
   Directory.CreateDirectory(program);
   string oldInstances=Path.Combine(content,"data","instances"),versions=Path.Combine(content,"versions");
   if(Directory.Exists(oldInstances)){
    Check(root,oldInstances);Check(root,versions);
    if(Directory.Exists(versions))throw new IOException("旧整合包目录和 versions 同时存在，文件已保留，请联系维护者完成整理。");
    Directory.Move(oldInstances,versions);
   }
   File.WriteAllText(Path.Combine(program,"layout-v2"),"2");
  }
 }
}
