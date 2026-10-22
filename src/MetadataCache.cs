using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FoolLauncher {
 public class MetaPackage { public string uid {get;set;} public string sha256 {get;set;} }
 public class MetaRequirement { public string uid {get;set;} public string equals {get;set;} public string suggests {get;set;} }
 public class MetaVersionEntry { public string version {get;set;} public string sha256 {get;set;} }
 public class MetaRoot { public int formatVersion {get;set;} public MetaPackage[] packages {get;set;} }
 public class MetaList { public string uid {get;set;} public MetaVersionEntry[] versions {get;set;} }
 public class MetaVersion { public string uid {get;set;} public string version {get;set;} public MetaRequirement[] requires {get;set;} }
 public class MetaComponent { public string uid {get;set;} public string version {get;set;} }
 public class MetaComponents { public MetaComponent[] components {get;set;} }
 public sealed class MetadataCache {
  readonly string DirectoryPath;
  readonly string BaseUrl;
  public readonly List<string> VersionFiles=new List<string>();
  static readonly HttpClient Http=CreateHttp();
  public MetadataCache(string data):this(data,"https://meta.prismlauncher.org/v1/") {}
  internal MetadataCache(string data,string baseUrl) {DirectoryPath=Path.Combine(data,"meta");BaseUrl=baseUrl;}
  static HttpClient CreateHttp() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var client=new HttpClient(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=Timeout.InfiniteTimeSpan};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("FoolLauncher/0.1.1");return client;
  }
  static void Identifier(string value) {
   if(String.IsNullOrEmpty(value)||!Regex.IsMatch(value,@"^[A-Za-z0-9_.+\-]{1,150}$")||value=="."||value=="..")throw new InvalidDataException("无效的组件标识。");
  }
  internal static string DependencyVersion(MetaRequirement dependency,IDictionary<string,string> wanted) {
   Identifier(dependency.uid);string selected;
   if(!wanted.TryGetValue(dependency.uid,out selected)) {
    selected=!String.IsNullOrEmpty(dependency.equals)?dependency.equals:dependency.suggests;
    // Prism resolves game mappings against the selected Minecraft version.
    if(String.IsNullOrEmpty(selected)) {
     if(dependency.uid=="net.fabricmc.intermediary"||dependency.uid=="org.quiltmc.hashed")wanted.TryGetValue("net.minecraft",out selected);
     else if(dependency.uid=="org.lwjgl")selected="2.9.1";
     else if(dependency.uid=="org.lwjgl3")selected="3.1.2";
    }
    if(String.IsNullOrEmpty(selected))throw new InvalidDataException("无法确定组件依赖版本："+dependency.uid+"。请检查整合包组件设置。");
   }
   Identifier(selected);
   if(!String.IsNullOrEmpty(dependency.equals)&&selected!=dependency.equals)throw new InvalidDataException("组件依赖版本冲突："+dependency.uid);
   return selected;
  }
  internal static bool Matches(byte[] bytes,string expected) {
   if(!Regex.IsMatch(expected??"","^[0-9a-fA-F]{64}$"))throw new InvalidDataException("元数据缺少有效的 SHA-256。");
   using(var hash=SHA256.Create())return String.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase);
  }
  async Task<byte[]> Fetch(string relative,CancellationToken ct) {
   Exception last=null;
   for(int attempt=0;attempt<3;attempt++) {
    ct.ThrowIfCancellationRequested();
    try {
     using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
      deadline.CancelAfter(TimeSpan.FromSeconds(45));
      using(var response=await Http.GetAsync(BaseUrl+relative,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)) {
       response.EnsureSuccessStatusCode();
       using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()) {
        var buffer=new byte[81920];int n;
        while((n=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0) {
         if(output.Length+n>32*1024*1024)throw new InvalidDataException("元数据大小超限。");output.Write(buffer,0,n);
        }
        return output.ToArray();
       }
      }
     }
    }catch(Exception ex){if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);last=ex;}
   }
   throw new IOException("组件元数据下载失败："+relative+"。请检查网络后重试。",last);
  }
  string PathFor(string relative) {return PackService.SafePath(DirectoryPath,relative);}
  void Save(string relative,byte[] bytes) {
   string dest=PathFor(relative);Directory.CreateDirectory(Path.GetDirectoryName(dest));string temp=dest+".tmp";File.WriteAllBytes(temp,bytes);
   if(File.Exists(dest))File.Replace(temp,dest,null);else File.Move(temp,dest);
  }
  static T Parse<T>(byte[] bytes) {return Json.Parse<T>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));}
  async Task<byte[]> Verified(string relative,string expected,CancellationToken ct) {
   string dest=PathFor(relative);
   if(File.Exists(dest)){byte[] cached=File.ReadAllBytes(dest);if(Matches(cached,expected))return cached;}
   byte[] result=await Fetch(relative,ct).ConfigureAwait(false);
   if(!Matches(result,expected))throw new InvalidDataException("组件元数据校验失败："+relative+"。文件未写入缓存。");
   // Parse before publishing; a successful HTTP response alone is insufficient.
   Parse<Dictionary<string,object>>(result);Save(relative,result);return result;
  }
  public async Task Ensure(string instanceFolder,IProgress<Update> progress,CancellationToken ct) {
   VersionFiles.Clear();
   var pack=Json.Read<MetaComponents>(Path.Combine(instanceFolder,"mmc-pack.json"));
   if(pack.components==null||pack.components.Length==0)throw new InvalidDataException("实例缺少组件列表。");
   var wanted=new Dictionary<string,string>();
   foreach(var component in pack.components) {Identifier(component.uid);Identifier(component.version);wanted[component.uid]=component.version;}
   progress.Report(new Update("正在补全游戏与模组加载器组件元数据…"));
   var rootBytes=await Fetch("index.json",ct).ConfigureAwait(false);var root=Parse<MetaRoot>(rootBytes);
   if(root.formatVersion!=1||root.packages==null)throw new InvalidDataException("元数据根索引格式无效。");
   var pending=new Queue<MetaComponent>(pack.components);var visited=new HashSet<string>();int count=0;
   while(pending.Count>0) {
    ct.ThrowIfCancellationRequested();var component=pending.Dequeue();string key=component.uid+"/"+component.version;
    if(!visited.Add(key))continue;if(visited.Count>100)throw new InvalidDataException("组件依赖数量超限。");
    progress.Report(new Update("校验组件："+component.uid+" "+component.version));
    var package=root.packages.FirstOrDefault(p=>p.uid==component.uid);if(package==null)throw new InvalidDataException("组件未列于官方索引："+component.uid);
    var list=Parse<MetaList>(await Verified(component.uid+"/index.json",package.sha256,ct).ConfigureAwait(false));
    if(list.uid!=component.uid||list.versions==null)throw new InvalidDataException("组件版本索引不匹配。");
    var entry=list.versions.FirstOrDefault(v=>v.version==component.version);if(entry==null)throw new InvalidDataException("官方元数据未列出版本："+key);
    var version=Parse<MetaVersion>(await Verified(key+".json",entry.sha256,ct).ConfigureAwait(false));
    if(version.uid!=component.uid||version.version!=component.version)throw new InvalidDataException("组件版本信息不匹配。");
    VersionFiles.Add(PathFor(key+".json"));
    count++;
    foreach(var dependency in version.requires??new MetaRequirement[0]) {
     string selected=DependencyVersion(dependency,wanted);wanted[dependency.uid]=selected;
     pending.Enqueue(new MetaComponent{uid=dependency.uid,version=selected});
    }
   }
   // Publish the root only after its dependency hash chain is complete.
   Save("index.json",rootBytes);
   progress.Report(new Update("组件元数据已就绪（"+count+" 个组件，包含传递依赖）。"));
  }
 }
}
