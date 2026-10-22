using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public class GameArtifact {public string url {get;set;} public string sha1 {get;set;} public long size {get;set;}}
 public class GameDownloads {public GameArtifact artifact {get;set;} public Dictionary<string,GameArtifact> classifiers {get;set;}}
 public class GameOs {public string name {get;set;} public string arch {get;set;} public string version {get;set;}}
 public class GameRule {public string action {get;set;} public GameOs os {get;set;} public Dictionary<string,bool> features {get;set;}}
 public class GameLibrary {public string name {get;set;} public string url {get;set;} public GameDownloads downloads {get;set;} public GameRule[] rules {get;set;} public Dictionary<string,string> natives {get;set;}}
 public class GameAssetIndex:GameArtifact {public string id {get;set;}}
 public class GameMetadata {public GameLibrary[] libraries {get;set;} public GameLibrary[] mavenFiles {get;set;} public GameLibrary mainJar {get;set;} public GameAssetIndex assetIndex {get;set;}}
 public class GameAsset {public string hash {get;set;} public long size {get;set;}}
 public class GameAssets {public Dictionary<string,GameAsset> objects {get;set;}}
 public sealed class GameFiles {
  readonly PackService Service;readonly Dictionary<string,PackFile> Queue=new Dictionary<string,PackFile>(StringComparer.OrdinalIgnoreCase);
  readonly List<KeyValuePair<string,PackFile>> VerifiedFiles=new List<KeyValuePair<string,PackFile>>();
  static readonly HttpClient MavenHttp=new HttpClient(){Timeout=TimeSpan.FromSeconds(45)};
  public GameFiles(PackService service){Service=service;}
  internal static string MavenPath(string name) {
   var extension="jar";var chunks=name.Split('@');if(chunks.Length>1)extension=chunks[1];var parts=chunks[0].Split(':');
   if(parts.Length<3||parts.Length>4)throw new InvalidDataException("无效的 Maven 坐标。");
   return parts[0].Replace('.','/')+"/"+parts[1]+"/"+parts[2]+"/"+parts[1]+"-"+parts[2]+(parts.Length==4?"-"+parts[3]:"")+"."+extension;
  }
  internal static bool Allowed(GameRule[] rules) {
   if(rules==null||rules.Length==0)return true;bool allowed=false;
   foreach(var rule in rules) {
    if(rule.features!=null&&rule.features.Any(f=>f.Value))continue;
    if(rule.os!=null){if(!String.IsNullOrEmpty(rule.os.name)&&rule.os.name!="windows")continue;
     if(!String.IsNullOrEmpty(rule.os.arch)&&!Regex.IsMatch("amd64",rule.os.arch))continue;
     if(!String.IsNullOrEmpty(rule.os.version)&&!Regex.IsMatch(Environment.OSVersion.Version.ToString(),rule.os.version))continue;}
    allowed=rule.action=="allow";
   }
   return allowed;
  }
  static string[] Sources(string url) {
   var urls=new List<string>{url};var uri=new Uri(url);string mirror=null;
   if(uri.Host=="libraries.minecraft.net")mirror="https://bmclapi2.bangbang93.com/maven"+uri.AbsolutePath;
   else if(uri.Host=="maven.minecraftforge.net")mirror="https://bmclapi2.bangbang93.com/maven"+uri.AbsolutePath;
   else if(uri.Host=="resources.download.minecraft.net")mirror="https://bmclapi2.bangbang93.com/assets"+uri.AbsolutePath;
   else if(uri.Host=="piston-data.mojang.com"||uri.Host=="piston-meta.mojang.com"||uri.Host=="launcher.mojang.com")mirror="https://bmclapi2.bangbang93.com"+uri.AbsolutePath;
   if(mirror!=null)urls.Add(mirror);return urls.ToArray();
  }
  void Add(string relative,GameArtifact artifact) {
   if(artifact==null||String.IsNullOrEmpty(artifact.url))return;
   if(!artifact.url.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(artifact.sha1??"","^[0-9a-fA-F]{40}$")||artifact.size<0)throw new InvalidDataException("游戏依赖缺少可信下载地址或校验："+relative);
   string destination=PackService.SafePath(Service.Data,relative);
   PackFile old;if(Queue.TryGetValue(destination,out old)&&old.hashes["sha1"]!=artifact.sha1)throw new InvalidDataException("游戏依赖校验值冲突："+relative);
   Queue[destination]=new PackFile{path=relative,fileSize=artifact.size,hashes=new Dictionary<string,string>{{"sha1",artifact.sha1}},downloads=Sources(artifact.url)};
  }
  internal static string LegacyUrl(GameLibrary library) {
   Uri repository;
   if(!Uri.TryCreate(library.url,UriKind.Absolute,out repository)||repository.Scheme!="https"||!String.IsNullOrEmpty(repository.UserInfo)||!(repository.Host=="maven.fabricmc.net"||repository.Host=="maven.quiltmc.org"))throw new InvalidDataException("不支持的模组加载器 Maven 仓库："+library.url);
   return library.url.TrimEnd('/')+"/"+MavenPath(library.name);
  }
  async Task<GameArtifact> LegacyArtifact(GameLibrary library,CancellationToken ct) {
   string url=LegacyUrl(library);Exception last=null;
   for(int attempt=0;attempt<2;attempt++)try {
    string sha1;
    using(var response=await MavenHttp.GetAsync(url+".sha1",HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false)) {
     response.EnsureSuccessStatusCode();
     using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var bytes=new MemoryStream()) {
      byte[] buffer=new byte[256];int n;
      while((n=await input.ReadAsync(buffer,0,buffer.Length,ct).ConfigureAwait(false))>0){if(bytes.Length+n>1024)throw new InvalidDataException("Maven 校验文件过大。");bytes.Write(buffer,0,n);}
      sha1=Encoding.UTF8.GetString(bytes.ToArray()).Trim();
     }
    }
    if(!Regex.IsMatch(sha1,"^[0-9a-fA-F]{40}$"))throw new InvalidDataException("Maven 仓库没有提供有效的 SHA-1。");
    using(var request=new HttpRequestMessage(HttpMethod.Head,url))using(var response=await MavenHttp.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false)) {
     response.EnsureSuccessStatusCode();long? size=response.Content.Headers.ContentLength;
     if(!size.HasValue||size.Value<=0||size.Value>256*1024*1024)throw new InvalidDataException("Maven 仓库没有提供有效的文件大小。");
     return new GameArtifact{url=url,sha1=sha1,size=size.Value};
    }
   }catch(Exception ex){if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);last=ex;}
   throw new IOException("读取加载器库下载信息失败："+library.name,last);
  }
  async Task Library(GameLibrary library,CancellationToken ct) {
   if(library==null||!Allowed(library.rules))return;
   string relative="libraries/"+MavenPath(library.name);
   if(library.downloads==null){if(!String.IsNullOrEmpty(library.url))Add(relative,await LegacyArtifact(library,ct).ConfigureAwait(false));return;}
   Add(relative,library.downloads.artifact);
   if(library.natives!=null&&library.natives.ContainsKey("windows")&&library.downloads.classifiers!=null) {
    string classifier=library.natives["windows"].Replace("${arch}","64");GameArtifact artifact;
    if(library.downloads.classifiers.TryGetValue(classifier,out artifact))Add("libraries/"+MavenPath(library.name+":"+classifier),artifact);
   }
  }
  async Task DownloadQueue(IProgress<Update> progress,CancellationToken ct) {
   var files=Queue.ToArray();int done=0;var gate=new SemaphoreSlim(8);var failures=new List<string>();
   await Task.WhenAll(files.Select(async item=>{
    await gate.WaitAsync(ct).ConfigureAwait(false);
    try {
     if(!await Task.Run(()=>PackService.Verify(item.Key,item.Value),ct).ConfigureAwait(false)) {
      progress.Report(new Update("补全游戏依赖："+Path.GetFileName(item.Value.path),Volatile.Read(ref done),files.Length));
      await Service.Download(item.Value,item.Key,ct,120).ConfigureAwait(false);
     }
    }catch(OperationCanceledException){throw;}catch(Exception ex){lock(failures)failures.Add(item.Value.path+"："+ex.Message);}
    finally{int value=Interlocked.Increment(ref done);progress.Report(new Update("游戏文件已校验 "+value+" / "+files.Length,value,files.Length));gate.Release();}
   })).ConfigureAwait(false);
   if(failures.Count>0){string report=Path.Combine(Service.Data,"game-download-errors.txt");File.WriteAllLines(report,failures,Encoding.UTF8);throw new IOException("还有 "+failures.Count+" 个游戏依赖下载失败。详情："+report);}
   VerifiedFiles.AddRange(files);Queue.Clear();
  }
  void PublishPrismCache() {
   string path=Path.Combine(Service.Data,"metacache");
   var root=File.Exists(path)?Json.Read<Dictionary<string,object>>(path):new Dictionary<string,object>();
   var entries=new Dictionary<string,object>(StringComparer.Ordinal);object raw;
   if(root.TryGetValue("entries",out raw)&&raw is System.Collections.IEnumerable)foreach(var item in (System.Collections.IEnumerable)raw) {
    var entry=item as Dictionary<string,object>;if(entry==null||!entry.ContainsKey("base")||!entry.ContainsKey("path"))continue;
    entries[Convert.ToString(entry["base"])+"/"+Convert.ToString(entry["path"])]=entry;
   }
   foreach(var file in VerifiedFiles) {
    string relative=file.Value.path;string cacheBase;string cachePath;
    if(relative.StartsWith("libraries/",StringComparison.Ordinal)){cacheBase="libraries";cachePath=relative.Substring(10);}
    else if(relative.StartsWith("assets/indexes/",StringComparison.Ordinal)){cacheBase="asset_indexes";cachePath=relative.Substring(15);}
    else continue;
    // Prism uses MD5 only to track changes in its HTTP cache. Content was already
    // checked against the official SHA-1 above; preserve its normal cache schema.
    string md5;using(var hash=System.Security.Cryptography.MD5.Create())using(var stream=File.OpenRead(file.Key))md5=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    long timestamp=(long)(File.GetLastWriteTimeUtc(file.Key)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
    var record=new Dictionary<string,object>{{"base",cacheBase},{"path",cachePath},{"md5sum",md5},{"etag",""},{"last_changed_timestamp",timestamp},{"eternal",true}};
    entries[cacheBase+"/"+cachePath]=record;
   }
   root["version"]="1";root["entries"]=entries.Values.ToArray();Json.Write(path,root);
  }
  public async Task Ensure(IEnumerable<string> metadataFiles,IProgress<Update> progress,CancellationToken ct) {
   var assets=new List<GameAssetIndex>();var installers=new List<string>();
   foreach(string file in metadataFiles) {
    var metadata=Json.Read<GameMetadata>(file);
    foreach(var library in metadata.libraries??new GameLibrary[0])await Library(library,ct).ConfigureAwait(false);
    foreach(var library in metadata.mavenFiles??new GameLibrary[0]){await Library(library,ct).ConfigureAwait(false);if(library.name!=null&&library.name.Contains(":forge:")&&library.name.EndsWith(":installer"))installers.Add(PackService.SafePath(Service.Data,"libraries/"+MavenPath(library.name)));}
    await Library(metadata.mainJar,ct).ConfigureAwait(false);
    if(metadata.assetIndex!=null){if(!Regex.IsMatch(metadata.assetIndex.id??"",@"^[A-Za-z0-9_.\-]+$"))throw new InvalidDataException("无效资源索引。");assets.Add(metadata.assetIndex);Add("assets/indexes/"+metadata.assetIndex.id+".json",metadata.assetIndex);}
   }
   await DownloadQueue(progress,ct).ConfigureAwait(false);
   foreach(string installer in installers)using(var zip=ZipFile.OpenRead(installer)) {
    foreach(string name in new[]{"install_profile.json","version.json"}) {
     var entry=zip.GetEntry(name);if(entry==null||entry.Length>16*1024*1024)continue;
     using(var reader=new StreamReader(entry.Open(),Encoding.UTF8)){var profile=Json.Parse<GameMetadata>(reader.ReadToEnd());foreach(var library in profile.libraries??new GameLibrary[0])await Library(library,ct).ConfigureAwait(false);}
    }
   }
   await DownloadQueue(progress,ct).ConfigureAwait(false);
   foreach(var index in assets) {
    var manifest=Json.Read<GameAssets>(Path.Combine(Service.Data,"assets","indexes",index.id+".json"));if(manifest.objects==null)throw new InvalidDataException("无效资源对象索引。");
    foreach(var asset in manifest.objects.Values) {
     if(!Regex.IsMatch(asset.hash??"","^[0-9a-f]{40}$"))throw new InvalidDataException("无效的资源校验值。");
     string path=asset.hash.Substring(0,2)+"/"+asset.hash;Add("assets/objects/"+path,new GameArtifact{url="https://resources.download.minecraft.net/"+path,sha1=asset.hash,size=asset.size});
    }
   }
   await DownloadQueue(progress,ct).ConfigureAwait(false);
   PublishPrismCache();
   progress.Report(new Update("游戏本体、库文件和资源已补全并校验，接下来由 Prism 启动模组加载器。"));
  }
 }
}
