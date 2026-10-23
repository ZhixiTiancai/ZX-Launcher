using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace FoolLauncher {
 public sealed class ExportResult {public int Files,Skipped;public long Bytes;public string Path;}
 public sealed class BundledFile {public string path {get;set;} public long size {get;set;} public string sha512 {get;set;}}
 public sealed class BundledManifest {public int formatVersion {get;set;} public BundledFile[] files {get;set;}}
 public sealed class PackExporter {
  readonly PackService Service;
  static readonly HashSet<string> PrivateDirectories=new HashSet<string>(new[]{"saves","backups","backup","simplebackups","ftbbackups","screenshots","logs","crash-reports","server-resource-packs","replay_recordings","replays","recordings","cache","caches","downloads","versions","libraries","assets","natives","runtime","pcl","hmcl","bakaxl","xaero","xaerominimap","xaeroworldmap","accounts","authme","ias","essential","in-game-account-switcher"},StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> PrivateFiles=new HashSet<string>(new[]{"accounts.json","account.json","launcher_accounts.json","usercache.json","usernamecache.json","servers.dat","servers.dat_old","realms_persistence.json","session.json","auth.json","oauth.json","tokens.json","credentials.json","profilekeys.json"},StringComparer.OrdinalIgnoreCase);
  static readonly Regex Credential=new Regex(@"(?im)[""']?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|session[_-]?token|client[_-]?secret|password|api[_-]?key|authorization)[""']?\s*[:=]\s*[""']?([A-Za-z0-9_./+\-=]{8,})",RegexOptions.Compiled);
  public PackExporter(PackService service){Service=service;}
  internal static bool Included(string relative,bool directory) {
   var parts=relative.Replace('\\','/').Split('/');
   for(int i=0;i<(directory?parts.Length:parts.Length-1);i++) {
    string part=parts[i];bool personalConfig=parts[0].Equals("config",StringComparison.OrdinalIgnoreCase)&&new[]{"accounts","essential","authme","ias","in-game-account-switcher"}.Contains(part.ToLowerInvariant());
    bool skinCache=parts[0].Equals("CustomSkinLoader",StringComparison.OrdinalIgnoreCase)&&new[]{"cache","caches","logs"}.Contains(part.ToLowerInvariant());
    if(part.StartsWith(".")||i==0&&(PrivateDirectories.Contains(part)||part.EndsWith("-logs",StringComparison.OrdinalIgnoreCase)||part.StartsWith("XaeroWaypoints",StringComparison.OrdinalIgnoreCase))||personalConfig||skinCache)return false;
   }
   if(directory)return true;
   string name=parts.Last(),lower=name.ToLowerInvariant();
   bool credentialFile=new[]{".json",".json5",".toml",".cfg",".ini",".properties",".yaml",".yml",".txt",".conf",".db",".dat"}.Contains(System.IO.Path.GetExtension(lower))&&Regex.IsMatch(lower,@"(?:^|[_.-])(?:credentials?|passwords?|secrets?|tokens?)(?:[_.-]|$)");
   return !name.StartsWith(".")&&!PrivateFiles.Contains(name)&&!credentialFile&&!Regex.IsMatch(lower,@"\.(?:log(?:\.gz)?|tmp|download|bak|old|lock|lck)$")&&!lower.StartsWith("hs_err_pid")&&!lower.StartsWith("replay_pid");
  }
  static bool ContainsCredential(string path) {
   string extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
   if(!new[]{".json",".json5",".toml",".cfg",".ini",".properties",".yaml",".yml",".txt",".conf"}.Contains(extension))return false;
   if(new FileInfo(path).Length>2*1024*1024)return false;
   return Credential.IsMatch(File.ReadAllText(path,Encoding.UTF8));
  }
  List<string> Collect(string game,CancellationToken ct,out int skipped) {
   var files=new List<string>();var pending=new Queue<string>();pending.Enqueue(game);skipped=0;long bytes=0;
   if((File.GetAttributes(game)&FileAttributes.ReparsePoint)!=0)throw new IOException("游戏目录是文件夹链接，暂不能直接导出。");
   while(pending.Count>0) {
    ct.ThrowIfCancellationRequested();string dir=pending.Dequeue();
    foreach(string entry in Directory.EnumerateFileSystemEntries(dir)) {
     ct.ThrowIfCancellationRequested();var attributes=File.GetAttributes(entry);bool directory=(attributes&FileAttributes.Directory)!=0;
     string relative=entry.Substring(game.Length+1).Replace('\\','/');
     if((attributes&FileAttributes.ReparsePoint)!=0||!Included(relative,directory)){skipped++;continue;}
     PackService.SafePath(game,relative);
     if(directory){pending.Enqueue(entry);continue;}
     if(ContainsCredential(entry)){skipped++;continue;}
     long length=new FileInfo(entry).Length;if(length>4L*1024*1024*1024)throw new IOException("文件过大，无法导出："+relative);
     bytes+=length;if(bytes>20L*1024*1024*1024||files.Count>=99990)throw new IOException("整合包超过当前导入器的 20 GB / 10 万文件限制。");
     files.Add(entry);
    }
   }
   return files.OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).ToList();
  }
  Dictionary<string,string> Dependencies(Instance instance) {
   string patches=System.IO.Path.Combine(Service.Folder(instance),"patches");
   if(Directory.Exists(patches)&&Directory.EnumerateFiles(patches,"*.json").Any())throw new IOException("此实例包含自定义 Prism 组件补丁，暂不支持导出为 Modrinth 整合包。");
   var components=Json.Read<MetaComponents>(System.IO.Path.Combine(Service.Folder(instance),"mmc-pack.json"));
   var result=new Dictionary<string,string>();
   var loaders=new Dictionary<string,string>{{"net.minecraft","minecraft"},{"net.minecraftforge","forge"},{"net.neoforged","neoforge"},{"net.fabricmc.fabric-loader","fabric-loader"},{"org.quiltmc.quilt-loader","quilt-loader"}};
   foreach(var component in components.components??new MetaComponent[0]) {
    string key;if(loaders.TryGetValue(component.uid,out key)) {
     if(result.ContainsKey(key))throw new InvalidDataException("实例有重复组件："+key);result[key]=component.version;
    }else if(!new[]{"org.lwjgl","org.lwjgl3","net.fabricmc.intermediary","org.quiltmc.hashed"}.Contains(component.uid))throw new InvalidDataException("暂不能导出自定义组件："+component.uid);
   }
   if(!result.ContainsKey("minecraft"))throw new InvalidDataException("实例缺少 Minecraft 组件。");return result;
  }
  public static string SuggestedName(Instance instance) {
   string text=(instance.Name??"整合包")+"-"+(instance.Version??"")+"-整合包";
   foreach(char invalid in System.IO.Path.GetInvalidFileNameChars())text=text.Replace(invalid,'_');
   text=new string(text.Where(c=>!Char.IsControl(c)).Take(100).ToArray()).Trim().TrimEnd('.');
   if(text.Length>0&&Char.IsHighSurrogate(text[text.Length-1]))text=text.Substring(0,text.Length-1);
   return (String.IsNullOrWhiteSpace(text)?"整合包":text)+".zip";
  }
  static void WriteText(ZipArchive zip,string name,string value) {using(var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(false)))writer.Write(value);}
  internal static string Digest(HashAlgorithm hash){return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();}
  public Task<ExportResult> Export(Instance instance,string destination,IProgress<Update> progress,CancellationToken ct) {
   return Task.Run(()=>{
    ct.ThrowIfCancellationRequested();string target=System.IO.Path.GetFullPath(destination),game=System.IO.Path.GetFullPath(Service.Game(instance)).TrimEnd('\\','/');
    if(!instance.Ready)throw new IOException("整合包尚未补全，请先执行校验 / 补全文件再导出。");
    string dataPrefix=System.IO.Path.GetFullPath(Service.Data).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
    string versionsPrefix=System.IO.Path.GetFullPath(Service.InstanceRoot).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
    if(target.StartsWith(dataPrefix,StringComparison.OrdinalIgnoreCase)||target.StartsWith(versionsPrefix,StringComparison.OrdinalIgnoreCase))throw new IOException("请把导出的整合包保存到 data、versions 文件夹以外，例如 minecraft 的 exports 文件夹。");
    if(!new[]{".zip",".mrpack"}.Contains(System.IO.Path.GetExtension(target).ToLowerInvariant()))throw new IOException("请使用 .zip 或 .mrpack 扩展名。");
    var original=Service.Index(instance);var index=new PackIndex{game="minecraft",formatVersion=1,name=instance.Name,versionId=instance.Version,summary=original.summary,dependencies=Dependencies(instance),files=new PackFile[0]};Service.Validate(index);
    progress.Report(new Update("正在整理整合包文件…"));int skipped;var files=Collect(game,ct,out skipped);
    if(!files.Any(f=>f.Substring(game.Length+1).Replace('\\','/').StartsWith("mods/",StringComparison.OrdinalIgnoreCase))&&original.files.Any(f=>f.path.StartsWith("mods/",StringComparison.OrdinalIgnoreCase)))throw new IOException("实例中没有模组文件，请先补全整合包再导出。");
    var bundled=new List<BundledFile>();long total=0;string temp=target+".export-"+Guid.NewGuid().ToString("N")+".tmp";
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
    try {
     using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)) {
      int done=0;
      foreach(string file in files) {
       ct.ThrowIfCancellationRequested();string relative=file.Substring(game.Length+1).Replace('\\','/');
       string extension=System.IO.Path.GetExtension(file).ToLowerInvariant();bool compressed=new[]{".jar",".zip",".png",".jpg",".jpeg",".ogg",".mp3",".mp4",".7z",".gz"}.Contains(extension);
       var entry=zip.CreateEntry("overrides/"+relative,compressed?CompressionLevel.NoCompression:CompressionLevel.Optimal);
       using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=entry.Open())using(var hash=SHA512.Create()) {
        long length=0;var buffer=new byte[131072];int n;
        while((n=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();if(length+n>4L*1024*1024*1024||total+length+n>20L*1024*1024*1024)throw new IOException("导出期间文件大小发生变化，超过整合包大小限制。");output.Write(buffer,0,n);hash.TransformBlock(buffer,0,n,null,0);length+=n;}
        hash.TransformFinalBlock(new byte[0],0,0);total+=length;
        bundled.Add(new BundledFile{path=relative,size=length,sha512=Digest(hash)});
       }
       progress.Report(new Update("正在导出 "+(++done)+" / "+files.Count+"："+relative,done,files.Count));
      }
      // All modpack files are overrides. Other Modrinth launchers therefore do
      // not need to download duplicate copies before applying the overrides.
      WriteText(zip,"modrinth.index.json",new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(index));
      WriteText(zip,"zhixi-launcher/files.json",new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(new BundledManifest{formatVersion=1,files=bundled.ToArray()}));
      byte[] icon=new PackIcons(Service.Root).CustomBytes(instance);
      if(icon!=null)using(var output=zip.CreateEntry("zhixi-launcher/icon.png",CompressionLevel.NoCompression).Open())output.Write(icon,0,icon.Length);
      WriteText(zip,"导入说明.txt","整合包："+instance.Name+"\r\n\r\n在智熄的启动器中点击“导入整合包”，选择本 ZIP 即可，无需手动解压。\r\n本包包含当前模组、配置、资源包和光影等文件，不含存档、截图、运行缓存或启动器账号。\r\n首次启动需要联网补全 Minecraft 本体、Java 和模组加载器依赖，并登录自己的正版账号。\r\n自定义图标可在智熄的启动器 0.1.6 及以上版本中自动还原。\r\n");
     }
     ct.ThrowIfCancellationRequested();if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
    }finally{if(File.Exists(temp))File.Delete(temp);}
    var result=new ExportResult{Path=target,Files=files.Count,Bytes=total,Skipped=skipped};
    progress.Report(new Update("整合包导出完成："+target,files.Count,files.Count));return result;
   },ct);
  }
 }
}
