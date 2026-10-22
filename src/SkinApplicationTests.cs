using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public static class SkinApplicationTests {
  const string Id="1234567890abcdef1234567890abcdef";
  sealed class Fake:HttpMessageHandler {
   public int Posts,Gets;public bool Fail,Mismatch,Unauthorized;public string LastVariant,LastBody;
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();if(request.RequestUri.Scheme!="https"||request.RequestUri.Host!="api.minecraftservices.com"||request.Headers.Authorization==null||request.Headers.Authorization.Parameter!="FAKE_SECRET_skin")throw new Exception("Skin authorization transport incorrect");
    if(Unauthorized){Unauthorized=false;return new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("FAKE_SECRET_do_not_display")};}
    if(request.Method==HttpMethod.Post){Posts++;if(request.RequestUri.AbsolutePath!="/minecraft/profile/skins")throw new Exception("Wrong skin endpoint");LastBody=await request.Content.ReadAsStringAsync();if(!LastBody.Contains("image/png")||!LastBody.Contains("skin.png")||!LastBody.Contains("variant"))throw new Exception("Skin multipart missing PNG/model");LastVariant=LastBody.Contains("slim")?"SLIM":"CLASSIC";if(Fail)return new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("FAKE_SECRET_server_error")};}else Gets++;
    var profile=new{id=Mismatch?new string('f',32):Id,name="TestPlayer",skins=new[]{new{state="ACTIVE",id="remote-skin",url="https://textures.minecraft.net/texture/"+new string(LastVariant=="SLIM"?'b':'a',64),variant=LastVariant??"CLASSIC"}}};
    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(AccountStore.Serialize(profile),Encoding.UTF8,"application/json")};
   }
  }
  static void Assert(bool value,string why){if(!value)throw new Exception(why);}
  public static void Run(string parent){string root=Path.Combine(parent,"skin-application"),data=Path.Combine(root,"data");Directory.CreateDirectory(data);bool running=false;
   var record=new{type="MSA",active=true,custom="KEEP",profile=new{id=Id,name="TestPlayer",skin=new{id="",url="",variant=""},capes=new[]{new{id="cape-KEEP"}}},entitlement=new{ownsMinecraft=true},ygg=new{token="FAKE_SECRET_skin",exp=AccountStore.UnixNow()+3600},msa=new{refresh_token="FAKE_SECRET_refresh"}};
   Json.Write(Path.Combine(data,"accounts.json"),new{formatVersion=3,accounts=new[]{record},extra="KEEP"});var accounts=new AccountStore(data,()=>running);var store=new SkinStore(root);var classic=store.Import(SkinTests.Fixture(32),"Classic","");var slim=store.Import(SkinTests.Fixture(64),"Slim","");slim.Slim=true;var usage=new SkinUsageStore(root);
   var fake=new Fake();using(var service=new SkinApplication(root,accounts,fake,(a,ct)=>Task.FromResult(a))) {
    service.Apply(classic,CancellationToken.None).GetAwaiter().GetResult();Assert(usage.Active(accounts.Current())==classic.Id&&fake.LastVariant=="CLASSIC","Classic skin not applied");
    service.Apply(slim,CancellationToken.None).GetAwaiter().GetResult();Assert(usage.Active(accounts.Current())==slim.Id&&usage.Active(accounts.Current())!=classic.Id&&fake.LastVariant=="SLIM","Single active skin/model replacement failed");
    var restored=new SkinUsageStore(root);Assert(restored.Active(new AccountStore(data,()=>false).Current())==slim.Id,"Active skin did not persist");
    string saved=File.ReadAllText(Path.Combine(data,"skin-usage.json"));fake.Fail=true;bool failed=false;try{service.Apply(classic,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException ex){failed=true;Assert(!ex.Message.Contains("FAKE_SECRET"),"Skin response leaked credentials");}Assert(failed&&File.ReadAllText(Path.Combine(data,"skin-usage.json"))==saved&&usage.Active(accounts.Current())==slim.Id,"Failed upload changed active selection");fake.Fail=false;
    int posts=fake.Posts;fake.Mismatch=true;failed=false;try{service.Apply(classic,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){failed=true;}Assert(failed&&fake.Posts==posts,"Mismatched account uploaded a skin");fake.Mismatch=false;
    fake.Unauthorized=true;int refreshes=0;using(var refreshing=new SkinApplication(root,accounts,fake,(a,ct)=>{refreshes++;return Task.FromResult(a);}))refreshing.Apply(classic,CancellationToken.None).GetAwaiter().GetResult();Assert(refreshes==1&&usage.Active(accounts.Current())==classic.Id,"Expired game token did not refresh exactly once");
   }
   var accountRoot=Json.Read<Dictionary<string,object>>(Path.Combine(data,"accounts.json"));var selected=accounts.Current();Assert(AccountStore.Text(selected.Data,"custom")=="KEEP"&&AccountStore.Text(accountRoot,"extra")=="KEEP"&&AccountStore.Object(selected.Data,"profile").ContainsKey("capes"),"Applying skin lost account/cape metadata");
   running=true;bool denied=false;try{accounts.SkinAccount();}catch(IOException){denied=true;}Assert(denied,"Skin change allowed while engine running");running=false;accounts.Select(Id,true);denied=false;try{accounts.SkinAccount();}catch(IOException){denied=true;}Assert(denied,"Offline account skin upload allowed");
   Console.WriteLine("PASS: official skin multipart/auth/profile verification, classic/slim models, single active selection/restart persistence, failed-upload preservation, token renewal, offline/engine guards, cape/account metadata and credential redaction.");
  }
 }
}
