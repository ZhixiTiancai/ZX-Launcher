using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public static class AccountTests {
  const string Client="11111111-2222-3333-4444-555555555555",Profile="1234567890abcdef1234567890abcdef";
  sealed class Reporter:IProgress<string>{public void Report(string value){if(value.Contains("FAKE_SECRET"))throw new Exception("Credential leaked to progress");}}
  sealed class Fake:HttpMessageHandler {
   public int Calls,Polls;public string Error,Uri="https://microsoft.com/devicelogin";public bool NoOwnership;
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
    ct.ThrowIfCancellationRequested();Calls++;string url=request.RequestUri.AbsoluteUri,path=request.RequestUri.AbsolutePath,body=request.Content==null?"":await request.Content.ReadAsStringAsync();
    if(request.RequestUri.Scheme!="https"||url.Contains("FAKE_SECRET"))throw new Exception("Unsafe auth transport");
    object response;HttpStatusCode status=HttpStatusCode.OK;
    if(path.EndsWith("/devicecode")){if(!body.Contains(Client))throw new Exception("Client ID missing");response=new {device_code="FAKE_SECRET_device",user_code="TEST-CODE",verification_uri=Uri,interval=1,expires_in=30};}
    else if(path.EndsWith("/token")) {
     if(!body.Contains(Client))throw new Exception("Client ID missing from token request");
     if(Error!=null){status=HttpStatusCode.BadRequest;response=new {error=Error,error_description="FAKE_SECRET_do_not_show"};}
     else if(body.Contains("device_code")&&Polls++<2){status=HttpStatusCode.BadRequest;response=new {error=Polls==1?"authorization_pending":"slow_down"};}
     else response=new {access_token="FAKE_SECRET_msa",refresh_token="FAKE_SECRET_refresh",expires_in=3600};
    }else if(path=="/user/authenticate") {if(!body.Contains("d=FAKE_SECRET_msa"))throw new Exception("Xbox request has wrong token");response=new {Token="FAKE_SECRET_xbox",DisplayClaims=new {xui=new[]{new {uhs="user_hash"}}}};}
    else if(path=="/xsts/authorize") {if(!body.Contains("rp://api.minecraftservices.com/"))throw new Exception("Wrong XSTS relying party");response=new {Token="FAKE_SECRET_xsts",DisplayClaims=new {xui=new[]{new {uhs="user_hash"}}}};}
    else if(path=="/launcher/login") {if(!body.Contains("XBL3.0 x=user_hash;FAKE_SECRET_xsts"))throw new Exception("Wrong Minecraft login identity");response=new {access_token="FAKE_SECRET_minecraft",expires_in=86400};}
    else if(path=="/entitlements/license") {CheckBearer(request);response=new {items=NoOwnership?new object[0]:new object[]{new {name="product_minecraft"},new {name="game_minecraft"}}};}
    else if(path=="/minecraft/profile") {CheckBearer(request);response=new {id=Profile,name="TestPlayer",skins=new[]{new {state="ACTIVE",id="skin",url="https://textures.minecraft.net/texture/test",variant="CLASSIC"}},capes=new object[0]};}
    else throw new Exception("Unexpected login endpoint: "+path);
    return new HttpResponseMessage(status){Content=new StringContent(AccountStore.Serialize(response),Encoding.UTF8,"application/json")};
   }
   static void CheckBearer(HttpRequestMessage request){if(request.Headers.Authorization==null||request.Headers.Authorization.Scheme!="Bearer"||request.Headers.Authorization.Parameter!="FAKE_SECRET_minecraft")throw new Exception("Minecraft bearer missing");}
  }
  static Dictionary<string,object> OnlineRecord(){return Json.Parse<Dictionary<string,object>>(AccountStore.Serialize(new {type="MSA",profile=new {id=Profile,name="ExistingPlayer",skin=new {id="",url="",variant=""},capes=new object[0]},entitlement=new {ownsMinecraft=true,canPlayMinecraft=true},msa=new {refresh_token="FAKE_SECRET_existing_refresh"},ygg=new {token="FAKE_SECRET_existing_game"},active=true,custom_field="preserve"}));}
  public static void Run(string root) {
   string data=Path.Combine(root,"accounts-test");Directory.CreateDirectory(data);string file=Path.Combine(data,"accounts.json");
   var seed=new Dictionary<string,object>{{"formatVersion",3},{"accounts",new[]{OnlineRecord()}},{"extra_root","preserve"}};File.WriteAllText(file,AccountStore.Serialize(seed));byte[] original=File.ReadAllBytes(file);bool running=false;var store=new AccountStore(data,()=>running);
   if(store.Current().Name!="ExistingPlayer"||store.Mode()!="online")throw new Exception("Existing account not adopted");
   store.Select(Profile,true);if(!store.LaunchArguments().Contains("--offline \"ExistingPlayer\"")||!File.ReadAllBytes(file).SequenceEqual(original))throw new Exception("Offline selection rewrote account credentials");
   store.SignOut();bool denied=false;try{store.LaunchArguments();}catch(IOException){denied=true;}if(!denied||store.Current()!=null)throw new Exception("Sign out still launches selected account");
   var offline=store.AddOffline("Offline_Test");if(offline.Id.Length!=32||store.Mode()!="offline"||store.LaunchArguments().Contains("FAKE_SECRET"))throw new Exception("Offline role/launch arguments wrong");
   if(store.List().First(a=>a.Id==offline.Id).OwnsGame)throw new Exception("Offline role invented game ownership");
   if(AccountStore.Text(Json.Read<Dictionary<string,object>>(file),"extra_root")!="preserve"||AccountStore.Text(store.List().First(a=>a.Id==Profile).Data,"custom_field")!="preserve")throw new Exception("Account mutation lost unknown data");
   string backup=Directory.GetFiles(Path.Combine(data,"account-backups"),"*.bin").First();if(Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("FAKE_SECRET")||!ProtectedData.Unprotect(File.ReadAllBytes(backup),null,DataProtectionScope.CurrentUser).SequenceEqual(original))throw new Exception("Account backup encryption failed");
   running=true;denied=false;try{store.Remove(Profile);}catch(IOException){denied=true;}if(!denied)throw new Exception("Allowed account write while engine running");running=false;
   store.Remove(Profile);denied=false;try{store.LaunchArguments();}catch(IOException){denied=true;}if(!denied)throw new Exception("Offline launch without owned account accepted");
   var fake=new Fake();var delays=new List<int>();Dictionary<string,object> record;
   using(var auth=new MicrosoftLogin(fake,(seconds,ct)=>{ct.ThrowIfCancellationRequested();delays.Add(seconds);return Task.FromResult(0);})) {
    var device=auth.Begin(Client,CancellationToken.None).GetAwaiter().GetResult();record=auth.Complete(Client,device,new Reporter(),CancellationToken.None).GetAwaiter().GetResult();
    if(!delays.SequenceEqual(new[]{1,1,6})||fake.Calls!=9)throw new Exception("OAuth polling/slow_down sequence wrong");
    store.SetClientId(Client);store.SaveMicrosoft(record);if(store.Current().Name!="TestPlayer"||!store.Current().OwnsGame||store.Mode()!="online"||store.LaunchClientId()!=Client||store.LaunchArguments()!=" --profile \"TestPlayer\"")throw new Exception("Native Microsoft account did not round-trip into Prism schema");
    record=auth.Refresh(Client,store.Current(),new Reporter(),CancellationToken.None).GetAwaiter().GetResult();store.SaveMicrosoft(record);
   }
   foreach(string error in new[]{"authorization_declined","expired_token","invalid_client"})using(var auth=new MicrosoftLogin(new Fake{Error=error},(s,ct)=>Task.FromResult(0))) {
    var device=auth.Begin(Client,CancellationToken.None).GetAwaiter().GetResult();denied=false;try{auth.Complete(Client,device,new Reporter(),CancellationToken.None).GetAwaiter().GetResult();}catch(LoginRequestException ex){denied=!ex.Message.Contains("FAKE_SECRET");}if(!denied)throw new Exception("Auth rejection leaked details or was accepted");
   }
   using(var auth=new MicrosoftLogin(new Fake{Uri="https://evil.example/authorize"},(s,ct)=>Task.FromResult(0))) {denied=false;try{auth.Begin(Client,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Non-Microsoft auth URL accepted");}
   using(var auth=new MicrosoftLogin(new Fake{NoOwnership=true},(s,ct)=>Task.FromResult(0))) {var device=auth.Begin(Client,CancellationToken.None).GetAwaiter().GetResult();denied=false;try{auth.Complete(Client,device,new Reporter(),CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}if(!denied)throw new Exception("Account without game entitlement accepted");}
   var cancelledFake=new Fake();using(var auth=new MicrosoftLogin(cancelledFake,(s,ct)=>Task.FromResult(0)))using(var cancel=new CancellationTokenSource()){var device=auth.Begin(Client,cancel.Token).GetAwaiter().GetResult();cancel.Cancel();denied=false;try{auth.Complete(Client,device,new Reporter(),cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){denied=true;}if(!denied||cancelledFake.Calls!=1)throw new Exception("Cancelled login issued further requests");}
   File.WriteAllText(file,"{\"formatVersion\":3,\"accounts\":\"invalid\"}");denied=false;try{store.AddOffline("AnotherPlayer");}catch(InvalidDataException){denied=true;}if(!denied||!File.ReadAllText(file).Contains("invalid"))throw new Exception("Corrupt account store overwritten");
   Console.WriteLine("PASS: account selection/sign-out, offline identity and entitlement guard, Prism schema, encrypted backups, engine write guard, native Microsoft/Xbox/Minecraft flow, polling/backoff, refresh, cancellation, hostile URL and credential error redaction.");
  }
 }
}
