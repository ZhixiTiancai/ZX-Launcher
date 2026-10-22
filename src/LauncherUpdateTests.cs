using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FoolLauncher {
 public static class LauncherUpdateTests {
  static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
  static void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,message);}
  static byte[] Bytes(string text){return Encoding.UTF8.GetBytes(text);}
  static string Hash(byte[] data){using(var stream=new MemoryStream(data))return LauncherUpdateCore.Hash(stream);}
  static string Package(string root,RSACryptoServiceProvider rsa,Dictionary<string,byte[]> files,bool tamper=false,bool extra=false) {
   var manifest=new UpdateManifest{Product="ZhixiLauncher",Format=1,Kind="package",Version="0.2.5",Notes="Synthetic update fixture",Files=files.Select(x=>new UpdateFile{Name=x.Key,Size=x.Value.Length,Sha256=Hash(x.Value)}).ToArray()};
   byte[] raw=Bytes(LauncherUpdateCore.Serializer().Serialize(manifest));
   var signed=new SignedUpdate{Payload=Convert.ToBase64String(raw),Signature=Convert.ToBase64String(rsa.SignData(raw,CryptoConfig.MapNameToOID("SHA256")))};
   string path=Path.Combine(root,Guid.NewGuid().ToString("N")+".zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) {
    using(var stream=zip.CreateEntry("update-manifest.json").Open()){var data=Bytes(LauncherUpdateCore.Serializer().Serialize(signed));stream.Write(data,0,data.Length);}
    foreach(var pair in files)using(var stream=zip.CreateEntry(pair.Key).Open()){var data=tamper&&pair.Key=="App.xaml"?Bytes("corruption"):pair.Value;stream.Write(data,0,data.Length);}
    if(extra)using(var stream=zip.CreateEntry("extra.txt").Open())stream.WriteByte(1);
   }return path;
  }
  static string Target(string parent) {
   string root=Path.Combine(parent,"install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"engine"));File.WriteAllText(Path.Combine(root,"engine/prismlauncher.exe"),"engine-original");
   foreach(string file in new[]{"智熄的启动器.exe","智熄更新器.exe","App.xaml"})File.WriteAllText(Path.Combine(root,file),"original-"+file);
   foreach(string path in new[]{"data/accounts.json","data/launcher-update-settings.json","data/skins/skin.png","data/instances/example/minecraft/mods/mod.jar","data/instances/example/minecraft/saves/world/level.dat","runtime/java.exe","exports/world.zip"}){string file=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,"KEEP-"+path);}
   return root;
  }
  static Dictionary<string,string> Snapshot(string root){return Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToDictionary(x=>x.Substring(root.Length+1),LauncherUpdateCore.HashFile);}
  static void Kept(string root,Dictionary<string,string> before,bool all){foreach(var pair in before)if(all||pair.Key.StartsWith("data/")||pair.Key.StartsWith("data\\")||pair.Key.StartsWith("runtime")||pair.Key.StartsWith("engine")||pair.Key.StartsWith("exports"))Assert(File.Exists(Path.Combine(root,pair.Key))&&LauncherUpdateCore.HashFile(Path.Combine(root,pair.Key))==pair.Value,"User data changed: "+pair.Key);}
  public static void Run(string parent) {
   string root=Path.Combine(parent,"updater");Directory.CreateDirectory(root);
   using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;string trust=rsa.ToXmlString(false);
    var files=new Dictionary<string,byte[]>{{"source/README.md",Bytes("source-v2")},{"智熄的启动器.exe",Bytes("frontend-v2")},{"App.xaml",Bytes("xaml-v2")},{"智熄更新器.exe",Bytes("updater-v2")}};
    Assert(new LauncherUpdateSettings().AutoCheck&&new LauncherUpdateSettings().FeedUrl=="https://github.com/ZhixiTiancai/ZX-Launcher/releases/latest/download/update.json","Default GitHub release feed is missing");
    var feed=new UpdateManifest{Product="ZhixiLauncher",Format=1,Kind="feed",Version="0.2.6",Notes="GitHub feed fixture",Url="https://github.com/ZhixiTiancai/ZX-Launcher/releases/download/v0.2.6/ZX-Launcher-0.2.6-update.zip",Size=1024,Sha256=new string('a',64)};
    byte[] feedPayload=Bytes(LauncherUpdateCore.Serializer().Serialize(feed));
    byte[] feedBytes=Bytes(LauncherUpdateCore.Serializer().Serialize(new SignedUpdate{Payload=Convert.ToBase64String(feedPayload),Signature=Convert.ToBase64String(rsa.SignData(feedPayload,CryptoConfig.MapNameToOID("SHA256")))}));
    Assert(LauncherUpdateCore.ReadSigned(feedBytes,"feed",trust).Version=="0.2.6","Signed release feed rejected");
    Reject(()=>LauncherUpdateCore.ReadSigned(feedBytes,"package",trust),"Feed used as a package manifest");
    string package=Package(root,rsa,files);var staged=LauncherUpdateCore.Stage(package,Path.Combine(root,"valid"),CancellationToken.None,trust);
    string target=Target(root);var before=Snapshot(target);string backup=LauncherUpdateCore.Apply(staged,target);Kept(target,before,false);Assert(File.Exists(Path.Combine(backup,"智熄的启动器.exe")),"No old frontend backup");foreach(var pair in files)Assert(File.ReadAllBytes(Path.Combine(target,pair.Key)).SequenceEqual(pair.Value),"Update not installed");
    string failed=Target(root);var prior=Snapshot(failed);Reject(()=>LauncherUpdateCore.Apply(staged,failed,n=>{if(n==2)throw new IOException("Injected write failure");}),"Failure did not roll back");Kept(failed,prior,true);Assert(!File.Exists(Path.Combine(failed,"source/README.md")),"Rollback retained new program file");
    string locked=Target(root);var lockBefore=Snapshot(locked);using(var held=File.Open(Path.Combine(locked,"App.xaml"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>LauncherUpdateCore.Apply(staged,locked),"Locked update was accepted");Kept(locked,lockBefore,true);
    Reject(()=>LauncherUpdateCore.Stage(Package(root,rsa,files,true),Path.Combine(root,"tamper"),CancellationToken.None,trust),"Tampered package accepted");
    Reject(()=>LauncherUpdateCore.Stage(Package(root,rsa,files,false,true),Path.Combine(root,"extra"),CancellationToken.None,trust),"Unlisted file accepted");
    using(var other=new RSACryptoServiceProvider(2048)){other.PersistKeyInCsp=false;Reject(()=>LauncherUpdateCore.Stage(package,Path.Combine(root,"wrong-key"),CancellationToken.None,other.ToXmlString(false)),"Wrong signature accepted");}
    foreach(string bad in new[]{"data/accounts.json","engine/prismlauncher.exe","runtime/java.exe","../outside.exe","source/src/../../data/accounts.json","source/src/CON.cs","source/src/evil.cs ","source/src/test.cs:stream","source\\src\\test.cs"}) {
     Assert(!LauncherUpdateCore.Allowed(bad),"Protected update path accepted: "+bad);
     var poisoned=new Dictionary<string,byte[]>(files);poisoned.Add(bad,Bytes("poison"));string zip=Package(root,rsa,poisoned);Reject(()=>LauncherUpdateCore.Stage(zip,Path.Combine(root,Guid.NewGuid().ToString("N")),CancellationToken.None,trust),"Signed protected file accepted");
    }
    string changed=Path.Combine(staged.Folder,"App.xaml");File.WriteAllText(changed,"staged corruption");string clean=Target(root);var cleanBefore=Snapshot(clean);Reject(()=>LauncherUpdateCore.Apply(staged,clean),"Changed staged file accepted");Kept(clean,cleanBefore,true);
    var cancelled=new CancellationToken(true);bool stopped=false;try{LauncherUpdateCore.Stage(package,Path.Combine(root,"cancelled"),cancelled,trust);}catch(OperationCanceledException){stopped=true;}Assert(stopped,"Cancelled update continued");
    foreach(string bad in new[]{"http://example.com/update.json","https://user:pass@example.com/update.json","file:///update.json","https://localhost/feed","https://example.com/feed#fragment"})Reject(()=>LauncherUpdateCore.Https(bad),"Unsafe update URL accepted");
   }
   Console.WriteLine("PASS: signed update validation, tamper/key/path rejection, bounded extraction, preflight locks, failed-write rollback, cancellation, and preservation of modpacks/mods/worlds/accounts/skins/runtime/engine/exports.");
  }
 }
}
