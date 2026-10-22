using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class DeviceLogin {public string DeviceCode,UserCode,VerificationUrl;public int Interval,Expires;}
 public sealed class LoginRequestException:IOException {
  public readonly string Code;public readonly HttpStatusCode Status;
  public LoginRequestException(string stage,HttpStatusCode status,string code):base(MessageFor(stage,status,code)){Code=code;Status=status;}
  static string MessageFor(string stage,HttpStatusCode status,string code) {
   if(code=="authorization_declined"||code=="access_denied")return "你已取消微软授权，可以重新点击登录。";
   if(code=="expired_token")return "登录代码已过期，请重新开始登录。";
   if(code=="invalid_grant")return "账号授权已失效，请重新登录。";
   if(code=="unauthorized_client"||code=="invalid_client")return "应用 Client ID 不可用，请检查应用注册以及公共客户端流程配置。";
   if(stage=="Minecraft 登录"&&status==HttpStatusCode.Forbidden)return "Minecraft 服务拒绝了此应用。请检查 Client ID 是否已获准使用 Minecraft 登录接口。";
   if(stage=="角色信息"&&status==HttpStatusCode.NotFound)return "该微软账号没有 Minecraft Java 版角色。请先确认游戏资格并创建角色。";
   if(code=="2148916233")return "此微软账号尚未设置 Xbox 玩家档案，请在 Xbox 官方网站完成设置。";
   if(code=="2148916238")return "此账号需要由家庭组织者在微软官方服务中完成儿童账号设置。";
   return stage+"失败（HTTP "+(int)status+"）。请检查网络或稍后重试。";
  }
 }
 public sealed class MicrosoftLogin:IDisposable {
  const string Authority="https://login.microsoftonline.com/consumers/oauth2/v2.0/";
  const string Scope="XboxLive.SignIn XboxLive.offline_access";
  readonly HttpClient Http;readonly Func<int,CancellationToken,Task> Delay;
  public MicrosoftLogin():this(new HttpClientHandler{AllowAutoRedirect=false},(seconds,ct)=>Task.Delay(TimeSpan.FromSeconds(seconds),ct)){}
  internal MicrosoftLogin(HttpMessageHandler handler,Func<int,CancellationToken,Task> delay){Http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(45)};Http.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.1.8");Delay=delay;ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;}
  public void Dispose(){Http.Dispose();}
  static string Required(Dictionary<string,object> value,string key){string text=AccountStore.Text(value,key);if(String.IsNullOrWhiteSpace(text))throw new InvalidDataException("登录服务返回的数据不完整："+key);return text;}
  static int Number(Dictionary<string,object> value,string key,int fallback){int number;return Int32.TryParse(AccountStore.Text(value,key),out number)?number:fallback;}
  async Task<Dictionary<string,object>> Send(string stage,HttpMethod method,string url,HttpContent content,string bearer,CancellationToken ct) {
   using(var request=new HttpRequestMessage(method,url)) {
    request.Content=content;request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));if(bearer!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
    try {
     using(var response=await Http.SendAsync(request,ct).ConfigureAwait(false)) {
      string body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(body.Length>2*1024*1024)throw new InvalidDataException("登录服务返回的数据过大。");
      Dictionary<string,object> value;try{value=Json.Parse<Dictionary<string,object>>(body);}catch{throw new IOException(stage+"返回了无法解析的数据。请检查网络。");}
      if(!response.IsSuccessStatusCode){string code=AccountStore.Text(value,"error");if(String.IsNullOrEmpty(code))code=AccountStore.Text(value,"XErr");throw new LoginRequestException(stage,response.StatusCode,code);}return value;
     }
    }catch(HttpRequestException){throw new IOException(stage+"网络连接失败，请检查网络后重试。");}
    catch(TaskCanceledException){ct.ThrowIfCancellationRequested();throw new IOException(stage+"请求超时，请重试。");}
   }
  }
  Task<Dictionary<string,object>> Form(string stage,string endpoint,Dictionary<string,string> fields,CancellationToken ct){return Send(stage,HttpMethod.Post,Authority+endpoint,new FormUrlEncodedContent(fields),null,ct);}
  Task<Dictionary<string,object>> Post(string stage,string url,object fields,CancellationToken ct){return Send(stage,HttpMethod.Post,url,new StringContent(AccountStore.Serialize(fields),Encoding.UTF8,"application/json"),null,ct);}
  static string Client(string value){Guid id;if(!Guid.TryParse(value,out id))throw new ArgumentException("请先在“登录配置”中填写你自己的 Microsoft 应用 Client ID。");return id.ToString();}
  public async Task<DeviceLogin> Begin(string clientId,CancellationToken ct) {
   var response=await Form("获取登录代码","devicecode",new Dictionary<string,string>{{"client_id",Client(clientId)},{"scope",Scope}},ct).ConfigureAwait(false);
   Uri uri;string url=Required(response,"verification_uri");if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!(uri.Host.Equals("microsoft.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".microsoft.com",StringComparison.OrdinalIgnoreCase)||uri.Host.Equals("login.microsoftonline.com",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("登录服务返回了非微软官方的授权地址。");
   return new DeviceLogin{DeviceCode=Required(response,"device_code"),UserCode=Required(response,"user_code"),VerificationUrl=url,Interval=Math.Max(1,Math.Min(60,Number(response,"interval",5))),Expires=Math.Max(1,Math.Min(3600,Number(response,"expires_in",900)))};
  }
  public async Task<Dictionary<string,object>> Complete(string clientId,DeviceLogin device,IProgress<string> progress,CancellationToken ct) {
   var deadline=DateTime.UtcNow.AddSeconds(device.Expires);int interval=device.Interval;
   while(DateTime.UtcNow<deadline) {
    await Delay(interval,ct).ConfigureAwait(false);Dictionary<string,object> token;
    try{token=await Form("微软授权","token",new Dictionary<string,string>{{"client_id",Client(clientId)},{"grant_type","urn:ietf:params:oauth:grant-type:device_code"},{"device_code",device.DeviceCode}},ct).ConfigureAwait(false);}
    catch(LoginRequestException ex){if(ex.Code=="authorization_pending")continue;if(ex.Code=="slow_down"){interval+=5;continue;}throw;}
    return await Exchange(clientId,token,progress,ct).ConfigureAwait(false);
   }
   throw new IOException("登录代码已过期，请重新点击登录。");
  }
  public async Task<Dictionary<string,object>> Refresh(string configuredClientId,AccountChoice account,IProgress<string> progress,CancellationToken ct) {
   string client=Client(configuredClientId);if(!String.Equals(client,account.ClientId,StringComparison.OrdinalIgnoreCase))throw new IOException("此账号使用的是原登录引擎的应用授权。你可以直接选择使用，启动时由引擎验证；若要在此重新登录，请用已配置的应用重新添加微软账号。");
   var token=await Form("刷新微软授权","token",new Dictionary<string,string>{{"client_id",client},{"grant_type","refresh_token"},{"refresh_token",Required(AccountStore.Object(account.Data,"msa"),"refresh_token")},{"scope",Scope}},ct).ConfigureAwait(false);
   if(String.IsNullOrEmpty(AccountStore.Text(token,"refresh_token")))token["refresh_token"]=Required(AccountStore.Object(account.Data,"msa"),"refresh_token");
   var record=await Exchange(client,token,progress,ct).ConfigureAwait(false);
   if(AccountStore.Text(AccountStore.Object(record,"profile"),"id")!=account.Id)throw new InvalidDataException("刷新结果与原账号不一致，未保存。");return record;
  }
  static Dictionary<string,object> Token(string token,long seconds){long now=AccountStore.UnixNow();return new Dictionary<string,object>{{"token",token},{"iat",now},{"exp",now+seconds}};}
  static Dictionary<string,object> Claim(Dictionary<string,object> response) {
   var claims=AccountStore.Object(response,"DisplayClaims");object entries;if(claims==null||!claims.TryGetValue("xui",out entries)||!(entries is IEnumerable))throw new InvalidDataException("Xbox 返回的数据缺少玩家信息。");
   var claim=((IEnumerable)entries).Cast<object>().FirstOrDefault() as Dictionary<string,object>;Required(claim,"uhs");return claim;
  }
  async Task<Dictionary<string,object>> Exchange(string client,Dictionary<string,object> msa,IProgress<string> progress,CancellationToken ct) {
   progress.Report("微软授权完成，正在验证 Xbox 账号…");
   string access=Required(msa,"access_token"),refresh=Required(msa,"refresh_token");
   var xbox=await Post("Xbox 登录","https://user.auth.xboxlive.com/user/authenticate",new {Properties=new {AuthMethod="RPS",SiteName="user.auth.xboxlive.com",RpsTicket="d="+access},RelyingParty="http://auth.xboxlive.com",TokenType="JWT"},ct).ConfigureAwait(false);
   string userToken=Required(xbox,"Token"),uhs=Required(Claim(xbox),"uhs");
   var xsts=await Post("Xbox 游戏授权","https://xsts.auth.xboxlive.com/xsts/authorize",new {Properties=new {SandboxId="RETAIL",UserTokens=new[]{userToken}},RelyingParty="rp://api.minecraftservices.com/",TokenType="JWT"},ct).ConfigureAwait(false);
   if(Required(Claim(xsts),"uhs")!=uhs)throw new InvalidDataException("Xbox 授权玩家信息不一致。");
   string xstsToken=Required(xsts,"Token");progress.Report("正在验证 Minecraft Java 版资格与角色…");
   var mc=await Post("Minecraft 登录","https://api.minecraftservices.com/launcher/login",new {xtoken="XBL3.0 x="+uhs+";"+xstsToken,platform="PC_LAUNCHER"},ct).ConfigureAwait(false);
   string mcToken=Required(mc,"access_token");
   var entitlements=await Send("游戏资格",HttpMethod.Get,"https://api.minecraftservices.com/entitlements/license?requestId="+Guid.NewGuid().ToString(),null,mcToken,ct).ConfigureAwait(false);
   object items;var names=new List<string>();if(entitlements.TryGetValue("items",out items)&&items is IEnumerable)foreach(object item in (IEnumerable)items)names.Add(AccountStore.Text(item as Dictionary<string,object>,"name"));
   bool owns=names.Contains("product_minecraft"),canPlay=names.Contains("game_minecraft");if(!owns&&!canPlay)throw new IOException("该账号没有可用的 Minecraft Java 版资格，请确认购买或订阅状态。");
   var profile=await Send("角色信息",HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile",null,mcToken,ct).ConfigureAwait(false);
   string profileId=Required(profile,"id"),name=Required(profile,"name");
   var skin=new Dictionary<string,object>{{"id",""},{"url",""},{"variant",""}};object skins;
   if(profile.TryGetValue("skins",out skins)&&skins is IEnumerable){var active=((IEnumerable)skins).Cast<object>().Select(v=>v as Dictionary<string,object>).FirstOrDefault(v=>AccountStore.Text(v,"state")=="ACTIVE");if(active!=null)foreach(string key in new[]{"id","url","variant"})skin[key]=AccountStore.Text(active,key);}
   var capes=new List<object>();object capeItems;if(profile.TryGetValue("capes",out capeItems)&&capeItems is IEnumerable)foreach(object item in (IEnumerable)capeItems){var cape=item as Dictionary<string,object>;if(cape!=null)capes.Add(new {id=AccountStore.Text(cape,"id"),url=AccountStore.Text(cape,"url"),alias=AccountStore.Text(cape,"alias")});}
   var msaToken=Token(access,Number(msa,"expires_in",3600));msaToken["refresh_token"]=refresh;
   var utoken=Token(userToken,3600);utoken["extra"]=new {uhs=uhs};var xrp=Token(xstsToken,3600);xrp["extra"]=new {uhs=uhs};
   return new Dictionary<string,object>{{"type","MSA"},{"msa-client-id",Client(client)},{"msa",msaToken},{"utoken",utoken},{"xrp-mc",xrp},{"ygg",Token(mcToken,Number(mc,"expires_in",86400))},{"entitlement",new {ownsMinecraft=owns,canPlayMinecraft=canPlay}},{"profile",new Dictionary<string,object>{{"id",profileId},{"name",name},{"skin",skin},{"capes",capes.ToArray()}}}};
  }
 }
}
