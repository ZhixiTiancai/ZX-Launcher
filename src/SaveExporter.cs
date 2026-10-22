using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FoolLauncher {
 public sealed class SaveExporter {
  readonly PackService Service;
  public SaveExporter(PackService service){Service=service;}
  public string Saves(Instance instance){return Path.Combine(Service.Game(instance),"saves");}
  public static string SuggestedName(Instance instance) {
   string name=instance.Name??"整合包";foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
   name=new string(name.Where(c=>!Char.IsControl(c)).Take(70).ToArray()).Trim().TrimEnd('.');
   if(name.Length>0&&Char.IsHighSurrogate(name[name.Length-1]))name=name.Substring(0,name.Length-1);
   return (String.IsNullOrWhiteSpace(name)?"整合包":name)+"-存档-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip";
  }
  static void CheckFolder(string folder) {
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("存档目录包含文件夹链接，无法完整导出："+Path.GetFileName(folder));
  }
  public Task<ExportResult> Export(Instance instance,string destination,IProgress<Update> progress,CancellationToken ct) {
   return Task.Run(()=>{
    ct.ThrowIfCancellationRequested();string saves=Path.GetFullPath(Saves(instance)).TrimEnd('\\','/'),target=Path.GetFullPath(destination);
    string dataPrefix=Path.GetFullPath(Service.Data).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
    if(target.StartsWith(dataPrefix,StringComparison.OrdinalIgnoreCase))throw new IOException("请将存档 ZIP 保存到 data 文件夹以外，例如启动器的 exports 文件夹。");
    if(!Path.GetExtension(target).Equals(".zip",StringComparison.OrdinalIgnoreCase))throw new IOException("请使用 .zip 扩展名。");
    if(!Directory.Exists(saves)||!Directory.EnumerateDirectories(saves).Any())throw new IOException("这个整合包还没有存档。请先在游戏中创建世界并保存退出。");
    CheckFolder(Service.Game(instance));CheckFolder(saves);
    var files=new List<string>();var directories=new List<string>();var pending=new Queue<string>();pending.Enqueue(saves);
    var locks=new List<FileStream>();string temp=target+".saves-"+Guid.NewGuid().ToString("N")+".tmp";long total=0;
    try {
     progress.Report(new Update("正在检查和整理存档…"));
     while(pending.Count>0) {
      ct.ThrowIfCancellationRequested();string dir=pending.Dequeue();
      foreach(string entry in Directory.EnumerateFileSystemEntries(dir).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)) {
       ct.ThrowIfCancellationRequested();string relative=entry.Substring(saves.Length+1).Replace('\\','/');PackService.SafePath(saves,relative);
       var attributes=File.GetAttributes(entry);
       if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("存档包含文件或文件夹链接，无法完整导出："+relative);
       if((attributes&FileAttributes.Directory)!=0){directories.Add(relative);pending.Enqueue(entry);continue;}
       if(Path.GetFileName(entry).Equals("session.lock",StringComparison.OrdinalIgnoreCase)) {
        FileStream held=null;
        try{held=new FileStream(entry,FileMode.Open,FileAccess.ReadWrite,FileShare.None);held.Lock(0,1);locks.Add(held);}
        catch(IOException){if(held!=null)held.Dispose();throw new IOException("存档正在使用或被其他程序锁定，请保存世界并退出游戏后再导出。");}
        continue;
       }
       files.Add(entry);
      }
     }
     if(files.Count==0)throw new IOException("saves 文件夹中没有可导出的存档文件。");
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)) {
      foreach(string directory in directories)zip.CreateEntry("saves/"+directory+"/");
      int done=0;var buffer=new byte[131072];
      foreach(string file in files) {
       ct.ThrowIfCancellationRequested();string relative=file.Substring(saves.Length+1).Replace('\\','/');
       // World regions, dimensions, player data, level.dat_old and datapacks all belong to the backup.
       var entry=zip.CreateEntry("saves/"+relative,CompressionLevel.Optimal);
       using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=entry.Open()) {
        int n;while((n=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();output.Write(buffer,0,n);total+=n;}
       }
       progress.Report(new Update("正在导出存档 "+(++done)+" / "+files.Count+"："+relative,done,files.Count));
      }
      using(var writer=new StreamWriter(zip.CreateEntry("存档恢复说明.txt").Open(),new UTF8Encoding(false)))writer.Write("整合包："+instance.Name+"\r\nMinecraft："+instance.Minecraft+"\r\n模组加载器："+instance.Loader+"\r\n\r\n这是存档备份 ZIP，不是整合包，请勿通过“导入整合包”导入。\r\n先安装相同版本的整合包，并退出游戏。\r\n解压此 ZIP，打开 saves 文件夹，将需要的世界文件夹复制到目标整合包“游戏目录”中的 saves 文件夹。\r\n如已有同名世界，先备份原存档或为导出的世界文件夹改名，避免覆盖。\r\n进入游戏后可在单人游戏列表中找到世界。\r\n本备份保留世界及玩家数据，不含模组、启动器账号或运行中的 session.lock 文件。\r\n");
     }
     ct.ThrowIfCancellationRequested();if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
     progress.Report(new Update("存档导出完成："+target,files.Count,files.Count));return new ExportResult{Path=target,Files=files.Count,Bytes=total};
    }finally{foreach(var held in locks)held.Dispose();if(File.Exists(temp))File.Delete(temp);}
   },ct);
  }
 }
}
