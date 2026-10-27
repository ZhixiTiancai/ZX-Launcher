using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class JavaRelease {
  public int Major;public string Version,Name,Url,Sha256;public long Size;
  public override string ToString(){return "Temurin "+Version+" · Windows x64 JRE · "+(Size/1048576.0).ToString("0.0")+" MB";}
 }
 public static class JavaDownload {
  public static readonly int[] Majors={8,11,16,17,21,25};
  static readonly HttpClient Http=Create();
  static HttpClient Create(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.3.2 (+https://github.com/ZhixiTiancai/ZX-Launcher)");return http;}
  static Uri Allowed(string url,bool api){var u=CatalogNetwork.Https(url);bool okay=api?u.Host=="api.adoptium.net":u.Host=="github.com"&&Regex.IsMatch(u.AbsolutePath,@"\A/adoptium/temurin\d+-binaries/releases/download/")||u.Host=="release-assets.githubusercontent.com"||u.Host=="objects.githubusercontent.com";if(!okay)throw new IOException(UiText.T("Java 下载地址不是受支持的官方来源。"));return u;}
  static async Task Read(string url,Stream output,long max,bool api,IProgress<Update> progress,CancellationToken ct){Uri u=Allowed(url,api);using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)){timeout.CancelAfter(TimeSpan.FromSeconds(api?30:900));for(int i=0;i<6;i++)using(var response=await Http.GetAsync(u,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
   if((int)response.StatusCode>=300&&(int)response.StatusCode<400){if(response.Headers.Location==null)throw new IOException("Java redirect missing");u=Allowed(new Uri(u,response.Headers.Location).AbsoluteUri,api);continue;}response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>max)throw new IOException("Java package exceeds size limit");
   using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)){var buffer=new byte[65536];long total=0;int n;DateTime last=DateTime.MinValue;while((n=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0){total+=n;if(total>max)throw new IOException("Java package exceeds size limit");await output.WriteAsync(buffer,0,n,timeout.Token).ConfigureAwait(false);if(progress!=null&&(DateTime.UtcNow-last).TotalMilliseconds>200){last=DateTime.UtcNow;progress.Report(new Update(UiText.T("正在下载 Java… ")+(total/1048576.0).ToString("0.0")+" / "+(max/1048576.0).ToString("0.0")+" MB",(int)(total/1024),(int)Math.Max(1,max/1024)));}}}return;
  }throw new IOException("Too many Java redirects");}}
  #pragma warning disable 0649
  sealed class Asset {public Binary binary;public VersionData version;}
  sealed class Binary {public string architecture,os,image_type;public Package package;}
  sealed class VersionData {public int major;public string semver;}
  sealed class Package {public string name,link,checksum;public long size;}
  #pragma warning restore 0649
  public static async Task<JavaRelease> Latest(int major,CancellationToken ct){if(!Majors.Contains(major))throw new IOException("Unsupported Java version");string json;using(var memory=new MemoryStream()){await Read("https://api.adoptium.net/v3/assets/latest/"+major+"/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse",memory,1024*1024,true,null,ct).ConfigureAwait(false);json=System.Text.Encoding.UTF8.GetString(memory.ToArray());}return Parse(json,major);}
  internal static JavaRelease Parse(string json,int major){var asset=(Json.Parse<Asset[]>(json)??new Asset[0]).FirstOrDefault(a=>a.version!=null&&a.version.major==major&&a.binary!=null&&a.binary.os=="windows"&&a.binary.architecture=="x64"&&a.binary.image_type=="jre"&&a.binary.package!=null&&a.binary.package.name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase));if(asset==null)throw new IOException(UiText.T("这个版本暂时没有 Windows 64 位 JRE 下载，请选择其他版本。"));var p=asset.binary.package;var r=new JavaRelease{Major=major,Version=asset.version.semver,Name=p.name,Url=p.link,Size=p.size,Sha256=p.checksum};Validate(r);return r;}
  static void Validate(JavaRelease r){if(r==null||!Majors.Contains(r.Major)||!Regex.IsMatch(r.Version??"",@"\A[0-9a-zA-Z._+\-]{1,80}\z")||r.Size<1024||r.Size>512L*1024*1024||!Regex.IsMatch(r.Sha256??"",@"\A[0-9a-fA-F]{64}\z")||Path.GetFileName(r.Name)!=r.Name||!r.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid Java release metadata");Allowed(r.Url,false);}
  public static async Task<JavaRuntime> Install(string root,JavaRelease release,IProgress<Update> progress,CancellationToken ct){Validate(release);string staging=Path.Combine(root,"runtime",".downloads",Guid.NewGuid().ToString("N"));LauncherUpdateCore.NoLinks(staging);Directory.CreateDirectory(staging);try{string zip=Path.Combine(staging,"java.zip");using(var file=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))await Read(release.Url,file,release.Size,false,progress,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(new FileInfo(zip).Length!=release.Size||!String.Equals(LauncherUpdateCore.HashFile(zip),release.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException(UiText.T("Java 下载校验失败，未安装。"));return await Task.Run(()=>Extract(root,zip,release,Path.Combine(staging,"unpacked"),progress,ct),ct).ConfigureAwait(false);}finally{try{LauncherUpdateCore.NoLinks(staging);if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}}}
  internal static JavaRuntime Extract(string root,string zip,JavaRelease release,string staging,IProgress<Update> progress,CancellationToken ct){
   Validate(release);LauncherUpdateCore.NoLinks(staging);Directory.CreateDirectory(staging);progress.Report(new Update(UiText.T("正在解压并检查 Java…")));long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var archive=ZipFile.OpenRead(zip)){if(archive.Entries.Count>30000)throw new InvalidDataException("Too many Java archive entries");foreach(var entry in archive.Entries){ct.ThrowIfCancellationRequested();string name=entry.FullName;if(name.Contains("\\")||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Java archive link or invalid path");string path=PackService.SafePath(staging,name.TrimEnd('/'));if(!names.Add(path))throw new InvalidDataException("Duplicate Java archive path");LauncherUpdateCore.NoLinks(path);if(name.EndsWith("/",StringComparison.Ordinal)){Directory.CreateDirectory(path);continue;}total+=entry.Length;if(entry.Length>512L*1024*1024||total>2L*1024*1024*1024)throw new InvalidDataException("Java archive too large");Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew)){var bytes=new byte[65536];long copied=0;int n;while((n=input.Read(bytes,0,bytes.Length))>0){ct.ThrowIfCancellationRequested();copied+=n;if(copied>entry.Length)throw new InvalidDataException("Invalid Java entry size");output.Write(bytes,0,n);}}}}
   var candidates=JavaManager.ScanFolders(new[]{staging},3,ct);var java=candidates.FirstOrDefault(j=>j.Major==release.Major&&j.Architecture=="x64");if(java==null)throw new IOException(UiText.T("Java 压缩包中没有可用的 64 位运行时。"));string home=Directory.GetParent(Path.GetDirectoryName(java.Path)).FullName;
   string target=Path.Combine(root,"runtime","temurin-"+release.Major+"-"+release.Version.Replace('+','_')+"-"+release.Sha256.Substring(0,8));LauncherUpdateCore.NoLinks(target);ct.ThrowIfCancellationRequested();if(Directory.Exists(target)){var existing=JavaManager.Inspect(Path.Combine(target,"bin","javaw.exe"));if(existing==null||existing.Major!=release.Major||existing.Architecture!="x64")throw new IOException("Existing Java folder is invalid; preserved without replacement");return existing;}
   Directory.Move(home,target);java=JavaManager.Inspect(Path.Combine(target,"bin",Path.GetFileName(java.Path)));if(java==null)throw new IOException("Installed Java could not be read");return java;
  }
 }
}
