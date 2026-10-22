using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace FoolLauncher {
 public sealed class AccountChoice {
  public string Id,Name,Type,ClientId;public bool Active,OwnsGame;internal Dictionary<string,object> Data;
  public override string ToString(){return Name+"   ·   "+(Type=="Offline"?"离线角色":"微软账号")+(Active?"   ✓ 当前":"");}
 }
 public sealed class AccountSettings {public string Mode="auto",ProfileId="",ClientId="";}
 public sealed class AccountStore {
  readonly string Folder,AccountsFile,SettingsFile;readonly Func<bool> EngineRunning;
  public AccountStore(string data,Func<bool> engineRunning){Folder=data;AccountsFile=Path.Combine(data,"accounts.json");SettingsFile=Path.Combine(data,"zhixi-account-settings.json");EngineRunning=engineRunning;}
  internal static string Text(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?Convert.ToString(value):"";}
  internal static Dictionary<string,object> Object(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value as Dictionary<string,object>:null;}
  internal static bool Flag(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)&&value is bool&&(bool)value;}
  internal static string Serialize(object data){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(data);}
  Dictionary<string,object> ReadRoot() {
   if(!File.Exists(AccountsFile))return new Dictionary<string,object>{{"formatVersion",3},{"accounts",new object[0]}};
   var root=Json.Read<Dictionary<string,object>>(AccountsFile);
   if(Text(root,"formatVersion")!="3"||!(root.ContainsKey("accounts")&&root["accounts"] is IList))throw new InvalidDataException("账号文件格式无法识别，已保留原文件。请检查账号文件。");return root;
  }
  static List<Dictionary<string,object>> Records(Dictionary<string,object> root){var records=((IEnumerable)root["accounts"]).Cast<object>().Select(v=>v as Dictionary<string,object>).ToList();if(records.Any(v=>v==null))throw new InvalidDataException("账号列表有无法识别的数据，已保留原文件。");return records;}
  static AccountChoice Choice(Dictionary<string,object> account) {
   var profile=Object(account,"profile");var entitlement=Object(account,"entitlement");
   return new AccountChoice{Id=Text(profile,"id"),Name=Text(profile,"name"),Type=Text(account,"type"),ClientId=Text(account,"msa-client-id"),Active=Flag(account,"active"),OwnsGame=Flag(entitlement,"ownsMinecraft")||Flag(entitlement,"canPlayMinecraft"),Data=account};
  }
  public List<AccountChoice> List(){return Records(ReadRoot()).Select(Choice).Where(a=>!String.IsNullOrWhiteSpace(a.Id)&&!String.IsNullOrWhiteSpace(a.Name)&&(a.Type=="MSA"||a.Type=="Offline")).ToList();}
  public AccountSettings Settings(){return File.Exists(SettingsFile)?Json.Read<AccountSettings>(SettingsFile):new AccountSettings();}
  public AccountChoice Current(){var settings=Settings();if(settings.Mode=="none")return null;var accounts=List();return settings.Mode=="auto"?accounts.FirstOrDefault(a=>a.Active)??accounts.FirstOrDefault(a=>a.Type=="MSA"):accounts.FirstOrDefault(a=>a.Id==settings.ProfileId);}
  public string Mode(){var settings=Settings();var selected=Current();return selected==null?"none":settings.Mode=="offline"||selected.Type=="Offline"?"offline":"online";}
  void Guard(){if(EngineRunning())throw new IOException("请先退出 Prism 和游戏，再修改账号或登录。");}
  internal static void Atomic(string path,string text) {
   Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  void SaveSettings(AccountSettings settings){Guard();Atomic(SettingsFile,Serialize(settings));}
  public void SetClientId(string clientId){Guid id;if(!String.IsNullOrWhiteSpace(clientId)&&!Guid.TryParse(clientId.Trim(),out id))throw new ArgumentException("Client ID 应为应用注册页面中的 GUID 编号。");var settings=Settings();settings.ClientId=clientId.Trim();SaveSettings(settings);}
  public void Select(string id,bool offline){Guard();var account=List().FirstOrDefault(a=>a.Id==id);if(account==null)throw new IOException("账号已不存在，请刷新列表。");var settings=Settings();settings.ProfileId=id;settings.Mode=offline||account.Type=="Offline"?"offline":"online";SaveSettings(settings);}
  public void SignOut(){var settings=Settings();settings.Mode="none";settings.ProfileId="";SaveSettings(settings);}
  void Mutate(Action<List<Dictionary<string,object>>> change) {
   Guard();byte[] previous=File.Exists(AccountsFile)?File.ReadAllBytes(AccountsFile):null;var root=ReadRoot();var records=Records(root);change(records);root["accounts"]=records.ToArray();Guard();
   byte[] latest=File.Exists(AccountsFile)?File.ReadAllBytes(AccountsFile):null;
   if((previous==null)!=(latest==null)||previous!=null&&!previous.SequenceEqual(latest))throw new IOException("账号文件已被其他程序更新，请重试。");
   if(previous!=null){string backups=Path.Combine(Folder,"account-backups");Directory.CreateDirectory(backups);File.WriteAllBytes(Path.Combine(backups,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".bin"),ProtectedData.Protect(previous,null,DataProtectionScope.CurrentUser));}
   Atomic(AccountsFile,Serialize(root));
  }
  public void SaveMicrosoft(Dictionary<string,object> record) {
   record=Json.Parse<Dictionary<string,object>>(Serialize(record));var account=Choice(record);Guid client;
   if(account.Type!="MSA"||!Guid.TryParse(account.ClientId,out client)||!account.OwnsGame||!Regex.IsMatch(account.Id,@"\A[0-9a-fA-F]{32}\z")||!Regex.IsMatch(account.Name,@"\A[A-Za-z0-9_]{1,16}\z")||String.IsNullOrEmpty(Text(Object(record,"msa"),"refresh_token"))||String.IsNullOrEmpty(Text(Object(record,"ygg"),"token")))throw new InvalidDataException("微软登录返回的账号数据不完整或缺少游戏资格，未修改现有账号。");
   Mutate(records=>{int index=records.FindIndex(a=>Text(Object(a,"profile"),"id")==account.Id);if(index>=0){if(Flag(records[index],"active"))record["active"]=true;records[index]=record;}else records.Add(record);});Select(account.Id,false);
  }
  public AccountChoice AddOffline(string name) {
   Guard();name=(name??"").Trim();if(!Regex.IsMatch(name,@"\A[A-Za-z0-9_]{3,16}\z"))throw new ArgumentException("离线角色名请使用 3–16 位英文字母、数字或下划线。");
   if(List().Any(a=>a.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("已有同名角色，请直接从列表选择。");
   byte[] bytes;using(var md5=MD5.Create())bytes=md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:"+name));bytes[6]=(byte)((bytes[6]&15)|48);bytes[8]=(byte)((bytes[8]&63)|128);string id=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
   var record=new Dictionary<string,object>{{"type","Offline"},{"entitlement",new Dictionary<string,object>{{"ownsMinecraft",false},{"canPlayMinecraft",false}}},{"ygg",new Dictionary<string,object>{{"token","0"},{"iat",UnixNow()}}},{"profile",new Dictionary<string,object>{{"id",id},{"name",name},{"skin",new Dictionary<string,object>{{"id",""},{"url",""},{"variant",""}}},{"capes",new object[0]}}}};
   Mutate(records=>records.Add(record));Select(id,true);return Choice(record);
  }
  public void Remove(string id){Mutate(records=>records.RemoveAll(a=>Text(Object(a,"profile"),"id")==id));if(Settings().ProfileId==id)SignOut();}
  public string LaunchArguments() {
   var selected=Current();if(selected==null)throw new IOException("请点击右上角“账号”，选择账号或添加账号后再启动。");
   if(!Regex.IsMatch(selected.Name,@"\A[A-Za-z0-9_]{1,16}\z"))throw new InvalidDataException("账号角色名无效。");
   if(Mode()=="offline"&&!List().Any(a=>a.Type=="MSA"&&a.OwnsGame))throw new IOException("离线模式需要保留至少一个已验证拥有 Minecraft Java 版的微软账号。请先完成正版登录。");
   return " --profile \""+selected.Name+"\""+(Mode()=="offline"?" --offline \""+selected.Name+"\"":"");
  }
  public string LaunchClientId(){var selected=Current();return selected!=null&&selected.Type=="MSA"?selected.ClientId:List().Where(a=>a.Type=="MSA"&&a.OwnsGame).OrderByDescending(a=>a.Active).Select(a=>a.ClientId).FirstOrDefault()??"";}
  internal static long UnixNow(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;}
 }
}
