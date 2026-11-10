using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class ModTarget {
  public string Id,Name,Game,Loader,Folder,RecordHash,IndexHash;
  public static ModTarget Capture(PackService service,Instance instance){
   if(instance==null)throw new IOException("请先在整合包库选择要安装 Mod 的整合包。");
   if(Path.GetFileName(instance.Id)!=instance.Id)throw new InvalidDataException("整合包编号无效。");string folder=service.Folder(instance);LauncherUpdateCore.NoLinks(folder);LauncherUpdateCore.NoLinks(service.Game(instance));
   var saved=Json.Read<Instance>(Path.Combine(folder,"launcher-instance.json"));var index=service.Index(instance);
   string loader=index.dependencies.Keys.FirstOrDefault(k=>k!="minecraft"),normalized=loader=="fabric-loader"?"fabric":loader=="quilt-loader"?"quilt":loader;
   if(CatalogService.LoaderNumber(normalized)==0)throw new IOException("此整合包没有受支持的模组加载器，请先选择 Forge、Fabric、NeoForge 或 Quilt 整合包。");
   return new ModTarget{Id=instance.Id,Name=saved.Name,Game=index.dependencies["minecraft"],Loader=normalized,Folder=folder,RecordHash=LauncherUpdateCore.HashFile(Path.Combine(folder,"launcher-instance.json")),IndexHash=LauncherUpdateCore.HashFile(Path.Combine(folder,"pack-index.json"))};
  }
  public void Check(PackService service){
   if(service.Folder(new Instance{Id=Id})!=Folder)throw new IOException("目标整合包目录已变化。");LauncherUpdateCore.NoLinks(Folder);LauncherUpdateCore.NoLinks(Path.Combine(Folder,"minecraft","mods"));
   if(!Directory.Exists(Folder)||!File.Exists(Path.Combine(Folder,"launcher-instance.json"))||LauncherUpdateCore.HashFile(Path.Combine(Folder,"launcher-instance.json"))!=RecordHash||LauncherUpdateCore.HashFile(Path.Combine(Folder,"pack-index.json"))!=IndexHash)throw new IOException("目标整合包已变化，请重新选择后再下载。");
  }
 }
 public sealed class InstalledCatalogMod {public string Source,Project,Version,FileName;}
 public sealed class ModInstallResult {public int Files,Dependencies,Replaced,Kept;public string Backup,Target;public string[] Blocked=new string[0];}
 public sealed class ModReplacement {
  public readonly string OldFile,NewFile;
  internal readonly string Path,Hash;
  internal ModReplacement(string path,string hash,string next){Path=path;Hash=hash;OldFile=System.IO.Path.GetFileName(path);NewFile=next;}
  internal string Key {get{return Path+"|"+Hash+"|"+NewFile;}}
 }
 public sealed class CatalogInstaller {
  readonly PackService Service;readonly CatalogService Catalog;readonly Func<CatalogArtifact,string,string,IProgress<Update>,CancellationToken,Task> Download;readonly Func<bool> WritingBlocked;
  public CatalogInstaller(PackService service,CatalogService catalog):this(service,catalog,CatalogNetwork.Download){}
  internal CatalogInstaller(PackService service,CatalogService catalog,Func<CatalogArtifact,string,string,IProgress<Update>,CancellationToken,Task> download,Func<bool> writingBlocked=null){Service=service;Catalog=catalog;Download=download;WritingBlocked=writingBlocked;}
  public async Task<List<CatalogVersion>> Resolve(ModTarget target,CatalogVersion root,CancellationToken ct){
   var result=new List<CatalogVersion>();var chosen=new Dictionary<string,CatalogVersion>();
   await ResolveOne(target,root,chosen,result,ct).ConfigureAwait(false);
   foreach(var v in result)foreach(var dep in v.Dependencies??new CatalogDependency[0])if(dep.Type=="incompatible"&&dep.Project!=null&&chosen.ContainsKey(v.Source+":"+dep.Project))throw new IOException("所选 Mod 与必需依赖声明不兼容，请选择其他版本。");
   return result;
  }
  async Task ResolveOne(ModTarget target,CatalogVersion version,Dictionary<string,CatalogVersion> chosen,List<CatalogVersion> result,CancellationToken ct){
   ct.ThrowIfCancellationRequested();if(version.Source!="modrinth"&&version.Source!="curseforge")throw new IOException("不支持此资源来源。");if(!CatalogService.Compatible(version,target.Game,target.Loader))throw new IOException("Mod 与目标整合包的 Minecraft 版本或加载器不匹配。");CatalogNetwork.Validate(version.File,"mod");
   string key=version.Source+":"+version.ProjectId;CatalogVersion prior;if(chosen.TryGetValue(key,out prior)){if(prior.Id!=version.Id)throw new IOException("必需依赖要求了同一项目的不同版本，请选择其他 Mod 版本。");return;}
   if(chosen.Count>=80)throw new IOException("必需依赖数量过多，已停止安装。");chosen[key]=version;
   foreach(var dependency in version.Dependencies??new CatalogDependency[0])if(dependency.Type=="required"){
    CatalogVersion needed;
    if(!String.IsNullOrEmpty(dependency.Version))needed=await Catalog.Version(version.Source,dependency.Project,dependency.Version,ct).ConfigureAwait(false);
    else{if(String.IsNullOrEmpty(dependency.Project))throw new IOException("此版本包含无法自动解析的必需前置，请选择其他版本。");string depKey=version.Source+":"+dependency.Project;if(chosen.TryGetValue(depKey,out needed))continue;
     var compatible=await Catalog.Versions(new CatalogProject{Id=dependency.Project,Source=version.Source,Kind="mod"},target.Game,target.Loader,0,ct).ConfigureAwait(false);needed=compatible.FirstOrDefault(v=>v.Channel=="release")??compatible.FirstOrDefault();if(needed==null)throw new IOException("找不到适用于此整合包的必需前置："+dependency.Project);
    }
    await ResolveOne(target,needed,chosen,result,ct).ConfigureAwait(false);
   }
   result.Add(version);
  }
  internal static HashSet<string> ModIds(string path){
   var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var zip=ZipFile.OpenRead(path)){
    if(zip.Entries.Count>40000)throw new IOException("Mod JAR 条目数量过多。");
    foreach(string name in new[]{"fabric.mod.json","quilt.mod.json","META-INF/mods.toml","META-INF/neoforge.mods.toml","mcmod.info"}){
     var e=zip.GetEntry(name);if(e==null)continue;if(e.Length>2*1024*1024)throw new IOException("Mod 元数据过大。");string text;using(var reader=new StreamReader(e.Open(),Encoding.UTF8))text=reader.ReadToEnd();
     if(name=="fabric.mod.json"){var data=Json.Parse<Dictionary<string,object>>(text);object value;if(data.TryGetValue("id",out value))result.Add(Convert.ToString(value));}
     else if(name=="quilt.mod.json"){var data=Json.Parse<Dictionary<string,object>>(text);object loader,value;if(data.TryGetValue("quilt_loader",out loader)&&loader is Dictionary<string,object>&&((Dictionary<string,object>)loader).TryGetValue("id",out value))result.Add(Convert.ToString(value));}
     else if(name.EndsWith(".toml")){
      // Dependency tables also contain modId; only [[mods]] declares a provided ID.
      var tables=Regex.Matches(text,@"(?m)^[ \t]*\[\[?([^\]\r\n]+)\]\]?[ \t]*(?:#.*)?\r?$");
      for(int n=0;n<tables.Count;n++){var table=tables[n];if(table.Groups[1].Value.Trim()!="mods"||!table.Value.TrimStart().StartsWith("[["))continue;int start=table.Index+table.Length,end=n+1<tables.Count?tables[n+1].Index:text.Length;var id=Regex.Match(text.Substring(start,end-start),@"(?m)^[ \t]*modId[ \t]*=[ \t]*['""]([a-zA-Z0-9_.\-]+)['""]");if(id.Success)result.Add(id.Groups[1].Value);}
     }else foreach(Match match in Regex.Matches(text,@"""modid""\s*:\s*""([a-zA-Z0-9_.\-]+)"""))result.Add(match.Groups[1].Value);
    }
   }result.RemoveWhere(x=>!Regex.IsMatch(x??"",@"\A[a-zA-Z0-9_.\-]{1,100}\z"));return result;
  }
  static bool Match(string path,CatalogArtifact f){return PackService.Verify(path,new PackFile{fileSize=f.Size,hashes=f.Hashes});}
  sealed class Change {public string Target,Source,Saved;public bool Had,Written;}
  internal static void Commit(string root,string staging,string backup,List<KeyValuePair<string,string>> files,CancellationToken ct,Action<int> afterWrite){
   LauncherUpdateCore.NoLinks(staging);LauncherUpdateCore.NoLinks(backup);
   var changes=new List<Change>();string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";
   foreach(var file in files){string target=Path.GetFullPath(file.Key);if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Mod 安装目标超出当前整合包。");LauncherUpdateCore.NoLinks(target);if(file.Value!=null)LauncherUpdateCore.NoLinks(file.Value);if(File.Exists(target))using(var held=File.Open(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}changes.Add(new Change{Target=target,Source=file.Value,Saved=PackService.SafePath(backup,target.Substring(prefix.Length)),Had=File.Exists(target)});}
   Directory.CreateDirectory(backup);
   try{int count=0;foreach(var c in changes){ct.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(c.Target));if(c.Had){Directory.CreateDirectory(Path.GetDirectoryName(c.Saved));File.Move(c.Target,c.Saved);}c.Written=true;if(c.Source!=null)File.Move(c.Source,c.Target);if(afterWrite!=null)afterWrite(++count);}}
   catch(Exception failure){var errors=new List<string>();foreach(var c in Enumerable.Reverse(changes.Where(x=>x.Written)))try{LauncherUpdateCore.NoLinks(c.Target);if(File.Exists(c.Target))File.Delete(c.Target);if(c.Had)File.Move(c.Saved,c.Target);}catch{errors.Add(c.Target);}if(errors.Count>0)throw new IOException("安装未完成，部分原文件保留在备份目录："+backup,failure);throw;}
  }
  public Task<ModInstallResult> Install(ModTarget target,CatalogVersion selected,IProgress<Update> progress,CancellationToken ct){return Install(target,selected,progress,ct,null);}
  internal async Task<ModInstallResult> Install(ModTarget target,CatalogVersion selected,IProgress<Update> progress,CancellationToken ct,Action<int> afterWrite,Func<ModReplacement[],Task<bool>> confirm=null){
   target.Check(Service);progress.Report(new Update("正在解析必需前置…"));var versions=await Resolve(target,selected,ct).ConfigureAwait(false);
   return await InstallResolved(target,versions,progress,ct,confirm,afterWrite).ConfigureAwait(false);
  }
  static string[] ExistingMods(string mods){LauncherUpdateCore.NoLinks(mods);return Directory.Exists(mods)?Directory.GetFiles(mods).Where(f=>f.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)||f.EndsWith(".jar.disabled",StringComparison.OrdinalIgnoreCase)).ToArray():new string[0];}
  static bool Active(string path){return path.EndsWith(".jar",StringComparison.OrdinalIgnoreCase);}
  static async Task<bool> Approve(IEnumerable<ModReplacement> replacements,HashSet<string> approved,Func<ModReplacement[],Task<bool>> confirm,CancellationToken ct){
   ct.ThrowIfCancellationRequested();var pending=replacements.Where(r=>!approved.Contains(r.Key)).GroupBy(r=>r.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();if(pending.Length==0)return true;
   if(confirm==null)throw new IOException("检测到已有模组，需要确认覆盖后才能安装。");
   bool accepted=await confirm(pending).ConfigureAwait(false);
   ct.ThrowIfCancellationRequested();if(accepted)foreach(var item in pending)approved.Add(item.Key);return accepted;
  }
  sealed class PlannedMod {
   public CatalogVersion Version;public string Stage,Hash,Same,Destination;public HashSet<string> Ids;public ModReplacement[] Replacements;
  }
  sealed class KeptMod {public string[] Paths;public bool Exact;}
  static void Keep(Dictionary<CatalogVersion,KeptMod> kept,CatalogVersion version,IEnumerable<string> paths,string same){
   kept[version]=new KeptMod{Paths=(same==null?paths:paths.Concat(new[]{same})).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),Exact=same!=null};
  }
  static void BlockUnavailable(List<CatalogVersion> versions,Dictionary<CatalogVersion,KeptMod> kept,Dictionary<CatalogVersion,string> blocked){
   bool changed;do{changed=false;foreach(var v in versions.Where(v=>!kept.ContainsKey(v)&&!blocked.ContainsKey(v)).ToArray())
    foreach(var dep in (v.Dependencies??new CatalogDependency[0]).Where(d=>d.Type=="required")){
     bool pinned=!String.IsNullOrEmpty(dep.Version);
     var required=versions.FirstOrDefault(d=>d.Source==v.Source&&(pinned?d.Id==dep.Version:d.ProjectId==dep.Project));
     KeptMod retained;if(required!=null&&(blocked.ContainsKey(required)||kept.TryGetValue(required,out retained)&&(!retained.Paths.Any(Active)||pinned&&!retained.Exact))){
      blocked[v]=v.File.Name+"：保留的前置无法满足要求（"+required.File.Name+"）。";changed=true;break;
     }
    }
   }while(changed);
  }
  internal async Task<ModInstallResult> InstallResolved(ModTarget target,List<CatalogVersion> versions,IProgress<Update> progress,CancellationToken ct,Func<ModReplacement[],Task<bool>> confirm,Action<int> afterWrite=null){
   target.Check(Service);
   string staging=PackService.SafePath(target.Folder,".mod-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
   string lockFile=Path.Combine(target.Folder,".mod-install.lock");LauncherUpdateCore.NoLinks(lockFile);
   try{using(var held=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    string mods=Path.Combine(target.Folder,"minecraft","mods"),receiptFile=Path.Combine(target.Folder,"catalog-mods.json");LauncherUpdateCore.NoLinks(receiptFile);
    var receipts=File.Exists(receiptFile)?Json.Read<List<InstalledCatalogMod>>(receiptFile):new List<InstalledCatalogMod>();
    var existing=ExistingMods(mods);var hashes=new Dictionary<string,string>();foreach(string file in existing){ct.ThrowIfCancellationRequested();LauncherUpdateCore.NoLinks(file);hashes[file]=LauncherUpdateCore.HashFile(file);}
    var approved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var known=new Dictionary<CatalogVersion,ModReplacement[]>();var copies=new Dictionary<CatalogVersion,string>();var activeCopies=new Dictionary<CatalogVersion,string>();
    var kept=new Dictionary<CatalogVersion,KeptMod>();var blocked=new Dictionary<CatalogVersion,string>();
    foreach(var v in versions){
     string same=existing.FirstOrDefault(f=>Active(f)&&Match(f,v.File)),copy=same??existing.FirstOrDefault(f=>!Active(f)&&Match(f,v.File));copies[v]=copy;activeCopies[v]=same;
     known[v]=existing.Where(f=>f!=same&&(f==copy||receipts.Any(r=>r.Source==v.Source&&r.Project==v.ProjectId&&String.Equals(r.FileName,Path.GetFileName(f),StringComparison.OrdinalIgnoreCase)))).Select(f=>new ModReplacement(f,hashes[f],same==null?v.File.Name:Path.GetFileName(same))).ToArray();
    }
    if(!await Approve(known.Values.SelectMany(r=>r),approved,confirm,ct).ConfigureAwait(false))
     foreach(var pair in known.Where(p=>p.Value.Length>0))Keep(kept,pair.Key,pair.Value.Select(r=>r.Path),activeCopies[pair.Key]);
    BlockUnavailable(versions,kept,blocked);
    var staged=new Dictionary<CatalogVersion,string>();int n=0;
    foreach(var version in versions.Where(v=>!kept.ContainsKey(v)&&!blocked.ContainsKey(v))){ct.ThrowIfCancellationRequested();string path=Path.Combine(staging,(++n)+".jar"),same=copies[version]??staged.Values.FirstOrDefault(f=>Match(f,version.File));
     if(same!=null){LauncherUpdateCore.NoLinks(same);File.Copy(same,path);progress.Report(new Update("已存在并通过校验："+Path.GetFileName(same)));}else await Download(version.File,path,"mod",progress,ct).ConfigureAwait(false);if(!Match(path,version.File))throw new IOException("Mod 校验失败："+version.File.Name);staged[version]=path;}
    return await Task.Run(()=>InstallPrepared(target,versions,staged,staging,progress,ct,afterWrite,existing,hashes,receipts,approved,confirm,kept,blocked),ct).ConfigureAwait(false);
   }}finally{if(Directory.Exists(staging)){LauncherUpdateCore.NoLinks(staging);Directory.Delete(staging,true);}}
  }
  async Task<ModInstallResult> InstallPrepared(ModTarget target,List<CatalogVersion> versions,Dictionary<CatalogVersion,string> staged,string staging,IProgress<Update> progress,CancellationToken ct,Action<int> afterWrite,string[] existing,Dictionary<string,string> hashes,List<InstalledCatalogMod> receipts,HashSet<string> approved,Func<ModReplacement[],Task<bool>> confirm,Dictionary<CatalogVersion,KeptMod> kept,Dictionary<CatalogVersion,string> blocked){
   ct.ThrowIfCancellationRequested();target.Check(Service);string mods=Path.Combine(target.Folder,"minecraft","mods");LauncherUpdateCore.NoLinks(mods);
   var identities=new Dictionary<string,HashSet<string>>();
   foreach(string file in existing){ct.ThrowIfCancellationRequested();LauncherUpdateCore.NoLinks(file);try{identities[file]=ModIds(file);}catch{identities[file]=new HashSet<string>();}}
   var plans=new List<PlannedMod>();
   foreach(var v in versions.Where(staged.ContainsKey)){
    ct.ThrowIfCancellationRequested();string hash=LauncherUpdateCore.HashFile(staged[v]);var ids=ModIds(staged[v]);
    string same=existing.FirstOrDefault(f=>Active(f)&&hashes[f]==hash),destination=same??PackService.SafePath(mods,v.File.Name);
    var replacements=existing.Where(f=>f!=same&&(identities[f].Overlaps(ids)||hashes[f]==hash||receipts.Any(r=>r.Source==v.Source&&r.Project==v.ProjectId&&String.Equals(r.FileName,Path.GetFileName(f),StringComparison.OrdinalIgnoreCase)))).Select(f=>new ModReplacement(f,hashes[f],Path.GetFileName(destination))).ToArray();
    plans.Add(new PlannedMod{Version=v,Stage=staged[v],Hash=hash,Ids=ids,Same=same,Destination=destination,Replacements=replacements});
   }
   if(!await Approve(plans.SelectMany(p=>p.Replacements),approved,confirm,ct).ConfigureAwait(false))
    foreach(var p in plans.Where(p=>p.Replacements.Any(r=>!approved.Contains(r.Key))))Keep(kept,p.Version,p.Replacements.Select(r=>r.Path),p.Same);
   var protectedPaths=new HashSet<string>(kept.Values.SelectMany(k=>k.Paths),StringComparer.OrdinalIgnoreCase);
   foreach(var p in plans.Where(p=>!kept.ContainsKey(p.Version)&&p.Replacements.Any(r=>protectedPaths.Contains(r.Path))))blocked[p.Version]=p.Version.File.Name+"：会覆盖你选择保留的文件。";
   BlockUnavailable(versions,kept,blocked);
   var result=new ModInstallResult{Dependencies=versions.Count-1,Target=mods,Kept=kept.Count,Blocked=blocked.Values.ToArray()};
   var remove=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var installs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var artifacts=new Dictionary<string,CatalogArtifact>(StringComparer.OrdinalIgnoreCase);
   var plannedIds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var identical=new Dictionary<string,string>();var reused=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in plans.Where(p=>!kept.ContainsKey(p.Version)&&!blocked.ContainsKey(p.Version))){var v=p.Version;string destination;
    foreach(var dep in v.Dependencies??new CatalogDependency[0])if(dep.Type=="incompatible"&&receipts.Any(r=>r.Source==v.Source&&r.Project==dep.Project&&File.Exists(PackService.SafePath(mods,r.FileName))))throw new IOException("当前整合包中已有此版本声明不兼容的 Mod，请先处理冲突。");
    if(!identical.TryGetValue(p.Hash,out destination)){
    foreach(string id in p.Ids){string previous;if(plannedIds.TryGetValue(id,out previous))throw new IOException("本次下载的两个不同文件都声明了模组 ID："+id+"\n"+previous+"\n"+v.File.Name+"\n请选择其他版本。");plannedIds[id]=v.File.Name;}
    destination=p.Destination;var older=p.Replacements.Select(r=>r.Path).ToArray();
    if(p.Same==null){
     if(File.Exists(destination)&&!older.Contains(destination,StringComparer.OrdinalIgnoreCase))throw new IOException("已存在同名但属于不同 Mod 的文件，请先检查 Mod目录："+v.File.Name);
     if(File.Exists(destination+".disabled")&&!older.Contains(destination+".disabled",StringComparer.OrdinalIgnoreCase))throw new IOException("已存在同名的禁用文件，请先检查 Mod 管理："+v.File.Name);
     if(installs.ContainsKey(destination))throw new IOException("不同资源使用了同一个文件名，已停止安装。");installs[destination]=p.Stage;artifacts[destination]=v.File;
    }else reused.Add(p.Same);
    foreach(string old in older)remove.Add(old);
    identical[p.Hash]=destination;
    }
    receipts.RemoveAll(r=>r.Source==v.Source&&r.Project==v.ProjectId);receipts.Add(new InstalledCatalogMod{Source=v.Source,Project=v.ProjectId,Version=v.Id,FileName=Path.GetFileName(destination)});
   }
   if(remove.Overlaps(reused))throw new IOException("本次安装的文件之间存在冲突，请选择其他版本。");
   if(installs.Count==0&&remove.Count==0)return result;
   receipts.RemoveAll(r=>remove.Contains(PackService.SafePath(mods,r.FileName))&&!installs.ContainsKey(PackService.SafePath(mods,r.FileName)));
   var index=Service.Index(new Instance{Id=target.Id});index.files=index.files.Where(f=>!remove.Contains(PackService.SafePath(Path.Combine(target.Folder,"minecraft"),f.path))).ToArray();
   var entries=index.files.ToList();foreach(var pair in artifacts){entries.RemoveAll(f=>f.path.Equals("mods/"+Path.GetFileName(pair.Key),StringComparison.OrdinalIgnoreCase));entries.Add(new PackFile{path="mods/"+Path.GetFileName(pair.Key),downloads=new[]{pair.Value.Url},fileSize=pair.Value.Size,hashes=pair.Value.Hashes});}index.files=entries.ToArray();Service.Validate(index);
   var record=Json.Read<Instance>(Path.Combine(target.Folder,"launcher-instance.json"));record.FileCount=index.files.Count(f=>f.env==null||!f.env.ContainsKey("client")||f.env["client"]!="unsupported");if(installs.Count>0)record.BundledFileCount=0;
   var writes=new List<KeyValuePair<string,string>>();foreach(string old in remove.Where(p=>!installs.ContainsKey(p)))writes.Add(new KeyValuePair<string,string>(old,null));foreach(var file in installs)writes.Add(file);
   foreach(var item in new[]{new{File="pack-index.json",Value=(object)index},new{File="launcher-instance.json",Value=(object)record},new{File="catalog-mods.json",Value=(object)receipts}}){string path=Path.Combine(staging,item.File);Json.Write(path,item.Value);writes.Add(new KeyValuePair<string,string>(Path.Combine(target.Folder,item.File),path));}
   string disabledState=Path.Combine(target.Folder,"disabled-mods.json");LauncherUpdateCore.NoLinks(disabledState);
   if(remove.Any(f=>!Active(f))&&File.Exists(disabledState)){var states=Json.Read<List<DisabledMod>>(disabledState);states.RemoveAll(s=>remove.Contains(PackService.SafePath(mods,s.File)));string path=Path.Combine(staging,"disabled-mods.json");Json.Write(path,states);writes.Add(new KeyValuePair<string,string>(disabledState,path));}
   target.Check(Service);foreach(var pair in hashes)if(!File.Exists(pair.Key)||LauncherUpdateCore.HashFile(pair.Key)!=pair.Value)throw new IOException("Mod目录在下载期间发生变化，请重试。");
   // Reject an externally added JAR as well; the selected target stays immutable.
   if(!new HashSet<string>(ExistingMods(mods),StringComparer.OrdinalIgnoreCase).SetEquals(existing))throw new IOException("Mod目录在下载期间发生变化，请重试。");
   if(WritingBlocked!=null&&WritingBlocked())throw new IOException("Prism 或游戏已打开，请退出后重试安装。");
   string backup=PackService.SafePath(target.Folder,"mod-backups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));progress.Report(new Update("正在安装到 "+target.Name+" 的 mods 文件夹…"));Commit(target.Folder,staging,backup,writes,ct,afterWrite);
   result.Files=installs.Count;result.Replaced=remove.Count;result.Backup=backup;return result;
  }
  public async Task<Instance> InstallPack(CatalogProject project,CatalogVersion selected,IProgress<Update> progress,Action<Instance> imported,CancellationToken ct){
   if(project.Kind!="modpack"||selected.Source!=project.Source||selected.ProjectId!=project.Id)throw new IOException("整合包项目或版本不匹配。");CatalogNetwork.Validate(selected.File,"modpack");
   string cache=Path.Combine(Service.Data,"downloads","pack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);string archive=PackService.SafePath(cache,selected.File.Name);
   try{await Download(selected.File,archive,"modpack",progress,ct).ConfigureAwait(false);if(!Match(archive,selected.File))throw new IOException("整合包文件校验失败。");var instance=await Service.Import(archive,progress,ct).ConfigureAwait(false);imported(instance);
    if(!String.IsNullOrEmpty(project.IconUrl))try{byte[] icon=await CatalogNetwork.Icon(project.IconUrl,ct).ConfigureAwait(false);new PackIcons(Service.Root).SetBytes(instance,icon,ct);}catch(OperationCanceledException){ct.ThrowIfCancellationRequested();}catch{}
    progress.Report(new Update("整合包已添加到库，正在补全模组文件…"));await Service.Repair(instance,progress,ct).ConfigureAwait(false);return instance;}
   finally{if(Directory.Exists(cache)){LauncherUpdateCore.NoLinks(cache);Directory.Delete(cache,true);}}
  }
 }
}
