using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
namespace FoolLauncher {
 public static class SaveExportTests {
  sealed class Reporter:IProgress<Update> {readonly Action<Update> Action;public Reporter(Action<Update> action){Action=action;}public void Report(Update value){Action(value);}}
  static void Write(string root,string relative,string value){string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
  public static void Run(string root) {
   var service=new PackService(Path.Combine(root,"save-export"));var instance=new Instance{Id="worlds-test",Name="存档测试",Minecraft="1.20.1",Loader="forge 47.4.12"};
   var exporter=new SaveExporter(service);var progress=new Reporter(u=>{});string output=Path.Combine(root,"worlds.zip"),saves=exporter.Saves(instance);
   bool denied=false;try{exporter.Export(instance,output,progress,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}
   if(!denied||File.Exists(output))throw new Exception("Empty saves accepted");
   foreach(string file in new[]{"测试世界/level.dat","测试世界/level.dat_old","测试世界/region/r.0.0.mca","测试世界/DIM-1/region/r.1.0.mca","测试世界/DIM1/region/r.0.1.mca","测试世界/playerdata/player.dat","测试世界/advancements/player.json","测试世界/datapacks/example.zip","测试世界/dimensions/mod/moon/region/r.0.0.mca","第二个世界/level.dat"})Write(saves,file,"contents:"+file);
   Write(saves,"测试世界/session.lock","lock marker");Write(service.Game(instance),"mods/not-a-save.jar","not a world");Directory.CreateDirectory(Path.Combine(saves,"测试世界/empty"));
   string session=Path.Combine(saves,"测试世界/session.lock");File.WriteAllText(output,"previous archive");
   using(var held=new FileStream(session,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
    denied=false;try{exporter.Export(instance,output,progress,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}
    if(!denied||File.ReadAllText(output)!="previous archive")throw new Exception("Active world export did not preserve old output");
   }
   var result=exporter.Export(instance,output,progress,CancellationToken.None).GetAwaiter().GetResult();
   using(var archive=ZipFile.OpenRead(output)) {
    if(archive.Entries.Any(e=>e.FullName.EndsWith("session.lock")||e.FullName.Contains("mods/"))||archive.GetEntry("存档恢复说明.txt")==null||archive.GetEntry("saves/测试世界/empty/")==null)throw new Exception("Save archive layout/exclusions failed");
    var originals=Directory.GetFiles(saves,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!="session.lock").ToArray();
    if(result.Files!=originals.Length)throw new Exception("Save file count mismatch");
    foreach(string file in originals) {
     string name="saves/"+file.Substring(saves.Length+1).Replace('\\','/');var entry=archive.GetEntry(name);if(entry==null)throw new Exception("World data omitted: "+name);
     using(var reader=new StreamReader(entry.Open()))if(reader.ReadToEnd()!=File.ReadAllText(file))throw new Exception("World data differs: "+name);
    }
   }
   byte[] previous=File.ReadAllBytes(output);
   using(var cancel=new CancellationTokenSource()) {
    denied=false;try{exporter.Export(instance,output,new Reporter(u=>{if(u.Done>0)cancel.Cancel();}),cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){denied=true;}
    if(!denied||!File.ReadAllBytes(output).SequenceEqual(previous)||Directory.GetFiles(root,"*.saves-*.tmp").Length!=0)throw new Exception("Save export cancellation damaged previous archive");
   }
   // All session locks must be released even after cancellation.
   using(var held=new FileStream(session,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
   denied=false;try{exporter.Export(instance,Path.Combine(saves,"inside.zip"),progress,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){denied=true;}if(!denied)throw new Exception("Save export into source accepted");
   if(File.ReadAllText(session)!="lock marker")throw new Exception("Session lock was modified");
   Console.WriteLine("PASS: save ZIP content restore, multiple worlds/dimensions/player data, empty folders, session lock rejection/release, no-saves handling, output protection and cancellation atomicity.");
  }
 }
}
