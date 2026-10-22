using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;
namespace FoolLauncher {
 public static class PackRemoval {
  internal static string CheckedFolder(PackService service,Instance instance){
   if(instance==null||String.IsNullOrWhiteSpace(instance.Id)||Path.GetFileName(instance.Id)!=instance.Id||instance.Id.Contains("/")||instance.Id.Contains("\\"))throw new InvalidDataException(UiText.T("整合包路径无效，已阻止删除。"));
   string folder=service.Folder(instance);
   if(!Directory.Exists(folder)||!File.Exists(Path.Combine(folder,"launcher-instance.json")))throw new InvalidDataException(UiText.T("找不到此整合包，请刷新后重试。"));
   var current=new DirectoryInfo(folder);while(current!=null){if((current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(UiText.T("整合包含有目录链接，无法安全移入回收站。"));current=current.Parent;}
   CheckLinks(new DirectoryInfo(folder));return folder;
  }
  static void CheckLinks(DirectoryInfo directory){foreach(var item in directory.EnumerateFileSystemInfos()){if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(UiText.T("整合包含有目录链接，无法安全移入回收站。"));var child=item as DirectoryInfo;if(child!=null)CheckLinks(child);else if(item.Name.Equals("session.lock",StringComparison.OrdinalIgnoreCase)){try{using(var held=new FileStream(item.FullName,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){held.Lock(0,1);held.Unlock(0,1);}}catch(IOException){throw new IOException(UiText.T("存档仍在使用，请保存世界并退出游戏后再删除整合包。"));}}}}
  public static void Recycle(PackService service,Instance instance){string folder=CheckedFolder(service,instance);FileSystem.DeleteDirectory(folder,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);}
 }
}
