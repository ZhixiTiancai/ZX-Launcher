using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Net.Http;using System.Net;using System.Text;using System.Threading;using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class LoaderSelection {
  public string Key {get;set;}public string Version {get;set;}public bool Recommended {get;set;}public string Type {get;set;}
  public string Name {get{return Key=="forge"?"Forge":Key=="neoforge"?"NeoForge":Key=="fabric-loader"?"Fabric":Key=="quilt-loader"?"Quilt":"原版";}}
  public string Uid {get{return Key=="forge"?"net.minecraftforge":Key=="neoforge"?"net.neoforged":Key=="fabric-loader"?"net.fabricmc.fabric-loader":Key=="quilt-loader"?"org.quiltmc.quilt-loader":null;}}
  public override string ToString(){return Version+(Recommended?"  ·  推荐":"")+(Type=="beta"||Type=="alpha"?"  ·  "+Type:"");}
  public void Validate(){if(Uid==null||String.IsNullOrWhiteSpace(Version))throw new InvalidDataException("请选择有效的模组加载器版本。");VanillaService.VersionId(Version);}
 }
 public sealed class LoaderCatalog {
  readonly Func<string,CancellationToken,Task<byte[]>> Fetch;public LoaderCatalog():this(Read){}internal LoaderCatalog(Func<string,CancellationToken,Task<byte[]>> fetch){Fetch=fetch;}
  static readonly HttpClient Http=Create();static HttpClient Create(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var h=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=Timeout.InfiniteTimeSpan};h.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.3.6 (+https://github.com/ZhixiTiancai/ZX-Launcher)");return h;}
  static async Task<byte[]> Read(string url,CancellationToken ct){var uri=CatalogNetwork.Https(url);if(!new[]{"meta.prismlauncher.org","meta.fabricmc.net","meta.quiltmc.org"}.Contains(uri.Host))throw new InvalidDataException("加载器元数据来源无效。");using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)){timeout.CancelAfter(TimeSpan.FromSeconds(30));using(var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>16*1024*1024)throw new IOException("加载器清单过大。");using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){var b=new byte[32768];int n;while((n=await input.ReadAsync(b,0,b.Length,timeout.Token).ConfigureAwait(false))>0){if(output.Length+n>16*1024*1024)throw new IOException("加载器清单过大。");output.Write(b,0,n);}return output.ToArray();}}}}
  sealed class Entry {public string version {get;set;}public string sha256 {get;set;}public MetaRequirement[] requires {get;set;}public bool recommended {get;set;}public string type {get;set;}}
  sealed class Index {public string uid {get;set;}public Entry[] versions {get;set;}}
  sealed class Upstream {public UpstreamLoader loader {get;set;}}sealed class UpstreamLoader {public string version {get;set;}}
  static T Parse<T>(byte[] bytes){return Json.Parse<T>(Encoding.UTF8.GetString(bytes));}
  async Task<Index> IndexFor(string uid,MetaRoot root,CancellationToken ct){var package=root.packages.FirstOrDefault(p=>p.uid==uid);if(package==null)return new Index{uid=uid,versions=new Entry[0]};byte[] bytes=await Fetch("https://meta.prismlauncher.org/v1/"+uid+"/index.json",ct);if(!MetadataCache.Matches(bytes,package.sha256))throw new IOException("加载器清单 SHA-256 校验失败，请重新查询。");var index=Parse<Index>(bytes);if(index.uid!=uid||index.versions==null)throw new IOException("加载器清单编号无效。");return index;}
  public async Task<LoaderSelection[]> Versions(string key,string minecraft,CancellationToken ct){VanillaService.VersionId(minecraft);var selection=new LoaderSelection{Key=key,Version="0"};selection.Validate();var root=Parse<MetaRoot>(await Fetch("https://meta.prismlauncher.org/v1/index.json",ct));if(root==null||root.formatVersion!=1||root.packages==null)throw new IOException("加载器根索引无效。");var index=await IndexFor(selection.Uid,root,ct);IEnumerable<Entry> entries=index.versions;
   if(key=="forge"||key=="neoforge")entries=entries.Where(e=>(e.requires??new MetaRequirement[0]).Any(r=>r.uid=="net.minecraft"&&r.equals==minecraft));
   else {string prefix=key=="fabric-loader"?"https://meta.fabricmc.net/v2/versions/loader/":"https://meta.quiltmc.org/v3/versions/loader/";var upstream=Parse<Upstream[]>(await Fetch(prefix+Uri.EscapeDataString(minecraft),ct));var supported=new HashSet<string>((upstream??new Upstream[0]).Where(u=>u.loader!=null).Select(u=>u.loader.version));entries=entries.Where(e=>supported.Contains(e.version));}
   var result=entries.Select(e=>new LoaderSelection{Key=key,Version=e.version,Recommended=e.recommended,Type=e.type}).ToArray();foreach(var item in result)item.Validate();return result.OrderByDescending(e=>e.Recommended).ToArray();
  }
 }
}
