using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FoolLauncher {
 public class PackFile {
  public string path {get;set;}
  public Dictionary<string,string> hashes {get;set;}
  public string[] downloads {get;set;}
  public string manualUrl {get;set;}
  public long fileSize {get;set;}
  public Dictionary<string,string> env {get;set;}
 }
 public class PackIndex {
  public string game {get;set;}
  public int formatVersion {get;set;}
  public string name {get;set;}
  public string versionId {get;set;}
  public string summary {get;set;}
  public Dictionary<string,string> dependencies {get;set;}
  public PackFile[] files {get;set;}
 }
 public class Instance {
  public string Id {get;set;}
  public string Name {get;set;}
  public string Version {get;set;}
  public string Minecraft {get;set;}
  public string Loader {get;set;}
  public bool Ready {get;set;}
  public int FileCount {get;set;}
  public int BundledFileCount {get;set;}
  [ScriptIgnore] public System.Windows.Media.ImageSource IconSource {get;set;}
  public override string ToString() {return Name+"  ·  "+Version;}
 }
 public class Update {
  public string Message; public int Done; public int Total;
  public Update(string message,int done=0,int total=0) {Message=message;Done=done;Total=total;}
 }
 public class MissingFile {public string path; public string reason; public string[] downloads;}
 public static class Json {
  public static T Read<T>(string path) {return Parse<T>(File.ReadAllText(path,Encoding.UTF8));}
  public static T Parse<T>(string text) {return new JavaScriptSerializer {MaxJsonLength=32*1024*1024,RecursionLimit=100}.Deserialize<T>(text);}
  public static void Write(string path,object obj) {
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(obj),new UTF8Encoding(false));
   if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
  }
 }
 public sealed class PackService {
  public readonly string Root;
  public string Data {get{return Path.Combine(Root,"data");}}
  public string InstanceRoot {get{return Path.Combine(Root,"versions");}}
  static readonly HttpClient Http=CreateHttp();
  static HttpClient CreateHttp() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   ServicePointManager.DefaultConnectionLimit=16;
   var handler=new HttpClientHandler {AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate};
   var client=new HttpClient(handler) {Timeout=Timeout.InfiniteTimeSpan};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("FoolLauncher/0.1");
   return client;
  }
  public PackService(string root) {
   Root=Path.GetFullPath(root);Directory.CreateDirectory(Data);Directory.CreateDirectory(InstanceRoot);
   // Give migrated timestamp-only instance folders a recognizable pack name.
   foreach(string old in Directory.GetDirectories(InstanceRoot).Where(p=>Path.GetFileName(p).StartsWith("pack-",StringComparison.Ordinal))) {
    Instance instance;try{instance=Json.Read<Instance>(Path.Combine(old,"launcher-instance.json"));}catch{continue;}
    string id=Path.GetFileName(old),name=ReadableId(instance.Name,id.Substring(Math.Max(0,id.Length-6))),target=SafePath(InstanceRoot,name);
    if(Directory.Exists(target))continue;
    string oldIcon=SafePath(Path.Combine(Data,"instance-icons"),id+".png"),icon=SafePath(Path.Combine(Data,"instance-icons"),name+".png");
    if(File.Exists(oldIcon)&&!File.Exists(icon))File.Copy(oldIcon,icon);
    Directory.Move(old,target);
   }
  }
  static string ReadableId(string name,string suffix){string safe=Regex.Replace(name??"",@"[\x00-\x1f<>:""/\\|?*]","_").Trim().TrimEnd('.');if(safe.Length>48)safe=safe.Substring(0,48);if(safe.Length==0)safe="整合包";if(Regex.IsMatch(safe,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))safe="_"+safe;return safe+"-"+suffix;}
  public string Folder(Instance i) {return SafePath(InstanceRoot,i.Id);}
  public string Game(Instance i) {return Path.Combine(Folder(i),"minecraft");}
  public static string SafePath(string root,string relative) {
   if(String.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(":")) throw new InvalidDataException("整合包含有非法路径。");
   string normalized=relative.Replace('\\','/');
   foreach(string part in normalized.Split('/')) {
    if(part==".."||part=="."||part.EndsWith(".")||part.EndsWith(" ")||part.IndexOfAny(new[]{'<','>','|','?','*','\0'})>=0||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)) throw new InvalidDataException("整合包含有不安全路径："+relative);
   }
   string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string result=Path.GetFullPath(Path.Combine(prefix,normalized.Replace('/',Path.DirectorySeparatorChar)));
   if(!result.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("整合包路径超出游戏目录。");
   return result;
  }
  public PackIndex Validate(PackIndex pack) {
   if(pack==null||pack.game!="minecraft"||pack.formatVersion!=1||pack.files==null||pack.dependencies==null||!pack.dependencies.ContainsKey("minecraft")) throw new InvalidDataException("此文件不是支持的 Modrinth 整合包（需含 modrinth.index.json）。");
   if(pack.files.Length>10000) throw new InvalidDataException("整合包文件数量过多。");
   foreach(var dep in pack.dependencies) {
    if(!new[]{"minecraft","forge","neoforge","fabric-loader","quilt-loader"}.Contains(dep.Key)) throw new InvalidDataException("第一版尚不支持依赖："+dep.Key);
    if(!Regex.IsMatch(dep.Value??"",@"^[A-Za-z0-9_.+\-]{1,100}$")) throw new InvalidDataException("无效的游戏/加载器版本。");
   }
   if(pack.dependencies.Keys.Count(k=>k!="minecraft")>1) throw new InvalidDataException("不能同时启用多个模组加载器。");
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var file in pack.files) {
    string target=SafePath(Path.Combine(Root,"validation"),file.path);
    if(!seen.Add(target)) throw new InvalidDataException("整合包存在重复文件："+file.path);
    if(file.fileSize<0||file.fileSize>4L*1024*1024*1024) throw new InvalidDataException("文件大小无效。");
    if(file.hashes==null||!(file.hashes.ContainsKey("sha512")&&Regex.IsMatch(file.hashes["sha512"],"^[0-9a-fA-F]{128}$"))&&!(file.hashes.ContainsKey("sha1")&&Regex.IsMatch(file.hashes["sha1"],"^[0-9a-fA-F]{40}$"))) throw new InvalidDataException("文件缺少有效校验值："+file.path);
    Uri manual;if(file.manualUrl!=null&&(!Uri.TryCreate(file.manualUrl,UriKind.Absolute,out manual)||manual.Scheme!="https"||manual.Host!="www.curseforge.com"||!String.IsNullOrEmpty(manual.UserInfo)))throw new InvalidDataException("手动下载网页无效。");
    if(file.downloads==null||file.downloads.Length==0&&file.manualUrl==null) throw new InvalidDataException("文件缺少下载地址："+file.path);
    foreach(string url in file.downloads) {
     Uri u;if(!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Scheme!="https"||!String.IsNullOrEmpty(u.UserInfo)) throw new InvalidDataException("文件含有非 HTTPS 下载地址。");
    }
   }
   return pack;
  }
  static bool ClientFile(PackFile f) {return f.env==null||!f.env.ContainsKey("client")||f.env["client"]!="unsupported";}
  public PackIndex Index(Instance i) {return Validate(Json.Read<PackIndex>(Path.Combine(Folder(i),"pack-index.json")));}
  static byte[] ReadZipEntry(ZipArchiveEntry entry,int maximum,CancellationToken ct) {
   if(entry.Length>maximum)throw new InvalidDataException("包内附加信息过大。");
   using(var input=entry.Open())using(var output=new MemoryStream()) {
    var buffer=new byte[65536];int n;while((n=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();if(output.Length+n>maximum)throw new InvalidDataException("包内附加信息过大。");output.Write(buffer,0,n);}return output.ToArray();
   }
  }
  public void Rename(Instance instance,string name) {
   name=(name??"").Trim();
   if(name.Length==0)throw new InvalidDataException("请输入整合包名称。");
   if(name.Length>100||name.Any(Char.IsControl))throw new InvalidDataException("名称最多 100 个字符，且不能包含换行或控制字符。");
   string record=Path.Combine(Folder(instance),"launcher-instance.json"),config=Path.Combine(Folder(instance),"instance.cfg");
   var data=Json.Read<Dictionary<string,object>>(record);byte[] oldConfig=File.ReadAllBytes(config);
   data["Name"]=name;
   try {IniConfig.UpdateGeneral(config,new Dictionary<string,string>{{"name",IniConfig.StringValue(name)}});Json.Write(record,data);}
   catch {File.WriteAllBytes(config,oldConfig);throw;}
   instance.Name=name;
  }
  public List<Instance> List() {
   var list=new List<Instance>();
   foreach(string dir in Directory.GetDirectories(InstanceRoot)) {
    try {var file=Path.Combine(dir,"launcher-instance.json"); if(File.Exists(file)){var i=Json.Read<Instance>(file);i.Id=Path.GetFileName(dir);list.Add(i);}}catch{}
   }
   return list.OrderBy(i=>i.Name).ToList();
  }
  public async Task<Instance> Import(string source,IProgress<Update> progress,CancellationToken ct) {
   return await Task.Run(()=>{
    using(var archive=ZipFile.OpenRead(source)) {
     if(archive.Entries.Count>100000||archive.Entries.Sum(e=>e.Length)>20L*1024*1024*1024) throw new InvalidDataException("整合包解压大小超出第一版限制。");
     var entry=archive.GetEntry("modrinth.index.json");PackIndex pack;string overridePrefix="overrides/";bool curse=false;
     if(entry!=null){pack=Validate(Json.Parse<PackIndex>(Encoding.UTF8.GetString(ReadZipEntry(entry,16*1024*1024,ct)).TrimStart('\uFEFF')));}
     else {
      entry=archive.GetEntry("manifest.json");
      if(entry==null)throw new InvalidDataException("此 ZIP 不是支持的整合包。请选含 modrinth.index.json 的 Modrinth 包，或含 manifest.json 的 CurseForge 包。启动器分享包应解压后运行 EXE。");
      var manifest=Json.Parse<CurseManifest>(Encoding.UTF8.GetString(ReadZipEntry(entry,16*1024*1024,ct)).TrimStart('\uFEFF'));CurseForgePack.Dependencies(manifest);overridePrefix=CurseForgePack.OverridePrefix(manifest);curse=true;
      progress.Report(new Update("已识别 CurseForge 整合包，正在通过 CurseTools 公共代理解析文件清单…"));
      pack=Validate(new CurseForgePack(Data).Resolve(manifest,progress,ct).GetAwaiter().GetResult());
     }
     string id=ReadableId(pack.name,Guid.NewGuid().ToString("N").Substring(0,6));
     var i=new Instance{Id=id,Name=pack.name??"未命名整合包",Version=pack.versionId??"",Minecraft=pack.dependencies["minecraft"],FileCount=pack.files.Count(ClientFile)};
     i.Loader=String.Join(" / ",pack.dependencies.Where(d=>d.Key!="minecraft").Select(d=>d.Key+" "+d.Value));
     string folder=Folder(i), game=Game(i);Directory.CreateDirectory(game);
     try {
      var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      int done=0;
      foreach(var e in archive.Entries) {
       ct.ThrowIfCancellationRequested();
       if(((e.ExternalAttributes>>16)&0xF000)==0xA000) throw new InvalidDataException("不支持符号链接条目。");
       string rel=e.FullName.Replace('\\','/'); string trimmed=null;
       if(rel.StartsWith(overridePrefix,StringComparison.Ordinal)) trimmed=rel.Substring(overridePrefix.Length);
       if(!curse&&rel.StartsWith("client-overrides/",StringComparison.Ordinal)) trimmed=rel.Substring(17);
       if(trimmed==null||trimmed.Length==0||rel.EndsWith("/"))continue;
       string target=SafePath(game,trimmed);
       // client-overrides intentionally takes precedence over overrides.
       if(!targets.Add(rel))throw new InvalidDataException("ZIP 存在重复条目。");
       Directory.CreateDirectory(Path.GetDirectoryName(target));
       using(var input=e.Open())using(var output=File.Create(target))input.CopyTo(output);
       if(++done%25==0) progress.Report(new Update("正在解压配置与内置文件… "+done));
      }
      // Reapply client overrides after common overrides, independent of ZIP order.
      foreach(var e in archive.Entries.Where(e=>!curse&&e.FullName.StartsWith("client-overrides/",StringComparison.Ordinal)&&!e.FullName.EndsWith("/"))) {
       ct.ThrowIfCancellationRequested(); string target=SafePath(game,e.FullName.Substring(17));
       Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=e.Open())using(var output=File.Create(target))input.CopyTo(output);
      }
      var bundleEntry=archive.GetEntry("zhixi-launcher/files.json");
      if(bundleEntry!=null) {
       if(bundleEntry.Length>32*1024*1024)throw new InvalidDataException("包内校验清单过大。");
       var bundle=Json.Parse<BundledManifest>(Encoding.UTF8.GetString(ReadZipEntry(bundleEntry,32*1024*1024,ct)).TrimStart('\uFEFF'));
       if(bundle.formatVersion!=1||bundle.files==null||bundle.files.Length>100000)throw new InvalidDataException("无效的包内校验清单。");
       var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int verified=0;
       foreach(var file in bundle.files) {
        ct.ThrowIfCancellationRequested();string target=SafePath(game,file.path);
        if(!seen.Add(target)||file.size<0||file.size>4L*1024*1024*1024||!Regex.IsMatch(file.sha512??"","^[a-fA-F0-9]{128}$"))throw new InvalidDataException("包内校验信息无效。");
        if(!Verify(target,new PackFile{fileSize=file.size,hashes=new Dictionary<string,string>{{"sha512",file.sha512}}}))throw new InvalidDataException("包内文件校验失败："+file.path);
        if(++verified%25==0)progress.Report(new Update("包内文件已校验 "+verified+" / "+bundle.files.Length,verified,bundle.files.Length));
       }
       i.BundledFileCount=bundle.files.Length;
      }
      var icon=archive.GetEntry("zhixi-launcher/icon.png");
      if(icon!=null)new PackIcons(Root).SetBytes(i,ReadZipEntry(icon,16*1024*1024,ct),ct);
      Json.Write(Path.Combine(folder,"pack-index.json"),pack);
      WriteComponents(i,pack);
      Json.Write(Path.Combine(folder,"launcher-instance.json"),i);
      ConfigureInstance(i,8192);
      progress.Report(new Update(i.BundledFileCount>0?"已导入 "+i.Name+"，包内 "+i.BundledFileCount+" 个文件已通过校验。":"已导入 "+i.Name+"，还需校验和补全 "+i.FileCount+" 个文件。"));
      return i;
     }catch {File.WriteAllText(Path.Combine(folder,"import-failed.txt"),"导入未完成。此文件夹保留以便排查，不会作为可启动实例列出。",Encoding.UTF8);throw;}
    }
   },ct);
  }
  void WriteComponents(Instance i,PackIndex pack) {
   var components=new List<object>{new {uid="net.minecraft",version=i.Minecraft,important=true}};
   foreach(var d in pack.dependencies.Where(d=>d.Key!="minecraft")) {
    string uid=d.Key=="forge"?"net.minecraftforge":d.Key=="neoforge"?"net.neoforged":d.Key=="fabric-loader"?"net.fabricmc.fabric-loader":"org.quiltmc.quilt-loader";
    components.Add(new{uid=uid,version=d.Value,important=true});
   }
   Json.Write(Path.Combine(Folder(i),"mmc-pack.json"),new{formatVersion=1,components=components});
  }
  public void ConfigureInstance(Instance i,int memory,bool requireJava=false) {
   var cfg=new Dictionary<string,string>();string path=Path.Combine(Folder(i),"instance.cfg");
   if(File.Exists(path))foreach(string line in File.ReadAllLines(path)) {int n=line.IndexOf('=');if(n>0)cfg[line.Substring(0,n)]=line.Substring(n+1);}
   cfg["InstanceType"]="OneSix";cfg["name"]=IniConfig.StringValue(i.Name.Replace("\r","").Replace("\n"," "));cfg["iconKey"]="default";
   cfg["OverrideMemory"]="true";cfg["MinMemAlloc"]="1024";cfg["MaxMemAlloc"]=Math.Max(2048,Math.Min(memory,24576)).ToString();
   JavaRuntime.Configure(Root,i.Minecraft,cfg,requireJava);
   File.WriteAllText(path,"[General]\r\n"+String.Join("\r\n",cfg.Select(k=>k.Key+"="+k.Value)),new UTF8Encoding(false));
  }
  public static bool Verify(string path,PackFile f) {
   if(!File.Exists(path)||new FileInfo(path).Length!=f.fileSize)return false;
   string key=f.hashes.ContainsKey("sha512")?"sha512":"sha1";
   using(HashAlgorithm hash=key=="sha512"?(HashAlgorithm)SHA512.Create():SHA1.Create())using(var file=File.OpenRead(path)) {
    string actual=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");return String.Equals(actual,f.hashes[key],StringComparison.OrdinalIgnoreCase);
   }
  }
  internal async Task Download(PackFile f,string target,CancellationToken ct,int timeoutSeconds=240) {
   if(f.downloads.Length==0)throw new IOException("此文件未提供自动下载地址。请通过缺失清单中的 CurseForge 网页下载，再使用“补入已下载文件”。");
   string error="没有可用下载地址";
   var urls=f.downloads.OrderBy(u=>u.Contains("cdn.modrinth.com")?0:u.Contains("mediafilez")?1:2).Distinct().ToArray();
   for(int attempt=0;attempt<2;attempt++)foreach(string url in urls) {
    ct.ThrowIfCancellationRequested();
    string temp=target+".download";
    try {
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
      deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
      using(var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)) {
       if(!response.IsSuccessStatusCode)throw new IOException("HTTP "+(int)response.StatusCode+"（"+new Uri(url).Host+"）");
       using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,81920,true)) {
        var buffer=new byte[81920];long length=0;int n;
        while((n=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0) {
         length+=n;if(length>f.fileSize)throw new IOException("下载文件大小超出清单。");
         await output.WriteAsync(buffer,0,n,deadline.Token).ConfigureAwait(false);
        }
       }
      }
     }
     if(!Verify(temp,f))throw new IOException("文件哈希校验失败");
     if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);return;
    }catch(Exception ex){if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);error=ex is OperationCanceledException?"下载超时":ex.Message;}
    finally{if(File.Exists(temp))File.Delete(temp);}
   }
   throw new IOException(error);
  }
  public async Task<int> Repair(Instance instance,IProgress<Update> progress,CancellationToken ct) {
   var pack=Index(instance);var files=pack.files.Where(ClientFile).ToArray();var failures=new List<MissingFile>();
   var gate=new SemaphoreSlim(6);int done=0;
   instance.Ready=false;Json.Write(Path.Combine(Folder(instance),"launcher-instance.json"),instance);
   await Task.WhenAll(files.Select(async file=>{
    await gate.WaitAsync(ct).ConfigureAwait(false);
    try {
     ct.ThrowIfCancellationRequested();string target=SafePath(Game(instance),file.path);
     if(!await Task.Run(()=>Verify(target,file),ct).ConfigureAwait(false)) {
      progress.Report(new Update("正在下载 "+Path.GetFileName(file.path),Volatile.Read(ref done),files.Length));
      await Download(file,target,ct).ConfigureAwait(false);
     }
    }catch(OperationCanceledException){throw;}
    catch(Exception ex){lock(failures)failures.Add(new MissingFile{path=file.path,reason=ex.Message,downloads=file.downloads.Concat(file.manualUrl==null?new string[0]:new[]{file.manualUrl}).ToArray()});}
    finally{int count=Interlocked.Increment(ref done);progress.Report(new Update("已校验 "+count+" / "+files.Length+" 个文件",count,files.Length));gate.Release();}
   })).ConfigureAwait(false);
   Json.Write(Path.Combine(Folder(instance),"missing-files.json"),failures);
   instance.Ready=failures.Count==0;Json.Write(Path.Combine(Folder(instance),"launcher-instance.json"),instance);
   progress.Report(new Update(failures.Count==0?"整合包文件全部就绪。启动时还会检查游戏本体与模组加载器依赖。":"有 "+failures.Count+" 个文件下载失败，可重试或补入已下载文件。",files.Length,files.Length));
   return failures.Count;
  }
  public async Task<int> AddLocalFiles(Instance i,string[] paths,CancellationToken ct) {
   var index=Index(i);int count=0;
   await Task.Run(()=>{foreach(string source in paths)foreach(var f in index.files.Where(f=>ClientFile(f)&&Path.GetFileName(f.path)==Path.GetFileName(source))) {
    ct.ThrowIfCancellationRequested();if(!Verify(source,f))continue;string target=SafePath(Game(i),f.path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,true);count++;
   }},ct);return count;
  }
 }
}
