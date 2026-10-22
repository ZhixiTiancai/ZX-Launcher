using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public static class CurseForgeTests {
  sealed class Reporter:IProgress<Update>{public void Report(Update value){}}
  static CurseFile MakeFile(int project,int id,string name,string url){string sha;using(var hash=SHA1.Create())sha=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("verified"))).Replace("-","").ToLowerInvariant();return new CurseFile{id=id,modId=project,gameId=432,isAvailable=true,fileName=name,fileLength=8,downloadUrl=url,hashes=new[]{new CurseHash{algo=1,value=sha}}};}
  public static void Run(string root) {
   var service=new PackService(Path.Combine(root,"curseforge-import"));var progress=new Reporter();
   var manifest=Json.Parse<CurseManifest>("{\"manifestType\":\"minecraftModpack\",\"manifestVersion\":1,\"name\":\"CF test\",\"version\":\"v1\",\"overrides\":\"custom-data\",\"minecraft\":{\"version\":\"1.20.1\",\"modLoaders\":[{\"id\":\"forge-47.4.20\",\"primary\":true}]},\"files\":[{\"projectID\":1,\"fileID\":101},{\"projectID\":2,\"fileID\":102},{\"projectID\":3,\"fileID\":103},{\"projectID\":4,\"fileID\":104},{\"projectID\":5,\"fileID\":105,\"required\":false}]}");
   var responses=new Dictionary<string,object>{{"mods/1/files/101",new CurseFileResponse{data=MakeFile(1,101,"mod.jar","https://example.com/mod.jar")}},{"mods/2/files/102",new CurseFileResponse{data=MakeFile(2,102,"resources.zip","https://example.com/resources.zip")}},{"mods/3/files/103",new CurseFileResponse{data=MakeFile(3,103,"shaders.zip","https://example.com/shaders.zip")}},{"mods/4/files/104",new CurseFileResponse{data=MakeFile(4,104,"manual.jar",null)}},{"mods/2",new CurseProjectResponse{data=new CurseProject{id=2,gameId=432,classId=12}}},{"mods/3",new CurseProjectResponse{data=new CurseProject{id=3,gameId=432,classId=6552}}},{"mods/4",new CurseProjectResponse{data=new CurseProject{id=4,gameId=432,classId=6,links=new CurseLinks{websiteUrl="https://www.curseforge.com/minecraft/mc-mods/manual-test"}}}}};
   var resolver=new CurseForgePack(service.Data,(path,ct)=>{ct.ThrowIfCancellationRequested();if(!responses.ContainsKey(path))throw new Exception("Unexpected metadata request: "+path);return Task.FromResult(AccountStore.Serialize(responses[path]));});
   var index=resolver.Resolve(manifest,progress,CancellationToken.None).GetAwaiter().GetResult();service.Validate(index);
   if(index.files.Length!=4||!index.files.Any(f=>f.path=="resourcepacks/resources.zip")||!index.files.Any(f=>f.path=="shaderpacks/shaders.zip")||index.dependencies["forge"]!="47.4.20")throw new Exception("CurseForge categorization/loader/default-required failed");
   var manual=index.files.Single(f=>f.path=="mods/manual.jar");if(manual.downloads.Length!=0||manual.manualUrl!="https://www.curseforge.com/minecraft/mc-mods/manual-test/download/104")throw new Exception("Restricted CF file was given invented download URL");
   string zip=Path.Combine(root,"curseforge-test.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)) {
    using(var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open()))writer.Write(AccountStore.Serialize(manifest));
    using(var writer=new StreamWriter(archive.CreateEntry("custom-data/config/keep.txt").Open()))writer.Write("custom overrides");
    using(var writer=new StreamWriter(archive.CreateEntry("overrides/config/wrong.txt").Open()))writer.Write("not selected");
    using(var writer=new StreamWriter(archive.CreateEntry("client-overrides/config/wrong.txt").Open()))writer.Write("not CF overrides");
   }
   var instance=service.Import(zip,progress,CancellationToken.None).GetAwaiter().GetResult();
   if(instance.FileCount!=4||instance.Ready||instance.Loader!="forge 47.4.20"||File.ReadAllText(Path.Combine(service.Game(instance),"config/keep.txt"))!="custom overrides"||File.Exists(Path.Combine(service.Game(instance),"config/wrong.txt")))throw new Exception("CF import ignored overrides or marked incomplete instance ready");
   foreach(var file in index.files.Where(f=>f.downloads.Length>0)){string target=PackService.SafePath(service.Game(instance),file.path);Directory.CreateDirectory(Path.GetDirectoryName(target));System.IO.File.WriteAllText(target,"verified",new UTF8Encoding(false));}
   if(service.Repair(instance,progress,CancellationToken.None).GetAwaiter().GetResult()!=1||instance.Ready)throw new Exception("Missing manual-download mod was silently ignored");
   var missing=Json.Read<MissingFile[]>(Path.Combine(service.Folder(instance),"missing-files.json"));if(missing.Length!=1||missing[0].downloads.Single()!=manual.manualUrl)throw new Exception("Missing-file report omitted manual download link");
   string local=Path.Combine(root,"manual.jar");System.IO.File.WriteAllText(local,"verified",new UTF8Encoding(false));if(service.AddLocalFiles(instance,new[]{local},CancellationToken.None).GetAwaiter().GetResult()!=1||service.Repair(instance,progress,CancellationToken.None).GetAwaiter().GetResult()!=0||!instance.Ready)throw new Exception("Manual CF file restore/hash verification failed");
   bool denied=false;var wrong=MakeFile(1,999,"test.jar","https://example.com/test.jar");try{CurseForgePack.ValidateFile(manifest.files[0],wrong);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Mismatched CF metadata IDs accepted");
   var archived=MakeFile(1,101,"archived.jar","https://edge.forgecdn.net/files/0/101/archived.jar");archived.isAvailable=false;archived.fileStatus=8;CurseForgePack.ValidateFile(manifest.files[0],archived);
   foreach(int blocked in new[]{0,5,6,7}){archived.fileStatus=blocked;bool blockedFile=false;try{CurseForgePack.ValidateFile(manifest.files[0],archived);}catch(InvalidDataException){blockedFile=true;}if(!blockedFile)throw new Exception("Unavailable/rejected/malware/deleted CF file accepted");}
   archived.fileStatus=8;archived.hashes=new CurseHash[0];denied=false;try{CurseForgePack.ValidateFile(manifest.files[0],archived);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Archived file accepted without SHA1");
   wrong=MakeFile(1,101,"../outside.jar","https://example.com/test.jar");denied=false;try{CurseForgePack.ValidateFile(manifest.files[0],wrong);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Unsafe CF filename accepted");
   manifest.overrides="../outside";denied=false;try{CurseForgePack.Dependencies(manifest);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("CF override traversal accepted");manifest.overrides="overrides";
   manifest.files[1].fileID=101;denied=false;try{resolver.Resolve(manifest,progress,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Duplicate CF references accepted");
   Console.WriteLine("PASS: CurseForge import, Forge version, custom overrides, cached metadata, optional-file semantics, resource/shader categorization, SHA-1, restricted-download reporting/manual restore and malicious manifest rejection.");
  }
 }
}
