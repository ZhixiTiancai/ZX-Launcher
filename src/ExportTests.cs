using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace FoolLauncher {
 public static class ExportTests {
  sealed class Reporter:IProgress<Update> {readonly Action<Update> Callback;public Reporter(Action<Update> callback){Callback=callback;}public void Report(Update update){Callback(update);}}
  static void Write(string root,string path,string text){string file=PackService.SafePath(root,path);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text,new UTF8Encoding(false));}
  static PackFile Remote(string path,string text) {var bytes=Encoding.UTF8.GetBytes(text);string hash;using(var sha=SHA512.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");return new PackFile{path=path,fileSize=bytes.Length,hashes=new Dictionary<string,string>{{"sha512",hash}},downloads=new[]{"https://example.invalid/should-never-be-downloaded.jar"}};}
  public static void Run(string root) {
   var progress=new Reporter(u=>{});var service=new PackService(Path.Combine(root,"export-source"));
   var instance=new Instance{Id="roundtrip",Name="朋友的整合包",Version="3.1",Minecraft="1.20.1",Loader="forge 47.4.12",Ready=true,FileCount=3};string folder=service.Folder(instance),game=service.Game(instance);Directory.CreateDirectory(game);
   var pack=new PackIndex{game="minecraft",formatVersion=1,name="旧名称",versionId="3.1",dependencies=new Dictionary<string,string>{{"minecraft","1.20.1"},{"forge","47.4.12"}},files=new[]{Remote("mods/keep.jar","unchanged"),Remote("mods/changed.jar","old"),Remote("mods/removed.jar","removed")}};
   Json.Write(Path.Combine(folder,"pack-index.json"),pack);Json.Write(Path.Combine(folder,"launcher-instance.json"),instance);
   Json.Write(Path.Combine(folder,"mmc-pack.json"),new MetaComponents{components=new[]{new MetaComponent{uid="net.minecraft",version="1.21.1"},new MetaComponent{uid="net.fabricmc.fabric-loader",version="0.19.1"}}});
   Write(folder,"instance.cfg","[General]\nname=朋友的整合包\nMaxMemAlloc=8192\n");
   foreach(var item in new Dictionary<string,string>{{"mods/keep.jar","unchanged"},{"mods/changed.jar","modified"},{"mods/added.jar","new mod"},{"config/settings.json","{\"enabled\":true}"},{"kubejs/assets/demo/textures/test.txt","resource"},{"resourcepacks/unpacked/assets/minecraft/textures/test.txt","texture"},{"rosmontislib/custom-data.txt","pack data"},{"saves/world/level.dat","private world"},{"screenshots/private.png","private screenshot"},{"logs/latest.log","private log"},{".fabric/cache.jar","runtime cache"},{"servers.dat","private address"},{"usercache.json","private player"},{"config/essential/accounts.json","private account"},{"config/private-api.json","{\"refresh_token\":\"actual_secret_token_0123456789\"}"}})Write(game,item.Key,item.Value);
   Write(service.Data,"accounts.json","launcher account outside game");
   Write(game,"mods/client-token-helper.jar","mod with token in its name");
   byte[] icon;using(var input=typeof(PackIcons).Assembly.GetManifestResourceStream("FoolLauncher.DefaultGrass.png"))using(var bytes=new MemoryStream()){input.CopyTo(bytes);icon=bytes.ToArray();}new PackIcons(service.Root).SetBytes(instance,icon,CancellationToken.None);
   var exporter=new PackExporter(service);string zip=Path.Combine(root,"roundtrip.zip");var result=exporter.Export(instance,zip,progress,CancellationToken.None).GetAwaiter().GetResult();
   using(var archive=ZipFile.OpenRead(zip)) {
    var names=archive.Entries.Select(e=>e.FullName).ToArray();
    foreach(string expected in new[]{"overrides/mods/keep.jar","overrides/mods/changed.jar","overrides/mods/added.jar","overrides/mods/client-token-helper.jar","overrides/kubejs/assets/demo/textures/test.txt","overrides/resourcepacks/unpacked/assets/minecraft/textures/test.txt","overrides/rosmontislib/custom-data.txt","zhixi-launcher/icon.png","zhixi-launcher/files.json"})if(!names.Contains(expected))throw new Exception("Export omitted pack content: "+expected);
    foreach(string forbidden in new[]{"/saves/","/screenshots/","/logs/","/.fabric/","accounts.json","servers.dat","usercache.json","private-api.json","removed.jar"})if(names.Any(n=>n.Contains(forbidden)))throw new Exception("Export included private/removed content: "+forbidden);
    PackIndex exported;using(var reader=new StreamReader(archive.GetEntry("modrinth.index.json").Open()))exported=Json.Parse<PackIndex>(reader.ReadToEnd());
    if(exported.files.Length!=0||exported.name!=instance.Name||exported.dependencies["minecraft"]!="1.21.1"||exported.dependencies["fabric-loader"]!="0.19.1"||exported.dependencies.ContainsKey("forge"))throw new Exception("Export manifest does not reflect current instance");
   }
   var recipient=new PackService(Path.Combine(root,"export-recipient"));var imported=recipient.Import(zip,progress,CancellationToken.None).GetAwaiter().GetResult();
   if(imported.Name!=instance.Name||imported.Minecraft!="1.21.1"||imported.BundledFileCount!=result.Files||imported.FileCount!=0||File.ReadAllText(Path.Combine(recipient.Game(imported),"mods/changed.jar"))!="modified"||File.Exists(Path.Combine(recipient.Game(imported),"mods/removed.jar")))throw new Exception("Export round-trip lost current pack state");
   if(new PackIcons(recipient.Root).CustomBytes(imported)==null)throw new Exception("Export round-trip lost custom icon");
   if(recipient.Repair(imported,progress,CancellationToken.None).GetAwaiter().GetResult()!=0)throw new Exception("Bundled pack attempted to redownload files");
   string cancelled=Path.Combine(root,"cancelled-export.zip");File.WriteAllText(cancelled,"previous file");
   using(var cancel=new CancellationTokenSource()) {
    bool rejected=false;try{exporter.Export(instance,cancelled,new Reporter(u=>{if(u.Done>0)cancel.Cancel();}),cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}
    if(!rejected||File.ReadAllText(cancelled)!="previous file"||Directory.GetFiles(root,"*.export-*.tmp").Length!=0)throw new Exception("Cancelled export damaged output or left partial ZIP");
   }
   bool denied=false;try{exporter.Export(instance,Path.Combine(service.Data,"unsafe.zip"),progress,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}if(!denied)throw new Exception("Export into instance data was accepted");
   string corrupt=Path.Combine(root,"tampered-export.zip");File.Copy(zip,corrupt);
   using(var archive=ZipFile.Open(corrupt,ZipArchiveMode.Update)){archive.GetEntry("overrides/mods/keep.jar").Delete();using(var writer=new StreamWriter(archive.CreateEntry("overrides/mods/keep.jar").Open()))writer.Write("tampered");}
   var invalidRecipient=new PackService(Path.Combine(root,"export-corruption-check"));denied=false;try{invalidRecipient.Import(corrupt,progress,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){denied=true;}if(!denied||invalidRecipient.List().Count!=0)throw new Exception("Corrupt bundled export accepted");
   Console.WriteLine("PASS: export/import round-trip, added/modified/removed mods, current components, bundled icon, resource assets, privacy exclusions, offline mod restore, cancellation atomicity and tamper rejection.");
  }
 }
}
