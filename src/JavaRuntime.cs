using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace FoolLauncher {
 public sealed class JavaRuntime {
  public string Path,Version;public int Major;
  internal static int VersionMajor(string version) {
   var match=Regex.Match(version??"",@"^(?:1\.)?(\d+)");return match.Success?Int32.Parse(match.Groups[1].Value):0;
  }
  internal static int[] RequiredMajors(string root,string minecraft) {
   string metadata=PackService.SafePath(System.IO.Path.Combine(root,"data","meta","net.minecraft"),minecraft+".json");
   if(File.Exists(metadata)) {
    var data=Json.Read<Dictionary<string,object>>(metadata);object values;
    if(data.TryGetValue("compatibleJavaMajors",out values)&&values is System.Collections.IEnumerable) {
     var majors=new List<int>();foreach(object value in (System.Collections.IEnumerable)values)majors.Add(Convert.ToInt32(value));
     if(majors.Count>0)return majors.ToArray();
    }
   }
   // Stable releases before metadata has been fetched (e.g. while importing).
   var match=Regex.Match(minecraft??"",@"^1\.(\d+)(?:\.(\d+))?$");if(!match.Success)return new int[0];
   int minor=Int32.Parse(match.Groups[1].Value),patch=match.Groups[2].Success?Int32.Parse(match.Groups[2].Value):0;
   return new[]{minor>=21||minor==20&&patch>=5?21:minor>=18?17:minor==17?16:8};
  }
  internal static JavaRuntime Find(string root,int[] majors) {
   string runtime=System.IO.Path.Combine(root,"runtime");if(!Directory.Exists(runtime))return null;
   var candidates=new List<JavaRuntime>();
   foreach(string java in Directory.GetFiles(runtime,"javaw.exe",SearchOption.AllDirectories)) {
    string release=System.IO.Path.Combine(Directory.GetParent(System.IO.Path.GetDirectoryName(java)).FullName,"release");if(!File.Exists(release))continue;
    string version=File.ReadAllLines(release).FirstOrDefault(l=>l.StartsWith("JAVA_VERSION="));if(version==null)continue;
    version=version.Substring(13).Trim('"');int major=VersionMajor(version);
    if(majors.Contains(major))candidates.Add(new JavaRuntime{Path=java,Version=version,Major=major});
   }
   return candidates.OrderBy(j=>Array.IndexOf(majors,j.Major)).FirstOrDefault();
  }
  internal static void Configure(string root,string minecraft,IDictionary<string,string> cfg,bool required) {
   string current;bool explicitJava=cfg.ContainsKey("OverrideJavaLocation")&&cfg["OverrideJavaLocation"]=="true"&&cfg.TryGetValue("JavaPath",out current)&&File.Exists(current);
   if(explicitJava) {
    current=System.IO.Path.GetFullPath(cfg["JavaPath"]);
    // Keep a user's external Java, but refresh runtimes managed by this launcher.
    if(!current.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"runtime"))+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
   }
   int[] majors=RequiredMajors(root,minecraft);var java=Find(root,majors);
   if(java==null){if(required)throw new IOException("Minecraft "+minecraft+" 需要 Java "+String.Join(" / ",majors)+"，请完整解压新版启动器的 runtime 文件夹，或在 Prism 实例设置中选择对应 Java。");return;}
   cfg["OverrideJavaLocation"]="true";cfg["AutomaticJava"]="false";cfg["JavaPath"]=java.Path.Replace('\\','/');cfg["JavaVersion"]=java.Version;cfg["JavaArchitecture"]="64";
  }
 }
}
