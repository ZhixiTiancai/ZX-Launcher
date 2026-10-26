using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace FoolLauncher {
 public sealed class CatalogProject:System.ComponentModel.INotifyPropertyChanged {
  public string Source,Id,Slug,IconUrl,Website,Kind,Updated,FilterGame,FilterLoader;string originalName,chineseName;
  public string Name{get{return Kind=="mod"&&UiText.Language!="en-US"&&!String.IsNullOrEmpty(chineseName)?chineseName:originalName;}set{originalName=value;}}
  public string OriginalName{get{return originalName;}}public string Summary{get;set;}public long Downloads;public string[] GameVersions,Loaders;
  internal void SetChineseName(string value){chineseName=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("Name"));}
  public string Meta {get{return (Source=="modrinth"?"Modrinth":"CurseForge")+"  ·  "+Downloads.ToString("N0")+UiText.T(" 次下载")+(String.IsNullOrEmpty(Updated)?"":"  ·  "+Updated.Substring(0,Math.Min(10,Updated.Length)));}}
  System.Windows.Media.ImageSource icon;public System.Windows.Media.ImageSource IconSource {get{return icon;}set{icon=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("IconSource"));}}public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
  public string VersionsText {get{string loader=String.IsNullOrEmpty(FilterLoader)?String.Join(" / ",(Loaders??new string[0]).Take(4)):FilterLoader;string game=String.IsNullOrEmpty(FilterGame)?String.Join(", ",(GameVersions??new string[0]).Reverse().Take(5)):FilterGame;return String.Join("  ·  ",new[]{loader,game}.Where(x=>x!=""));}}
  public override string ToString(){return Name??Id;}
 }
 public sealed class CatalogArtifact {public string Name,Url;public long Size;public Dictionary<string,string> Hashes;}
 public sealed class CatalogDependency {public string Project,Version,Type;}
 public sealed class CatalogVersion {
  public string Source,ProjectId,Id,Name,Number,Published,Channel;public string[] GameVersions,Loaders;public CatalogArtifact File;public CatalogDependency[] Dependencies;
  public override string ToString(){return (Name??Number??Id)+" · "+String.Join(" / ",Loaders??new string[0])+" · "+String.Join(", ",(GameVersions??new string[0]).Take(3));}
 }
 public sealed class CatalogQuery {public string Kind="mod",Source="all",Text="",Game="",Loader="",Category="";public int Page;}
 public sealed class CatalogResults {public CatalogProject[] Projects;public bool More;public string Warning="";}
 public static class CatalogNetwork {
  static readonly HttpClient Http=Create();
  static HttpClient Create(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var h=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate};var c=new HttpClient(h){Timeout=Timeout.InfiniteTimeSpan};c.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.3.0 (+https://github.com/ZhixiTiancai/ZX-Launcher)");return c;}
  public static Uri Https(string url){Uri u;if(!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Scheme!="https"||u.IsLoopback||u.HostNameType!=UriHostNameType.Dns||!String.IsNullOrEmpty(u.UserInfo)||u.Fragment.Length>0||u.Port!=443)throw new InvalidDataException("下载地址不是有效的公网 HTTPS 地址。");return u;}
  static bool ApiHost(Uri u){return new[]{"api.modrinth.com","api.curse.tools","mod.mcimirror.top"}.Contains(u.Host,StringComparer.OrdinalIgnoreCase);}
  public static bool Cdn(Uri u){return u.Host.Equals("cdn.modrinth.com",StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith(".forgecdn.net",StringComparison.OrdinalIgnoreCase)||u.Host.Equals("forgecdn.net",StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith(".cursecdn.com",StringComparison.OrdinalIgnoreCase);}
  static async Task Read(string url,Stream output,long max,bool api,IProgress<long> progress,CancellationToken ct){
   Uri u=Https(url);if(api?!ApiHost(u):!Cdn(u))throw new InvalidDataException("此资源的下载来源不受支持。");
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)){timeout.CancelAfter(TimeSpan.FromSeconds(api?35:900));
    for(int redirect=0;redirect<5;redirect++)using(var response=await Http.GetAsync(u,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
     if((int)response.StatusCode>=300&&(int)response.StatusCode<400){if(response.Headers.Location==null)throw new IOException("下载重定向无效。");u=Https(new Uri(u,response.Headers.Location).AbsoluteUri);if(api?!ApiHost(u):!Cdn(u))throw new IOException("下载重定向到不支持的来源。");continue;}
     if(!response.IsSuccessStatusCode)throw new IOException("HTTP "+(int)response.StatusCode+"（"+u.Host+"）");
     if(response.Content.Headers.ContentLength>max)throw new InvalidDataException("远程文件超过大小限制。");
     using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)){var buffer=new byte[65536];long total=0;int n;while((n=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0){total+=n;if(total>max)throw new InvalidDataException("远程文件超过大小限制。");await output.WriteAsync(buffer,0,n,timeout.Token).ConfigureAwait(false);if(progress!=null)progress.Report(total);}}return;
    }throw new IOException("下载重定向过多。");
   }
  }
  public static async Task<string> Json(string url,CancellationToken ct){using(var memory=new MemoryStream()){await Read(url,memory,12*1024*1024,true,null,ct).ConfigureAwait(false);return Encoding.UTF8.GetString(memory.ToArray());}}
  public static async Task<byte[]> Icon(string url,CancellationToken ct){using(var memory=new MemoryStream()){await Read(url,memory,2*1024*1024,false,null,ct).ConfigureAwait(false);return memory.ToArray();}}
  public static async Task<string> Curse(string path,CancellationToken ct){Exception error=null;foreach(string prefix in new[]{"https://api.curse.tools/v1/","https://mod.mcimirror.top/curseforge/v1/"})try{return await Json(prefix+path,ct).ConfigureAwait(false);}catch(OperationCanceledException){ct.ThrowIfCancellationRequested();error=new IOException("CurseForge 查询超时。");}catch(Exception ex){error=ex;}throw new IOException("CurseForge 公共代理暂不可用："+error.Message,error);}
  public static void Validate(CatalogArtifact a,string kind){
   if(a==null||String.IsNullOrWhiteSpace(a.Name)||a.Name.IndexOfAny(new[]{'/','\\'})>=0||a.Size<=0||a.Size>4L*1024*1024*1024)throw new InvalidDataException("文件名或大小无效。");PackService.SafePath(Path.GetTempPath(),a.Name);
   bool extension=kind=="mod"?a.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase):a.Name.EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase)||a.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase);if(!extension)throw new InvalidDataException("资源文件类型不匹配。");
   if(a.Hashes==null||!a.Hashes.Any(p=>(p.Key=="sha512"&&Regex.IsMatch(p.Value??"",@"\A[0-9a-fA-F]{128}\z"))||(p.Key=="sha1"&&Regex.IsMatch(p.Value??"",@"\A[0-9a-fA-F]{40}\z"))))throw new InvalidDataException("下载文件缺少有效的哈希校验值。");
   if(String.IsNullOrWhiteSpace(a.Url))throw new IOException("作者未开放自动下载，请使用“项目网页”访问作者提供的下载方式。");if(!Cdn(Https(a.Url)))throw new InvalidDataException("资源不是受支持的官方 CDN 下载地址。");
  }
  public static async Task Download(CatalogArtifact a,string destination,string kind,IProgress<Update> progress,CancellationToken ct){
   Validate(a,kind);LauncherUpdateCore.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));
   try{using(var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))await Read(a.Url,stream,a.Size,false,new ByteProgress(n=>progress.Report(new Update("正在下载 "+a.Name+" · "+(n/1048576.0).ToString("0.0")+" / "+(a.Size/1048576.0).ToString("0.0")+" MB",(int)Math.Min(n/1024,Int32.MaxValue),(int)Math.Max(1,Math.Min(a.Size/1024,Int32.MaxValue))))),ct).ConfigureAwait(false);
    ct.ThrowIfCancellationRequested();if(!PackService.Verify(destination,new PackFile{fileSize=a.Size,hashes=a.Hashes}))throw new IOException("下载文件哈希校验失败："+a.Name);
   }catch{if(File.Exists(destination))File.Delete(destination);throw;}
  }
  sealed class ByteProgress:IProgress<long>{readonly Action<long> action;DateTime last;public ByteProgress(Action<long> a){action=a;}public void Report(long n){if((DateTime.UtcNow-last).TotalMilliseconds<180)return;last=DateTime.UtcNow;action(n);}}
 }
 // API DTO fields are populated by JavaScriptSerializer.
 #pragma warning disable 0649
 public sealed class CatalogService {
  readonly Func<string,CancellationToken,Task<string>> Fetch;
  public CatalogService():this((url,ct)=>url.StartsWith("cf:",StringComparison.Ordinal)?CatalogNetwork.Curse(url.Substring(3),ct):CatalogNetwork.Json(url,ct)){}
  internal CatalogService(Func<string,CancellationToken,Task<string>> fetch){Fetch=fetch;}
  static string Q(string s){return Uri.EscapeDataString(s??"");}
  static string Encode(object value){return new JavaScriptSerializer().Serialize(value);}
  public static string Id(string s){if(!Regex.IsMatch(s??"",@"\A[a-zA-Z0-9_\-]{1,100}\z"))throw new InvalidDataException("资源编号无效。");return s;}
  public static int LoaderNumber(string s){return s=="forge"?1:s=="fabric"?4:s=="quilt"?5:s=="neoforge"?6:0;}
  static string NormalizeLoader(string s){return (s??"").ToLowerInvariant().Replace(" ","");}
  static readonly Dictionary<string,int> Categories=new Dictionary<string,int>{{"optimization",6814},{"library",421},{"technology",412},{"magic",419},{"worldgen",406},{"adventure",422},{"decoration",424},{"quests",4478},{"lightweight",4481},{"kitchen-sink",4482},{"hardcore",4479},{"multiplayer",4487}};
  public static string SearchAddress(CatalogQuery q,string source){
   if(q.Kind!="mod"&&q.Kind!="modpack"||q.Page<0||q.Page>500)throw new InvalidDataException("搜索条件无效。");
   if(source=="modrinth"){
    var facets=new List<string[]>{new[]{"project_type:"+q.Kind}};if(q.Game!="")facets.Add(new[]{"versions:"+q.Game});if(q.Loader!="")facets.Add(new[]{"categories:"+q.Loader});if(q.Category!="")facets.Add(new[]{"categories:"+q.Category});
    return "https://api.modrinth.com/v2/search?limit=20&offset="+q.Page*20+"&index="+(String.IsNullOrWhiteSpace(q.Text)?"downloads":"relevance")+"&query="+Q(q.Text)+"&facets="+Q(Encode(facets));
   }
   int cat;return "cf:mods/search?gameId=432&classId="+(q.Kind=="mod"?6:4471)+"&pageSize=20&index="+q.Page*20+"&sortField="+(String.IsNullOrWhiteSpace(q.Text)?6:1)+"&sortOrder=desc&searchFilter="+Q(q.Text)+(q.Game==""?"":"&gameVersion="+Q(q.Game))+(q.Loader==""?"":"&modLoaderType="+LoaderNumber(q.Loader))+(Categories.TryGetValue(q.Category,out cat)?"&categoryId="+cat:"");
  }
  sealed class MrSearch {public MrProject[] hits;public int total_hits;}
  sealed class MrProject {public string project_id,slug,title,description,icon_url,date_modified,project_type;public long downloads;public string[] versions,categories;}
  sealed class CfResponse {public CfProject[] data;public Pagination pagination;}
  sealed class Pagination {public int totalCount,index,resultCount,pageSize;}
  sealed class CfProject {public string name,slug,summary,dateModified;public int id,gameId,classId;public long downloadCount;public CfLogo logo;public CurseLinks links;}
  sealed class CfLogo {public string thumbnailUrl,url;}
  public async Task<CatalogResults> Search(CatalogQuery q,CancellationToken ct){
   string[] sources=q.Source=="all"?new[]{"modrinth","curseforge"}:new[]{q.Source};if(sources.Any(s=>s!="modrinth"&&s!="curseforge"))throw new InvalidDataException("不支持此搜索来源。");
   var results=await Task.WhenAll(sources.Select(async source=>{try{return await SearchOne(q,source,ct).ConfigureAwait(false);}catch(OperationCanceledException){ct.ThrowIfCancellationRequested();return new CatalogResults{Projects=new CatalogProject[0],Warning=source+"：查询超时"};}catch(Exception ex){return new CatalogResults{Projects=new CatalogProject[0],Warning=source+"："+ex.Message};}})).ConfigureAwait(false);
   var projects=new List<CatalogProject>();for(int n=0;n<20;n++)foreach(var r in results)if(r.Projects.Length>n){r.Projects[n].FilterGame=q.Game;r.Projects[n].FilterLoader=q.Loader;projects.Add(r.Projects[n]);}
   return new CatalogResults{Projects=projects.ToArray(),More=results.Any(r=>r.More),Warning=String.Join("\n",results.Select(r=>r.Warning).Where(s=>s!=""))};
  }
  async Task<CatalogResults> SearchOne(CatalogQuery q,string source,CancellationToken ct){
   string json=await Fetch(SearchAddress(q,source),ct).ConfigureAwait(false);
   if(source=="modrinth"){var r=Json.Parse<MrSearch>(json);return new CatalogResults{More=(q.Page+1)*20<r.total_hits,Projects=(r.hits??new MrProject[0]).Where(p=>p.project_type==q.Kind).Select(p=>new CatalogProject{Source=source,Id=Id(p.project_id),Kind=q.Kind,Slug=p.slug,Name=p.title,Summary=p.description,IconUrl=p.icon_url,Website="https://modrinth.com/"+q.Kind+"/"+Id(p.slug??p.project_id),Downloads=p.downloads,Updated=p.date_modified,GameVersions=p.versions,Loaders=(p.categories??new string[0]).Where(x=>LoaderNumber(x)>0).ToArray()}).ToArray()};}
   else{var r=Json.Parse<CfResponse>(json);return new CatalogResults{More=r.pagination!=null&&(q.Page+1)*20<r.pagination.totalCount,Projects=(r.data??new CfProject[0]).Where(p=>p.gameId==432&&p.classId==(q.Kind=="mod"?6:4471)).Select(p=>new CatalogProject{Source=source,Id=p.id.ToString(),Kind=q.Kind,Slug=p.slug,Name=p.name,Summary=p.summary,IconUrl=p.logo==null?null:p.logo.thumbnailUrl??p.logo.url,Website="https://www.curseforge.com/minecraft/"+(q.Kind=="mod"?"mc-mods/":"modpacks/")+Id(p.slug),Downloads=p.downloadCount,Updated=p.dateModified}).ToArray()};}
  }
  sealed class MrVersion {public string id,project_id,name,version_number,date_published,version_type;public string[] game_versions,loaders;public MrFile[] files;public MrDependency[] dependencies;}
  sealed class MrFile {public string filename,url,file_type;public long size;public bool primary;public Dictionary<string,string> hashes;}
  sealed class MrDependency {public string project_id,version_id,dependency_type;}
  sealed class CfFiles {public CfFile[] data;}
  sealed class CfSingle {public CfFile data;}
  sealed class CfFile {public int id,modId,gameId,fileStatus,releaseType;public string displayName,fileName,downloadUrl,fileDate;public long fileLength;public bool isAvailable,isServerPack;public string[] gameVersions;public CurseHash[] hashes;public CfDep[] dependencies;}
  sealed class CfDep {public int modId,relationType;}
  static CatalogVersion Mr(MrVersion v){var f=(v.files??new MrFile[0]).FirstOrDefault(x=>x.primary&&x.file_type==null)??(v.files??new MrFile[0]).FirstOrDefault(x=>x.file_type==null);return new CatalogVersion{Source="modrinth",ProjectId=Id(v.project_id),Id=Id(v.id),Name=v.name,Number=v.version_number,Published=v.date_published,Channel=v.version_type,GameVersions=v.game_versions,Loaders=v.loaders,File=f==null?null:new CatalogArtifact{Name=f.filename,Size=f.size,Url=f.url,Hashes=f.hashes},Dependencies=(v.dependencies??new MrDependency[0]).Select(d=>new CatalogDependency{Project=d.project_id,Version=d.version_id,Type=d.dependency_type}).ToArray()};}
  static CatalogVersion Cf(CfFile f){
   CurseForgePack.ValidateFile(new CurseReference{projectID=f.modId,fileID=f.id},new CurseFile{id=f.id,modId=f.modId,gameId=f.gameId,fileStatus=f.fileStatus,fileLength=f.fileLength,fileName=f.fileName,downloadUrl=f.downloadUrl,hashes=f.hashes,isAvailable=f.isAvailable});
   return new CatalogVersion{Source="curseforge",ProjectId=f.modId.ToString(),Id=f.id.ToString(),Name=f.displayName,Number=f.fileName,Published=f.fileDate,Channel=f.releaseType==1?"release":f.releaseType==2?"beta":"alpha",GameVersions=(f.gameVersions??new string[0]).Where(v=>Regex.IsMatch(v,@"\A\d[0-9a-zA-Z_.\-+]*\z")).ToArray(),Loaders=(f.gameVersions??new string[0]).Select(NormalizeLoader).Where(l=>LoaderNumber(l)>0).ToArray(),File=new CatalogArtifact{Name=f.fileName,Size=f.fileLength,Url=f.downloadUrl,Hashes=(f.hashes??new CurseHash[0]).Where(h=>h.algo==1).GroupBy(h=>h.algo).ToDictionary(h=>"sha1",h=>h.First().value)},Dependencies=(f.dependencies??new CfDep[0]).Select(d=>new CatalogDependency{Project=d.modId.ToString(),Type=d.relationType==3?"required":d.relationType==5?"incompatible":"optional"}).ToArray()};
  }
  public static bool Compatible(CatalogVersion v,string game,string loader){return v!=null&&(game==""||(v.GameVersions??new string[0]).Contains(game))&&(loader==""||(v.Loaders??new string[0]).Contains(loader));}
  public async Task<CatalogVersion[]> Versions(CatalogProject p,string game,string loader,int offset,CancellationToken ct){
   Id(p.Id);if(p.Source=="modrinth"){string url="https://api.modrinth.com/v2/project/"+p.Id+"/version?include_changelog=false"+(game==""?"":"&game_versions="+Q(Encode(new[]{game})))+(loader==""?"":"&loaders="+Q(Encode(new[]{loader})));var versions=Json.Parse<MrVersion[]>(await Fetch(url,ct).ConfigureAwait(false));return (versions??new MrVersion[0]).Select(Mr).Where(v=>v.ProjectId==p.Id&&Compatible(v,game,loader)).Skip(offset).Take(40).ToArray();}
   var r=Json.Parse<CfFiles>(await Fetch("cf:mods/"+p.Id+"/files?pageSize=40&index="+offset+(game==""?"":"&gameVersion="+Q(game))+(loader==""?"":"&modLoaderType="+LoaderNumber(loader)),ct).ConfigureAwait(false));
   var result=new List<CatalogVersion>();foreach(var file in r.data??new CfFile[0]){if(file.isServerPack||file.modId.ToString()!=p.Id)continue;try{var v=Cf(file);if(Compatible(v,game,loader))result.Add(v);}catch(InvalidDataException){}}return result.ToArray();
  }
  public async Task<CatalogVersion> Version(string source,string project,string version,CancellationToken ct){Id(version);if(source=="modrinth"){var v=Mr(Json.Parse<MrVersion>(await Fetch("https://api.modrinth.com/v2/version/"+version,ct).ConfigureAwait(false)));if(v.Id!=version||!String.IsNullOrEmpty(project)&&v.ProjectId!=project)throw new IOException("资源版本与项目不匹配。");return v;}if(source!="curseforge")throw new IOException("不支持此资源来源。");Id(project);var cf=Json.Parse<CfSingle>(await Fetch("cf:mods/"+project+"/files/"+version,ct).ConfigureAwait(false)).data;if(cf.isServerPack||cf.modId.ToString()!=project||cf.id.ToString()!=version)throw new IOException("资源版本与项目不匹配。");return Cf(cf);}
 }
 #pragma warning restore 0649
}
