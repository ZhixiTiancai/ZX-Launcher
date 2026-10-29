using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public static class SelfTests {
  public static int Run() {
   string root=Path.Combine(Environment.CurrentDirectory,"build","test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   foreach(string bad in new[]{"../outside","a/../../outside","C:/outside","/outside","a/CON.txt","a\\..\\outside","file:stream","a/NUL"}) {
    bool rejected=false;try{PackService.SafePath(root,bad);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Unsafe path accepted: "+bad);
   }
   string normal=PackService.SafePath(root,"mods/example.jar");if(!normal.StartsWith(root))throw new Exception("Normal path rejected");
   var payload=Encoding.UTF8.GetBytes("verified");string hash;using(var sha=SHA512.Create())hash=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
   var pack=new PackIndex {name="测试包",versionId="1",game="minecraft",formatVersion=1,dependencies=new System.Collections.Generic.Dictionary<string,string>{{"minecraft","1.20.1"},{"forge","47.4.12"}},files=new[]{new PackFile{path="mods/test.jar",fileSize=payload.Length,hashes=new System.Collections.Generic.Dictionary<string,string>{{"sha512",hash}},downloads=new[]{"https://example.com/test.jar"}}}};
   var service=new PackService(root);service.Validate(pack);
   string zip=Path.Combine(root,"test.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)) {
    using(var writer=new StreamWriter(z.CreateEntry("modrinth.index.json").Open()))writer.Write(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(pack));
    using(var writer=new StreamWriter(z.CreateEntry("overrides/config/test.txt").Open()))writer.Write("common");
    using(var writer=new StreamWriter(z.CreateEntry("client-overrides/config/test.txt").Open()))writer.Write("client");
   }
   var i=service.Import(zip,new Progress<Update>(),CancellationToken.None).GetAwaiter().GetResult();
   if(File.ReadAllText(Path.Combine(service.Game(i),"config/test.txt"))!="client")throw new Exception("Override precedence broken");
   if(!File.ReadAllText(Path.Combine(service.Folder(i),"mmc-pack.json")).Contains("net.minecraftforge"))throw new Exception("Forge component missing");
   Directory.CreateDirectory(Path.GetDirectoryName(normal));File.WriteAllBytes(normal,payload);
   if(!PackService.Verify(normal,pack.files[0]))throw new Exception("Valid hash rejected");File.WriteAllText(normal,"tampered");if(PackService.Verify(normal,pack.files[0]))throw new Exception("Tampering accepted");
   pack.files[0].path="../escape";bool denied=false;try{service.Validate(pack);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Index traversal accepted");
   string sha256;using(var sha=SHA256.Create())sha256=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
   if(!MetadataCache.Matches(payload,sha256))throw new Exception("Metadata SHA-256 rejected valid bytes");
   if(MetadataCache.Matches(Encoding.UTF8.GetBytes("tampered"),sha256))throw new Exception("Metadata SHA-256 accepted corruption");
   var fixedIni=IniConfig.GeneralLines(new[]{"[General]","Language=old","KeepGeneral=1","[UI]","WindowState=keep","Language=misplaced"},new System.Collections.Generic.Dictionary<string,string>{{"Language","zh"},{"JavaPath","runtime/java.exe"}});
   int ui=Array.IndexOf(fixedIni,"[UI]");
   if(Array.IndexOf(fixedIni,"Language=zh")>ui||Array.IndexOf(fixedIni,"JavaPath=runtime/java.exe")>ui||fixedIni.Count(l=>l.StartsWith("Language="))!=1||!fixedIni.Contains("WindowState=keep"))throw new Exception("INI section repair failed");
   if(GameFiles.MavenPath("com.mojang:minecraft:1.20.1:client")!="com/mojang/minecraft/1.20.1/minecraft-1.20.1-client.jar")throw new Exception("Maven classifier path failed");
   if(GameFiles.Allowed(new[]{new GameRule{action="allow",os=new GameOs{name="linux"}}})||!GameFiles.Allowed(new[]{new GameRule{action="allow",os=new GameOs{name="windows"}}}))throw new Exception("Library OS rule failed");
   string sha1;using(var sha=SHA1.Create())sha1=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
   File.WriteAllBytes(normal,payload);var gameFile=new PackFile{fileSize=payload.Length,hashes=new System.Collections.Generic.Dictionary<string,string>{{"sha1",sha1}}};
   if(!PackService.Verify(normal,gameFile))throw new Exception("Game SHA-1 rejected valid file");File.WriteAllText(normal,"tampered");if(PackService.Verify(normal,gameFile))throw new Exception("Game SHA-1 accepted corrupted file");
   var wanted=new System.Collections.Generic.Dictionary<string,string>{{"net.minecraft","1.21.1"}};
   if(MetadataCache.DependencyVersion(new MetaRequirement{uid="net.fabricmc.intermediary"},wanted)!="1.21.1"||MetadataCache.DependencyVersion(new MetaRequirement{uid="org.quiltmc.hashed"},wanted)!="1.21.1")throw new Exception("Game mapping dependency resolution failed");
   if(MetadataCache.DependencyVersion(new MetaRequirement{uid="org.lwjgl3",suggests="3.3.3"},wanted)!="3.3.3")throw new Exception("Suggested dependency resolution failed");
   denied=false;try{MetadataCache.DependencyVersion(new MetaRequirement{uid="net.minecraft",equals="1.20.1"},wanted);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Dependency conflict accepted");
   denied=false;try{MetadataCache.DependencyVersion(new MetaRequirement{uid="unknown.component"},wanted);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Unknown unconstrained dependency accepted");
   if(GameFiles.LegacyUrl(new GameLibrary{name="net.fabricmc:intermediary:1.21.1",url="https://maven.fabricmc.net"})!="https://maven.fabricmc.net/net/fabricmc/intermediary/1.21.1/intermediary-1.21.1.jar")throw new Exception("Fabric Maven URL failed");
   foreach(string version in new[]{"17.0.20.1","21.0.12.1","1.8.0_402"})if(JavaRuntime.VersionMajor(version)!=(version.StartsWith("1.8")?8:Int32.Parse(version.Substring(0,2))))throw new Exception("Java major parse failed");
   if(JavaRuntime.RequiredMajors(root,"1.21.1")[0]!=21||JavaRuntime.RequiredMajors(root,"1.20.1")[0]!=17||JavaRuntime.RequiredMajors(root,"1.20.5")[0]!=21)throw new Exception("Java release selection failed");
   foreach(int major in new[]{17,21}){string home=Path.Combine(root,"runtime","jdk-"+major,"bin");Directory.CreateDirectory(home);JavaManagementTests.FakeExe(Path.Combine(home,"javaw.exe"));File.WriteAllText(Path.Combine(home,"..","release"),"JAVA_VERSION=\""+major+".0.1\"");}
   var javaCfg=new System.Collections.Generic.Dictionary<string,string>();JavaRuntime.Configure(root,"1.20.1",javaCfg,true);if(javaCfg["JavaVersion"]!="17.0.1")throw new Exception("Java17 selection failed");
   JavaRuntime.Configure(root,"1.21.1",javaCfg,true);if(javaCfg["JavaVersion"]!="21.0.1")throw new Exception("Managed Java17 not replaced for 1.21.1");
   LauncherUpdateTests.Run(root);
   ExportTests.Run(root);
   SaveExportTests.Run(root);
   AccountTests.Run(root);
   CurseForgeTests.Run(root);
   PackDropTests.Run(root);
   SkinTests.Run(root);
   SkinApplicationTests.Run(root);
   AppearanceTests.Run(root);
   CatalogTests.Run(root);
   PresentationTests.Run(root);JavaManagementTests.Run(root);ModManagerTests.Run(root);LocalModImportTests.Run(root);VanillaTests.Run(root);LoaderTests.Run(root);
   Console.WriteLine("PASS: traversal, import, hashes, metadata hash chain, INI sections, Maven/classifiers, library rules, Fabric/Quilt dependencies, dependency conflicts, Java17/21 selection.");return 0;
  }
 }
}
