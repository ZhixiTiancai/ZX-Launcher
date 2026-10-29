using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
namespace FoolLauncher {
 public sealed class JavaSettings {
  public bool Automatic=true;public string Selected="";public string[] Known=new string[0];
 }
 public sealed class JavaManager {
  public readonly string Root;readonly string SettingsFile;public JavaSettings Settings {get;private set;}
  public JavaManager(string root){Root=Path.GetFullPath(root);SettingsFile=Path.Combine(Root,"data","java-settings.json");try{Settings=Json.Read<JavaSettings>(SettingsFile)??new JavaSettings();}catch{Settings=new JavaSettings();}Settings.Known=(Settings.Known??new string[0]).Where(p=>!String.IsNullOrWhiteSpace(p)).Take(512).ToArray();}
  public string Full(string path){if(String.IsNullOrWhiteSpace(path))return "";return Path.GetFullPath(Path.IsPathRooted(path)?path:PackService.SafePath(Root,path.Replace('\\','/')));}
  string Store(string path){string p=Path.GetFullPath(path),prefix=Path.Combine(Root,"runtime")+Path.DirectorySeparatorChar;return p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?p.Substring(Root.Length+1).Replace('\\','/'):p;}
  public void Save(){LauncherUpdateCore.NoLinks(SettingsFile);Json.Write(SettingsFile,Settings);}
  public void Remember(IEnumerable<JavaRuntime> runtimes){Settings.Known=Settings.Known.Concat(runtimes.Select(j=>Store(j.Path))).Distinct(StringComparer.OrdinalIgnoreCase).Take(512).ToArray();Save();}
  public void Use(JavaRuntime java){if(java==null)throw new IOException(UiText.T("请先选择一个 Java。"));var valid=Inspect(java.Path);if(valid==null||valid.Architecture!="x64")throw new IOException(UiText.T("请选择有效的 Windows 64 位 Java。"));var old=Settings;Settings=new JavaSettings{Automatic=false,Selected=Store(valid.Path),Known=old.Known.Concat(new[]{Store(valid.Path)}).Distinct(StringComparer.OrdinalIgnoreCase).Take(512).ToArray()};try{Save();}catch{Settings=old;throw;}}
  public void Auto(){bool old=Settings.Automatic;Settings.Automatic=true;try{Save();}catch{Settings.Automatic=old;throw;}}
  public static JavaRuntime Inspect(string executable){try{
   string path=Path.GetFullPath(executable);if(!new[]{"javaw.exe","java.exe"}.Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase)||!File.Exists(path)||new FileInfo(path).Length<128)return null;
   string arch;using(var input=File.OpenRead(path))using(var reader=new BinaryReader(input)){if(reader.ReadUInt16()!=0x5a4d)return null;input.Position=60;int offset=reader.ReadInt32();if(offset<64||offset>input.Length-6)return null;input.Position=offset;if(reader.ReadUInt32()!=0x4550)return null;ushort machine=reader.ReadUInt16();arch=machine==0x8664?"x64":machine==0x14c?"x86":machine==0xaa64?"ARM64":"unknown";}
   string home=Directory.GetParent(Path.GetDirectoryName(path)).FullName,release=Path.Combine(home,"release"),version=null;
   if(File.Exists(release)&&new FileInfo(release).Length<65536){var match=Regex.Match(File.ReadAllText(release),"(?m)^JAVA_VERSION=\"([^\"]+)\"");if(match.Success)version=match.Groups[1].Value;}
   if(version==null){var info=FileVersionInfo.GetVersionInfo(path);var match=Regex.Match(info.ProductVersion??info.FileVersion??"",@"(?:1\.)?\d+(?:[._]\d+)+");if(match.Success)version=match.Value;}
   int major=JavaRuntime.VersionMajor(version);if(major<6||major>99)return null;
   string windowed=Path.Combine(Path.GetDirectoryName(path),"javaw.exe");if(!String.Equals(windowed,path,StringComparison.OrdinalIgnoreCase)&&File.Exists(windowed)){var w=Inspect(windowed);if(w!=null&&w.Major==major&&w.Architecture==arch)return w;}
   return new JavaRuntime{Path=path,Version=version,Major=major,Architecture=arch};
  }catch{return null;}}
  static IEnumerable<string> Dirs(string folder){try{return Directory.GetDirectories(folder).Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0).ToArray();}catch{return new string[0];}}
  public static JavaRuntime[] ScanFolders(IEnumerable<string> roots,int depth,CancellationToken ct){
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var found=new Dictionary<string,JavaRuntime>(StringComparer.OrdinalIgnoreCase);var queue=new Queue<KeyValuePair<string,int>>();foreach(string root in roots)try{if(!String.IsNullOrWhiteSpace(root)&&!root.StartsWith(@"\\",StringComparison.Ordinal))queue.Enqueue(new KeyValuePair<string,int>(Path.GetFullPath(root),0));}catch{}
   while(queue.Count>0&&seen.Count<16000){ct.ThrowIfCancellationRequested();var next=queue.Dequeue();if(!seen.Add(next.Key))continue;try{if(!Directory.Exists(next.Key)||(File.GetAttributes(next.Key)&FileAttributes.ReparsePoint)!=0)continue;if(Path.GetFileName(next.Key).StartsWith(".",StringComparison.Ordinal)&&next.Value>0)continue;
    foreach(string candidate in new[]{Path.Combine(next.Key,"bin","javaw.exe"),Path.Combine(next.Key,"bin","java.exe"),Path.Combine(next.Key,"javaw.exe"),Path.Combine(next.Key,"java.exe")}){var j=Inspect(candidate);if(j!=null)found[j.Path]=j;}
    if(next.Value<depth)foreach(string child in Dirs(next.Key))queue.Enqueue(new KeyValuePair<string,int>(child,next.Value+1));
   }catch{}}
   return found.Values.OrderByDescending(j=>j.Major).ThenByDescending(j=>j.Version,StringComparer.OrdinalIgnoreCase).ToArray();
  }
  IEnumerable<string> CommonRoots(){
   yield return Path.Combine(Root,"runtime");foreach(string p in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){string path=p.Trim().Trim('"');if(Directory.Exists(path)){yield return path;}}
   foreach(string name in new[]{"JAVA_HOME","JDK_HOME","JRE_HOME"}){string p=Environment.GetEnvironmentVariable(name);if(!String.IsNullOrWhiteSpace(p))yield return p;}
   foreach(string program in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})foreach(string vendor in new[]{"Java","Eclipse Adoptium","Eclipse Foundation","Microsoft","Zulu","BellSoft","Amazon Corretto","Semeru"})yield return Path.Combine(program,vendor);
   string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);foreach(string p in new[]{".jdks",".gradle\\jdks",".minecraft\\runtime","scoop\\apps\\temurin-lts-jdk","scoop\\apps\\openjdk"})yield return Path.Combine(user,p);
   yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),".minecraft","runtime");
   foreach(var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed))foreach(string p in new[]{"PCL\\.minecraft\\runtime","PCL\\PCL\\runtimes","PCL\\PCL\\java",".minecraft\\runtime","Java"})yield return Path.Combine(drive.Name,p);
   foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser}){
    var paths=new List<string>();try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))foreach(string family in new[]{@"SOFTWARE\JavaSoft\Java Runtime Environment",@"SOFTWARE\JavaSoft\JRE",@"SOFTWARE\JavaSoft\Java Development Kit",@"SOFTWARE\JavaSoft\JDK",@"SOFTWARE\Eclipse Adoptium\JRE",@"SOFTWARE\Eclipse Adoptium\JDK"})using(var key=baseKey.OpenSubKey(family)){if(key==null)continue;foreach(string version in key.GetSubKeyNames())using(var v=key.OpenSubKey(version)){string home=Convert.ToString(v.GetValue("JavaHome"));if(home!="")paths.Add(home);using(var m=v.OpenSubKey("hotspot\\MSI")){if(m!=null){home=Convert.ToString(m.GetValue("Path"));if(home!="")paths.Add(home);}}}}}catch{}foreach(string p in paths)yield return p;
   }
  }
  public JavaRuntime[] List(bool scanComputer,CancellationToken ct){
   var paths=new List<string>();foreach(string p in Settings.Known.Concat(new[]{Settings.Selected}))try{string full=Full(p);if(full!="")paths.Add(full);}catch{}
   var result=ScanFolders(scanComputer?CommonRoots():new[]{Path.Combine(Root,"runtime")},scanComputer?5:4,ct).ToList();foreach(string p in paths){var j=Inspect(p);if(j!=null)result.Add(j);}
   var unique=result.GroupBy(j=>j.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderByDescending(j=>j.Major).ThenByDescending(j=>j.Version,StringComparer.OrdinalIgnoreCase).ToArray();foreach(var j in unique)j.Source=j.Path.StartsWith(Path.Combine(Root,"runtime")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?UiText.T("启动器内置 / 下载"):UiText.T("本机 Java");return unique;
  }
  internal JavaRuntime Resolve(string minecraft,IDictionary<string,string> cfg,bool required){
   JavaRuntime java=null;int[] majors=JavaRuntime.RequiredMajors(Root,minecraft);
   if(!Settings.Automatic){java=Inspect(Full(Settings.Selected));if(java==null||java.Architecture!="x64"||majors.Length>0&&!majors.Contains(java.Major)){if(required)throw new JavaRequirementException(UiText.T("手动指定的 Java 不存在或不兼容。请在 Java 管理中重新选择，或开启自动选择。")+"\nMinecraft "+minecraft+UiText.T(" 需要 Java ")+String.Join(" / ",majors),majors);return null;}}
   else {
    // Preserve an external per-instance Prism override until the user saves a launcher-wide policy.
    string old;if(!File.Exists(SettingsFile)&&cfg!=null&&cfg.ContainsKey("OverrideJavaLocation")&&cfg["OverrideJavaLocation"]=="true"&&cfg.TryGetValue("JavaPath",out old)&&!Path.GetFullPath(old).StartsWith(Path.Combine(Root,"runtime")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){var external=Inspect(old);if(external!=null&&external.Architecture=="x64"&&majors.Contains(external.Major))java=external;}
    if(java==null)java=List(false,CancellationToken.None).Where(j=>j.Architecture=="x64"&&majors.Contains(j.Major)).OrderBy(j=>Array.IndexOf(majors,j.Major)).ThenBy(j=>j.Source==UiText.T("本机 Java")?1:0).ThenByDescending(j=>VersionKey(j.Version)).FirstOrDefault();
    if(java==null)java=List(true,CancellationToken.None).Where(j=>j.Architecture=="x64"&&majors.Contains(j.Major)).OrderBy(j=>Array.IndexOf(majors,j.Major)).ThenByDescending(j=>VersionKey(j.Version)).FirstOrDefault();
   }
   if(java==null&&required)throw new JavaRequirementException("Minecraft "+minecraft+UiText.T(" 需要 Java ")+String.Join(" / ",majors)+UiText.T("。请到侧栏 Java 管理下载或添加对应的 64 位 Java。"),majors);return java;
  }
  static string VersionKey(string v){return String.Join(".",Regex.Matches(v??"",@"\d+").Cast<Match>().Take(5).Select(m=>m.Value.PadLeft(8,'0')));}
 }
}
