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
namespace FoolLauncher {
 // Names only. Project IDs, summaries, version labels and JAR names stay original.
 public sealed class ModNameTranslations {
  public const string SourceUrl="https://raw.githubusercontent.com/Meloong-Git/PCL/main/PCLCS/Resource/WikiEntries.txt";
  readonly string CacheFile;readonly object Sync=new object();Task RefreshTask;DateTime Updated,Attempt;
  Dictionary<string,int> WikiIds=new Dictionary<string,int>();
  Dictionary<string,string> Names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string,string> Common=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
   {"sodium","钠"},{"iris","鸢尾"},{"entityculling","实体渲染机制优化"},{"cloth-config","布料配置 API"},{"cloth-config-legacy","布料配置 API"},{"fabric-api","Fabric 接口库"},{"jei","物品管理器"},{"just-enough-items-jei","物品管理器"},{"mouse-tweaks","鼠标手势"},{"ferrite-core","铁氧体磁芯"},{"ferritecore","铁氧体磁芯"},{"ferritecore-fabric","铁氧体磁芯"},{"jade","玉"},{"jade-fabric","玉"},{"modmenu","模组菜单"},{"mod-menu","模组菜单"},{"architectury-api","建筑师 API"},{"lithium","锂"},{"lithium-fabric","锂"},{"sodium-extra","钠 · 扩展"},{"modernfix","现代化修复"},{"appleskin","苹果皮"},{"create","机械动力"},{"create-fabric","机械动力"},{"mekanism","通用机械"},{"journeymap","旅行地图"},{"xaeros-minimap","Xaero 的小地图"},{"xaeros-world-map","Xaero 的世界地图"},{"waystones","传送石碑"},{"inventory-profiles-next","一键背包整理 Next"},{"roughly-enough-items","粗略物品管理器"},{"roughly-enough-items-fabric","粗略物品管理器"},{"emi","EMI 物品管理器"},{"indium","铟"},{"starlight","星光"},{"starlight-forge","星光"},{"spark","火花"},{"betterf3","更好的 F3"},{"debugify","调试修复"},{"immediatelyfast","即时优化"},{"dynamic-fps","动态帧率"},{"inventory-sorter","背包整理"},{"controlling","键位控制"},{"clumps","经验机制改革"}
  };
  public sealed class Cache {public int Format;public string Source,UpdatedUtc;public Dictionary<string,string> Names;public Dictionary<string,int> WikiIds;}
  public ModNameTranslations(string data){CacheFile=Path.Combine(data,"metadata","mod-names-v1.json");try{if(File.Exists(CacheFile)&&new FileInfo(CacheFile).Length<=5*1024*1024){var c=Json.Read<Cache>(CacheFile);if(c.Format==1&&c.Source==SourceUrl&&c.Names!=null&&c.Names.Count<=120000){Names=Clean(c.Names);WikiIds=c.WikiIds??new Dictionary<string,int>();DateTime.TryParse(c.UpdatedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out Updated);if(c.WikiIds==null||Updated>DateTime.UtcNow.AddDays(1))Updated=DateTime.MinValue;}}}catch{}}
  static Dictionary<string,string> Clean(Dictionary<string,string> input){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var p in input)if(Regex.IsMatch(p.Key??"",@"\A(modrinth|curseforge):[a-zA-Z0-9_\-]{1,100}\z")&&Valid(p.Value))result[p.Key]=p.Value;return result;}
  static bool Valid(string value){return !String.IsNullOrWhiteSpace(value)&&value.Length<=140&&!value.Any(Char.IsControl)&&Regex.IsMatch(value,@"[\u3400-\u9fff]");}
  internal static Dictionary<string,string> Parse(string text){
   if(text==null||text.Length>3*1024*1024)throw new InvalidDataException("名称对照表大小无效。");var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var lines=text.TrimEnd('\r','\n').Split('\n');
   // The upstream's final line is an encoded popularity index, not a name record.
   for(int n=0;n<lines.Length-1;n++)foreach(string entry in lines[n].TrimEnd('\r').Split('¨')){
    string[] parts=entry.Split('|');if(parts.Length<2)continue;string raw=parts[parts.Length-1].Trim(),name=raw.Replace("*","");int english=name.IndexOf(" (",StringComparison.Ordinal);if(english>=0)name=name.Substring(0,english);name=name.Trim();if(!Valid(name))continue;
    string[] slugs=parts[0].Split('@');Action<string,string> add=(source,slug)=>{if(Regex.IsMatch(slug,@"\A[a-zA-Z0-9_\-]{1,100}\z"))result[source+":"+slug]=name;};
    if(slugs.Length==1)add("curseforge",slugs[0]);else if(slugs.Length==2){if(slugs[0]!="")add("curseforge",slugs[0]);if(slugs[1]!="")add("modrinth",slugs[1]);else if(slugs[0]!="")add("modrinth",slugs[0]);}
   }return result;
  }
  public int Wiki(string source,string slug){int id;lock(Sync)return WikiIds.TryGetValue(source+":"+slug,out id)&&id>0&&id<1000000?id:0;}
  public string Lookup(string source,string slug){if(String.IsNullOrEmpty(slug)||(source!="modrinth"&&source!="curseforge"))return null;string name;lock(Sync){if(Names.TryGetValue(source+":"+slug,out name))return name;}return Common.TryGetValue(slug,out name)?name:null;}
  public static bool Chinese(string text){return Regex.IsMatch(text??"",@"[\u3400-\u9fff]");}
  static string SearchKey(string value){return Regex.Replace((value??"").ToLowerInvariant(),@"[\s\p{P}\p{S}]","");}
  public string[] SearchSlugs(string source,string text){
   string key=SearchKey(text);if(key.Length==0)return new string[0];var items=new Dictionary<string,string>(Common,StringComparer.OrdinalIgnoreCase);lock(Sync){foreach(var p in Names)if(p.Key.StartsWith(source+":",StringComparison.Ordinal))items[p.Key.Substring(source.Length+1)]=p.Value;}
   return items.Select(p=>new{Slug=p.Key,Name=SearchKey(p.Value)}).Where(p=>p.Name.Contains(key)).OrderBy(p=>p.Name==key?0:p.Name.StartsWith(key,StringComparison.Ordinal)?1:2).ThenBy(p=>p.Name.Length).ThenBy(p=>p.Slug,StringComparer.OrdinalIgnoreCase).Select(p=>p.Slug).ToArray();
  }
  public void Apply(IEnumerable<CatalogProject> projects){foreach(var p in projects)p.SetChineseName(p.Kind=="mod"?Lookup(p.Source,p.Slug):null);}
  public Task EnsureAsync(){lock(Sync){if(Updated>DateTime.UtcNow.AddDays(-7)||Attempt>DateTime.UtcNow.AddMinutes(-15))return RefreshTask??Task.FromResult(0);Attempt=DateTime.UtcNow;RefreshTask=Task.Run((Func<Task>)Refresh);return RefreshTask;}}
  async Task Refresh(){try{
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))using(var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate})using(var http=new HttpClient(handler)){
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.3.1 (+https://github.com/ZhixiTiancai/ZX-Launcher)");using(var response=await http.GetAsync(SourceUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
     response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>3*1024*1024)return;using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var memory=new MemoryStream()){
      byte[] buffer=new byte[32768];int n;while((n=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0){if(memory.Length+n>3*1024*1024)return;memory.Write(buffer,0,n);}var names=Parse(Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF'));if(names.Count<500||names.Count>120000)return;
      var wiki=new Dictionary<string,int>();var lines=Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF').Split('\n');for(int i=0;i<lines.Length-1;i++)foreach(var key in Parse(lines[i]+"\nTAIL").Keys)wiki[key]=i+1;DateTime updated=DateTime.UtcNow;lock(Sync){Names=names;WikiIds=wiki;Updated=updated;}try{LauncherUpdateCore.NoLinks(CacheFile);Json.Write(CacheFile,new Cache{Format=1,Source=SourceUrl,UpdatedUtc=updated.ToString("o"),Names=names,WikiIds=wiki});}catch{}
     }
    }
   }
  }catch{/* A missing translation source must never prevent resource searches. */}}
 }
}
