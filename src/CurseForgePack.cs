using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class CurseManifest {public string manifestType,name,version,author,overrides;public int manifestVersion;public CurseMinecraft minecraft;public CurseReference[] files;}
 public sealed class CurseMinecraft {public string version;public CurseLoader[] modLoaders;}
 public sealed class CurseLoader {public string id;public bool primary;}
 public sealed class CurseReference {public int projectID,fileID;public bool required=true;}
 public sealed class CurseHash {public string value;public int algo;}
 public sealed class CurseFile {public int id,modId,gameId;public string fileName,downloadUrl;public long fileLength;public CurseHash[] hashes;public bool isAvailable;}
 public sealed class CurseFileResponse {public CurseFile data;}
 public sealed class CurseLinks {public string websiteUrl;}
 public sealed class CurseProject {public int id,gameId,classId;public CurseLinks links;}
 public sealed class CurseProjectResponse {public CurseProject data;}
 public sealed class CurseForgePack {
  readonly string Cache;readonly Func<string,CancellationToken,Task<string>> Fetch;readonly SemaphoreSlim ProjectGate=new SemaphoreSlim(1);
  static readonly HttpClient Http=CreateHttp();
  static HttpClient CreateHttp(){var client=new HttpClient(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=TimeSpan.FromSeconds(40)};client.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.1.9");return client;}
  public CurseForgePack(string data):this(data,Get){}
  internal CurseForgePack(string data,Func<string,CancellationToken,Task<string>> fetch){Cache=Path.Combine(data,"metadata","curseforge");Fetch=fetch;}
  static async Task<string> Get(string path,CancellationToken ct) {
   using(var response=await Http.GetAsync("https://api.curse.tools/v1/cf/"+path,ct).ConfigureAwait(false)) {
    if(!response.IsSuccessStatusCode)throw new IOException("CurseForge 文件信息查询失败（CurseTools 代理 HTTP "+(int)response.StatusCode+"），请稍后重试。");
    string body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(body.Length>4*1024*1024)throw new InvalidDataException("CurseForge 文件信息过大。");return body;
   }
  }
  public static string OverridePrefix(CurseManifest manifest) {
   string relative=(manifest.overrides??"overrides").Replace('\\','/').TrimEnd('/');PackService.SafePath(Path.GetTempPath(),relative);return relative+"/";
  }
  public static Dictionary<string,string> Dependencies(CurseManifest manifest) {
   if(manifest==null||manifest.manifestType!="minecraftModpack"||manifest.manifestVersion!=1||manifest.minecraft==null||manifest.files==null||manifest.files.Length>10000)throw new InvalidDataException("无效的 CurseForge 整合包清单。");
   OverridePrefix(manifest);var result=new Dictionary<string,string>{{"minecraft",manifest.minecraft.version}};
   var loaders=manifest.minecraft.modLoaders??new CurseLoader[0];var primary=loaders.Where(l=>l!=null&&l.primary).ToArray();
   if(primary.Length>1||primary.Length==0&&loaders.Length>1)throw new InvalidDataException("CurseForge 整合包没有明确的主加载器。");
   var loader=primary.FirstOrDefault()??loaders.SingleOrDefault();
   if(loader!=null) {
    string id=loader.id??"";var names=new Dictionary<string,string>{{"forge-","forge"},{"neoforge-","neoforge"},{"fabric-loader-","fabric-loader"},{"fabric-","fabric-loader"},{"quilt-loader-","quilt-loader"},{"quilt-","quilt-loader"}};
    var kind=names.FirstOrDefault(n=>id.StartsWith(n.Key,StringComparison.OrdinalIgnoreCase));if(kind.Key==null)throw new InvalidDataException("暂不支持此 CurseForge 加载器："+id);result[kind.Value]=id.Substring(kind.Key.Length);
   }
   return result;
  }
  internal static CurseFile ValidateFile(CurseReference reference,CurseFile file) {
   if(file==null||file.id!=reference.fileID||file.modId!=reference.projectID||file.gameId!=432||!file.isAvailable||file.fileLength<0||file.fileLength>4L*1024*1024*1024)throw new InvalidDataException("CurseForge 文件编号或大小无效："+reference.projectID+" / "+reference.fileID);
   if(String.IsNullOrWhiteSpace(file.fileName)||file.fileName.IndexOfAny(new[]{'/','\\'})>=0)throw new InvalidDataException("CurseForge 文件名无效。");PackService.SafePath(Path.GetTempPath(),file.fileName);
   if(file.hashes==null||!file.hashes.Any(h=>h!=null&&h.algo==1&&Regex.IsMatch(h.value??"",@"\A[0-9a-fA-F]{40}\z")))throw new InvalidDataException("CurseForge 文件缺少 SHA-1 校验值："+file.fileName);
   if(!String.IsNullOrWhiteSpace(file.downloadUrl)){Uri uri;if(!Uri.TryCreate(file.downloadUrl,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("CurseForge 文件下载地址不是有效 HTTPS 地址。");}
   return file;
  }
  async Task<CurseFile> FileInfo(CurseReference reference,CancellationToken ct) {
   string key=reference.projectID+"-"+reference.fileID+".json",path=Path.Combine(Cache,key);
   if(System.IO.File.Exists(path)){try{return ValidateFile(reference,Json.Read<CurseFileResponse>(path).data);}catch(InvalidDataException){}catch(ArgumentException){}}
   Exception last=null;
   for(int attempt=0;attempt<2;attempt++) {
    ct.ThrowIfCancellationRequested();
    try{string text=await Fetch("mods/"+reference.projectID+"/files/"+reference.fileID,ct).ConfigureAwait(false);var file=ValidateFile(reference,Json.Parse<CurseFileResponse>(text).data);Directory.CreateDirectory(Cache);Json.Write(path,new CurseFileResponse{data=file});return file;}
    catch(OperationCanceledException){ct.ThrowIfCancellationRequested();last=new IOException("CurseForge 文件查询超时。");}
    catch(HttpRequestException){last=new IOException("无法连接 CurseForge 文件信息代理。");}
    catch(IOException ex){last=ex;}
   }
   throw new IOException("无法解析 CurseForge 文件 "+reference.projectID+" / "+reference.fileID+"："+(last==null?"未知错误":last.Message));
  }
  async Task<CurseProject> ProjectInfo(CurseReference reference,CancellationToken ct) {
   await ProjectGate.WaitAsync(ct).ConfigureAwait(false);try {
   string path=Path.Combine(Cache,"project-"+reference.projectID+".json");CurseProject project=null;
   if(System.IO.File.Exists(path)){try{project=Json.Read<CurseProjectResponse>(path).data;}catch(ArgumentException){}}
   if(project==null||project.id!=reference.projectID||project.gameId!=432){project=Json.Parse<CurseProjectResponse>(await Fetch("mods/"+reference.projectID,ct).ConfigureAwait(false)).data;if(project==null||project.id!=reference.projectID||project.gameId!=432)throw new InvalidDataException("CurseForge 项目信息不匹配。");Directory.CreateDirectory(Cache);Json.Write(path,new CurseProjectResponse{data=project});}return project;
   }finally{ProjectGate.Release();}
  }
  async Task<string> DirectoryFor(CurseReference reference,CurseFile file,CancellationToken ct) {
   if(file.fileName.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))return "mods/";
   if(!file.fileName.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("暂不支持此 CurseForge 文件类型："+file.fileName);
   var project=await ProjectInfo(reference,ct).ConfigureAwait(false);
   if(project.classId==12)return "resourcepacks/";if(project.classId==6552)return "shaderpacks/";if(project.classId==6)return "mods/";
   throw new InvalidDataException("暂不支持此 CurseForge 项目类别："+project.classId);
  }
  public async Task<PackIndex> Resolve(CurseManifest manifest,IProgress<Update> progress,CancellationToken ct) {
   var dependencies=Dependencies(manifest);var references=manifest.files.Where(f=>f!=null&&f.required).ToArray();var seen=new HashSet<int>();
   foreach(var reference in manifest.files){if(reference==null||reference.projectID<=0||reference.fileID<=0||!seen.Add(reference.fileID))throw new InvalidDataException("CurseForge 清单含有无效或重复文件编号。");}
   int done=0;using(var gate=new SemaphoreSlim(6)) {
    var files=await Task.WhenAll(references.Select(async reference=>{
     await gate.WaitAsync(ct).ConfigureAwait(false);
     try {
      var file=await FileInfo(reference,ct).ConfigureAwait(false);string folder=await DirectoryFor(reference,file,ct).ConfigureAwait(false);
      string sha=file.hashes.First(h=>h!=null&&h.algo==1&&Regex.IsMatch(h.value??"",@"\A[0-9a-fA-F]{40}\z")).value;
      string manual=null;if(String.IsNullOrWhiteSpace(file.downloadUrl)){var project=await ProjectInfo(reference,ct).ConfigureAwait(false);string site=project.links==null?null:project.links.websiteUrl;Uri uri;if(!Uri.TryCreate(site,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="www.curseforge.com"||!String.IsNullOrEmpty(uri.UserInfo)||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("缺少有效的 CurseForge 手动下载网页。");manual=site.TrimEnd('/')+"/download/"+reference.fileID;}
      return new PackFile{path=folder+file.fileName,fileSize=file.fileLength,hashes=new Dictionary<string,string>{{"sha1",sha}},downloads=String.IsNullOrWhiteSpace(file.downloadUrl)?new string[0]:new[]{file.downloadUrl},manualUrl=manual};
     }finally{int count=Interlocked.Increment(ref done);progress.Report(new Update("CurseForge 文件信息已解析 "+count+" / "+references.Length+"（CurseTools 公共代理）",count,references.Length));gate.Release();}
    })).ConfigureAwait(false);
    return new PackIndex{game="minecraft",formatVersion=1,name=manifest.name,versionId=manifest.version,summary="从 CurseForge 整合包导入；文件信息由 CurseTools 公共代理解析。",dependencies=dependencies,files=files};
   }
  }
 }
}
