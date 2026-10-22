using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FoolLauncher {
 public sealed class SkinUsage {public string SkinId{get;set;}public string Url{get;set;}public string Variant{get;set;}}
 public sealed class SkinUsageStore {
  readonly string PathName;
  public SkinUsageStore(string root){PathName=Path.Combine(root,"data","skin-usage.json");}
  Dictionary<string,SkinUsage> Read(){return File.Exists(PathName)?Json.Read<Dictionary<string,SkinUsage>>(PathName):new Dictionary<string,SkinUsage>();}
  public string Active(AccountChoice account){if(account==null||account.Type!="MSA")return "";SkinUsage used;var entries=Read();if(!entries.TryGetValue(account.Id,out used)||used==null||!Regex.IsMatch(used.SkinId??"",@"\A[a-f0-9]{64}\z"))return "";var skin=AccountStore.Object(AccountStore.Object(account.Data,"profile"),"skin");return AccountStore.Text(skin,"url")==used.Url&&String.Equals(AccountStore.Text(skin,"variant"),used.Variant,StringComparison.OrdinalIgnoreCase)?used.SkinId:"";}
  internal void Set(string accountId,string skinId,Dictionary<string,object> remote){var entries=Read();entries[accountId]=new SkinUsage{SkinId=skinId,Url=AccountStore.Text(remote,"url"),Variant=AccountStore.Text(remote,"variant")};Json.Write(PathName,entries);}
 }
 public sealed class SkinApplication:IDisposable {
  readonly AccountStore Accounts;readonly SkinStore Skins;readonly SkinUsageStore Usage;readonly HttpClient Http;readonly Func<AccountChoice,CancellationToken,Task<AccountChoice>> Refresh;
  public SkinApplication(string root,AccountStore accounts):this(root,accounts,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false},null){}
  internal SkinApplication(string root,AccountStore accounts,HttpMessageHandler handler,Func<AccountChoice,CancellationToken,Task<AccountChoice>> refresh){Accounts=accounts;Skins=new SkinStore(root);Usage=new SkinUsageStore(root);Http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(45)};Refresh=refresh??RefreshAccount;}
  public void Dispose(){Http.Dispose();}
  async Task<AccountChoice> RefreshAccount(AccountChoice account,CancellationToken ct){using(var auth=new MicrosoftLogin()){var record=await auth.Refresh(account.ClientId,account,new SilentProgress(),ct).ConfigureAwait(false);Accounts.RefreshForSkin(account.Id,record);return Accounts.List().Single(a=>a.Id==account.Id);}}
  sealed class SilentProgress:IProgress<string>{public void Report(string text){}}
  static string Bearer(AccountChoice account){string token=AccountStore.Text(AccountStore.Object(account.Data,"ygg"),"token");if(String.IsNullOrWhiteSpace(token))throw new IOException(UiText.T("账号登录已过期，请重新登录后再应用皮肤。"));return token;}
  static bool Expired(AccountChoice account){long exp;return Int64.TryParse(AccountStore.Text(AccountStore.Object(account.Data,"ygg"),"exp"),out exp)&&exp<=AccountStore.UnixNow()+120;}
  async Task<Dictionary<string,object>> Request(string path,HttpContent content,string token,CancellationToken ct){
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)){deadline.CancelAfter(TimeSpan.FromSeconds(45));var requestToken=deadline.Token;
   using(var request=new HttpRequestMessage(content==null?HttpMethod.Get:HttpMethod.Post,"https://api.minecraftservices.com/minecraft/profile"+path)) {
    request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Content=content;
    try{using(var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,requestToken).ConfigureAwait(false)) {
     if(response.StatusCode==HttpStatusCode.Unauthorized)throw new SkinUnauthorizedException();
     if(!response.IsSuccessStatusCode)throw new IOException(String.Format(UiText.T("官方皮肤服务请求失败（HTTP {0}），未更改使用标记。"),(int)response.StatusCode));
     if(response.Content.Headers.ContentLength>2*1024*1024)throw new IOException("Skin response too large.");
     using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){byte[] bytes=new byte[8192];int n;while((n=await input.ReadAsync(bytes,0,bytes.Length,requestToken).ConfigureAwait(false))>0){if(output.Length+n>2*1024*1024)throw new IOException("Skin response too large.");output.Write(bytes,0,n);}return output.Length==0?null:Json.Parse<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(output.ToArray()));}
    }}catch(HttpRequestException){throw new IOException(UiText.T("无法连接 Minecraft 官方皮肤服务，请检查网络后重试。"));}catch(TaskCanceledException){ct.ThrowIfCancellationRequested();throw new IOException(UiText.T("皮肤服务请求超时，请重试；本地使用标记未更改。"));}
   }
  }
  }
  sealed class SkinUnauthorizedException:IOException {public SkinUnauthorizedException():base(UiText.T("账号登录已过期，请重新登录后再应用皮肤。")){} }
  static void Profile(Dictionary<string,object> profile,string expected){if(profile==null||!String.Equals(AccountStore.Text(profile,"id"),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.T("官方返回的角色与当前账号不一致，已停止应用皮肤。"));}
  static Dictionary<string,object> Active(Dictionary<string,object> profile){object all;if(!profile.TryGetValue("skins",out all)||!(all is IEnumerable))throw new InvalidDataException("Missing active Minecraft skin.");var active=((IEnumerable)all).Cast<object>().Select(x=>x as Dictionary<string,object>).Where(x=>AccountStore.Text(x,"state")=="ACTIVE").ToArray();if(active.Length!=1)throw new InvalidDataException("Invalid active Minecraft skin.");var skin=active[0];Uri url;string variant=AccountStore.Text(skin,"variant");if(!Uri.TryCreate(AccountStore.Text(skin,"url"),UriKind.Absolute,out url)||(url.Scheme!="https"&&url.Scheme!="http")||url.Host!="textures.minecraft.net"||!String.IsNullOrEmpty(url.UserInfo)||!Regex.IsMatch(url.AbsolutePath,@"\A/texture/[a-fA-F0-9]{32,128}\z")||url.Query!=""||url.Fragment!=""||(variant!="CLASSIC"&&variant!="SLIM"))throw new InvalidDataException("Invalid Minecraft skin response.");return new Dictionary<string,object>{{"id",AccountStore.Text(skin,"id")},{"url",url.AbsoluteUri},{"variant",variant}};}
  public async Task<string> Apply(SavedSkin skin,CancellationToken ct){
   var account=Accounts.SkinAccount();byte[] image=File.ReadAllBytes(Skins.ImagePath(skin));SkinStore.Decode(image);string expected=account.Id;
   if(Expired(account))account=await Refresh(account,ct).ConfigureAwait(false);if(account.Id!=expected)throw new InvalidDataException("Refreshed profile mismatch.");
   Dictionary<string,object> before=null;bool unauthorized=false;
   try{before=await Request("",null,Bearer(account),ct).ConfigureAwait(false);}catch(SkinUnauthorizedException){unauthorized=true;}
   if(unauthorized){account=await Refresh(account,ct).ConfigureAwait(false);if(account.Id!=expected)throw new InvalidDataException("Refreshed profile mismatch.");before=await Request("",null,Bearer(account),ct).ConfigureAwait(false);}
   Profile(before,expected);ct.ThrowIfCancellationRequested();if(Accounts.SkinAccount().Id!=expected)throw new IOException(UiText.T("当前账号已切换，请重新选择皮肤。"));
   using(var form=new MultipartFormDataContent()) {
    form.Add(new StringContent(skin.Slim?"slim":"classic"),"variant");var file=new ByteArrayContent(image);file.Headers.ContentType=new MediaTypeHeaderValue("image/png");form.Add(file,"file","skin.png");
    var result=await Request("/skins",form,Bearer(account),ct).ConfigureAwait(false)??await Request("",null,Bearer(account),ct).ConfigureAwait(false);Profile(result,expected);var remote=Active(result);if(AccountStore.Text(remote,"variant")!=(skin.Slim?"SLIM":"CLASSIC"))throw new IOException(UiText.T("官方返回的皮肤模型不一致，请重试。"));
    // Only successful official responses update the account and the single scalar selection.
    Accounts.SaveSkin(expected,remote);try{Usage.Set(expected,skin.Id,remote);}catch{throw new IOException(UiText.T("官方皮肤已更新，但本地标记保存失败，请刷新或重新应用。"));}
    return account.Name;
   }
  }
 }
}
