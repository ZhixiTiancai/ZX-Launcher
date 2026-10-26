using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
namespace FoolLauncher {
 public static class ShortcutIcons {
  public sealed class Result {public string Icon;public int Updated;}
  static void NoLinks(string path){for(var d=new DirectoryInfo(Path.GetFullPath(path));d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("图标目录包含文件链接。");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("图标文件包含链接。");}
  internal static string SaveIcon(string root,byte[] bytes){
   if(bytes==null||bytes.Length<22||bytes.Length>1024*1024||bytes[0]!=0||bytes[1]!=0||bytes[2]!=1||bytes[3]!=0)throw new InvalidDataException("启动器图标资源无效。");
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant().Substring(0,16);
   string folder=Path.Combine(PortableLayout.ProgramFolder(root),"icons"),file=Path.Combine(folder,"launcher-"+hash+".ico");NoLinks(file);Directory.CreateDirectory(folder);
   if(!File.Exists(file)||!Equal(File.ReadAllBytes(file),bytes)){string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}}return file;
  }
  static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
  public static Result Refresh(string root){
   root=Path.GetFullPath(root).TrimEnd('\\');byte[] bytes;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Zhixi.LauncherIcon.ico"))using(var memory=new MemoryStream()){if(stream==null)throw new IOException("缺少快捷方式图标资源。");stream.CopyTo(memory);bytes=memory.ToArray();}
   string icon=SaveIcon(root,bytes),entry=Path.Combine(root,PortableLayout.EntryName);var result=new Result{Icon=icon};
   var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),root,PortableLayout.ProgramFolder(root),Path.Combine(PortableLayout.ProgramFolder(root),"shortcuts")};
   foreach(string folder in folders)try{if(String.IsNullOrEmpty(folder)||!Directory.Exists(folder))continue;foreach(string link in Directory.GetFiles(folder,"*.lnk",SearchOption.TopDirectoryOnly))try{if(Repair(link,entry,icon,Path.Combine(PortableLayout.ProgramFolder(root),"shortcut-backups")))result.Updated++;}catch{}}catch{}
   return result;
  }
  internal static bool Repair(string path,string entry,string icon,string backups){
   if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return false;object com=new ShellLink();try{
    var file=(IPersistFile)com;file.Load(path,0);var link=(IShellLinkW)com;var target=new StringBuilder(32768);link.GetPath(target,target.Capacity,IntPtr.Zero,4);
    string resolved;try{resolved=Path.GetFullPath(Environment.ExpandEnvironmentVariables(target.ToString()));}catch{return false;}
    if(!String.Equals(resolved,Path.GetFullPath(entry),StringComparison.OrdinalIgnoreCase))return false;
    var current=new StringBuilder(32768);int index;link.GetIconLocation(current,current.Capacity,out index);if(index==0&&String.Equals(current.ToString(),icon,StringComparison.OrdinalIgnoreCase))return false;
    NoLinks(backups);Directory.CreateDirectory(backups);string backup=Path.Combine(backups,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".lnk");File.Copy(path,backup);
    try{link.SetIconLocation(icon,0);file.Save(path,true);}catch{File.Copy(backup,path,true);throw;}
    SHChangeNotify(0x2000,0x0005|0x1000,path,IntPtr.Zero);return true;
   }finally{Marshal.FinalReleaseComObject(com);}
  }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint eventId,uint flags,[MarshalAs(UnmanagedType.LPWStr)]string path,IntPtr second);
  [ComImport,Guid("00021401-0000-0000-C000-000000000046")]internal class ShellLink{}
  [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]internal interface IShellLinkW {
   void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count,IntPtr findData,uint flags);void GetIDList(out IntPtr id);void SetIDList(IntPtr id);
   void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int count);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int count);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int count);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int show);void SetShowCmd(int show);
   void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
   void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);void Resolve(IntPtr window,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
  }
 }
}
