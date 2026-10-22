using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace FoolLauncher {
 public sealed class SignedUpdate {public string Payload{get;set;}public string Signature{get;set;}}
 public sealed class UpdateFile {public string Name{get;set;}public long Size{get;set;}public string Sha256{get;set;}}
 public sealed class UpdateManifest {
  public string Product{get;set;}public int Format{get;set;}public string Kind{get;set;}public string Version{get;set;}
  public string Notes{get;set;}public UpdateFile[] Files{get;set;}public string Url{get;set;}public long Size{get;set;}public string Sha256{get;set;}
 }
 public sealed class VerifiedUpdate {public string Version,Notes,Folder;public UpdateFile[] Files;}
 public static class LauncherUpdateCore {
  public const long MaxPackage=32*1024*1024,MaxExpanded=64*1024*1024;
  public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=20};}
  public static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  public static string HashFile(string path){using(var stream=File.OpenRead(path))return Hash(stream);}
  public static Version VersionOf(string text){Version version;if(!Regex.IsMatch(text??"",@"\A\d{1,4}\.\d{1,4}\.\d\z")||!Version.TryParse(text,out version))throw new InvalidDataException("Invalid update version.");return version;}
  public static bool Allowed(string name) {
   if(String.IsNullOrEmpty(name)||name.Length>220||name.Contains('\\')||name.Contains(':')||name.StartsWith("/"))return false;
   foreach(string part in name.Split('/'))if(part.Length==0||part=="."||part==".."||part.EndsWith(".")||part.EndsWith(" ")||Regex.IsMatch(part,@"[\x00-\x1f<>\""|?*]")||Regex.IsMatch(part,@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|\z)",RegexOptions.IgnoreCase))return false;
   if(new[]{"智熄的启动器.exe","智熄更新器.exe","App.xaml","使用说明.txt","THIRD-PARTY.txt","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.Wpf.dll","WebView2Loader.dll","WebView2-LICENSE.txt"}.Contains(name,StringComparer.Ordinal))return true;
   if(new[]{"source/LICENSE","source/README.md","source/使用说明.txt","source/朋友版说明.txt","source/THIRD-PARTY.txt"}.Contains(name,StringComparer.Ordinal))return true;
   if(name=="source/src/assets/README.txt")return true;
   if(name.StartsWith("source/src/",StringComparison.Ordinal)){string ext=Path.GetExtension(name);return new[]{".cs",".xaml",".png"}.Contains(ext,StringComparer.OrdinalIgnoreCase);}
   return name.StartsWith("source/build/",StringComparison.Ordinal)&&new[]{"build.ps1","build-updater.ps1","fetch-webview.ps1","fetch-deps.ps1","assemble.ps1","package-friends.py","package-program-update.ps1","install-update.ps1","initialize-update-key.ps1","UpdaterProgram.cs"}.Contains(name.Substring(13),StringComparer.Ordinal);
  }
  public static UpdateManifest ReadSigned(byte[] bytes,string kind){return ReadSigned(bytes,kind,UpdateTrust.PublicKey);}
  internal static UpdateManifest ReadSigned(byte[] bytes,string kind,string publicKey) {
   if(bytes.Length>256*1024)throw new InvalidDataException("Update manifest is too large.");
   try {
    var signed=Serializer().Deserialize<SignedUpdate>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
    byte[] payload=Convert.FromBase64String(signed.Payload),signature=Convert.FromBase64String(signed.Signature);
    using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(publicKey);if(!rsa.VerifyData(payload,CryptoConfig.MapNameToOID("SHA256"),signature))throw new InvalidDataException("Update signature verification failed.");}
    var manifest=Serializer().Deserialize<UpdateManifest>(Encoding.UTF8.GetString(payload));
    if(manifest.Product!="ZhixiLauncher"||manifest.Format!=1||manifest.Kind!=kind)throw new InvalidDataException("Update format does not match this launcher.");
    VersionOf(manifest.Version);if((manifest.Notes??"").Length>6000)throw new InvalidDataException("Update notes are too long.");
    if(kind=="feed") {Https(manifest.Url);if(manifest.Size<1||manifest.Size>MaxPackage||!Regex.IsMatch(manifest.Sha256??"",@"\A[a-f0-9]{64}\z"))throw new InvalidDataException("Invalid update download details.");}
    return manifest;
   }catch(InvalidDataException){throw;}catch(Exception ex){throw new InvalidDataException("Unable to verify this update manifest.",ex);}
  }
  public static Uri Https(string url){Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo)||!String.IsNullOrEmpty(uri.Fragment)||uri.IsLoopback)throw new InvalidDataException("Use a public HTTPS update address.");return uri;}
  public static void NoLinks(string path) {
   string full=Path.GetFullPath(path);for(var info=new DirectoryInfo(full);info!=null;info=info.Parent)if(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Update path contains a filesystem link.");
   if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("Update file is a filesystem link.");
  }
  public static void ValidateRoot(string root) {
   root=Path.GetFullPath(root);NoLinks(root);
   if(!File.Exists(Path.Combine(root,"engine","prismlauncher.exe"))||(!File.Exists(Path.Combine(root,"智熄的启动器.exe"))&&!File.Exists(Path.Combine(root,"愚者启动器.exe"))))throw new IOException("请选择原来的启动器文件夹（应包含启动器 EXE 和 engine 文件夹）。");
  }
  public static VerifiedUpdate Stage(string package,string output,CancellationToken cancel){return Stage(package,output,cancel,UpdateTrust.PublicKey);}
  internal static VerifiedUpdate Stage(string package,string output,CancellationToken cancel,string publicKey) {
   if(new FileInfo(package).Length>MaxPackage)throw new InvalidDataException("Update ZIP is too large.");
   NoLinks(output);Directory.CreateDirectory(output);if(Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Update staging folder must be empty.");
   using(var zip=ZipFile.OpenRead(package)) {
    if(zip.Entries.Count>1500)throw new InvalidDataException("Too many update files.");
    var duplicates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var entry in zip.Entries)if(!duplicates.Add(entry.FullName))throw new InvalidDataException("Duplicate update file.");
    var record=zip.GetEntry("update-manifest.json");if(record==null||record.Length>256*1024)throw new InvalidDataException("This is not a signed launcher update ZIP.");
    byte[] raw;using(var stream=record.Open())using(var memory=new MemoryStream()){CopyBounded(stream,memory,256*1024,cancel);raw=memory.ToArray();}
    var manifest=ReadSigned(raw,"package",publicKey);
    if(manifest.Files==null||manifest.Files.Length<3||manifest.Files.Length>1400)throw new InvalidDataException("Invalid update file list.");
    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
    foreach(var file in manifest.Files){if(!Allowed(file.Name)||!names.Add(file.Name)||file.Size<0||file.Size>MaxExpanded||!Regex.IsMatch(file.Sha256??"",@"\A[a-f0-9]{64}\z"))throw new InvalidDataException("Update attempts to replace a protected or invalid file: "+file.Name);total+=file.Size;}
    if(total>MaxExpanded||zip.Entries.Count!=manifest.Files.Length+1||!new[]{"智熄的启动器.exe","智熄更新器.exe","App.xaml"}.All(names.Contains))throw new InvalidDataException("Incomplete update package.");
    foreach(var file in manifest.Files) {
     cancel.ThrowIfCancellationRequested();var entry=zip.GetEntry(file.Name);if(entry==null||entry.Length!=file.Size)throw new InvalidDataException("Update file size mismatch.");
     if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Links are not accepted in updates.");
     string path=Path.Combine(output,file.Name.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));
     using(var source=entry.Open())using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))CopyBounded(source,target,file.Size,cancel);
     if(new FileInfo(path).Length!=file.Size||HashFile(path)!=file.Sha256)throw new InvalidDataException("Update file checksum mismatch.");
    }
    return new VerifiedUpdate{Version=manifest.Version,Notes=manifest.Notes,Folder=Path.GetFullPath(output),Files=manifest.Files};
   }
  }
  public static void CopyBounded(Stream source,Stream target,long max,CancellationToken cancel){byte[] buffer=new byte[65536];long count=0;int n;while((n=source.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();count+=n;if(count>max)throw new InvalidDataException("Download exceeds expected size.");target.Write(buffer,0,n);}}
  public static string Apply(VerifiedUpdate update,string root,Action<int> afterWrite=null) {
   ValidateRoot(root);root=Path.GetFullPath(root);string lockFile=Path.Combine(root,".launcher-update.lock");NoLinks(lockFile);
   using(var held=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))return ApplyLocked(update,root,afterWrite);
  }
  static string ApplyLocked(VerifiedUpdate update,string root,Action<int> afterWrite) {
   NoLinks(update.Folder);
   string backup=Path.Combine(root,"updates-backup",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));NoLinks(backup);
   // Recheck every staged hash and target lock before changing any installed file.
   foreach(var file in update.Files){if(!Allowed(file.Name))throw new InvalidDataException("Protected update path.");string source=Path.Combine(update.Folder,file.Name),target=Path.Combine(root,file.Name);NoLinks(source);NoLinks(target);if(new FileInfo(source).Length!=file.Size||HashFile(source)!=file.Sha256)throw new InvalidDataException("Staged update changed.");if(File.Exists(target))using(var held=File.Open(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }
   Directory.CreateDirectory(backup);var written=new List<string>();var newFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   try {
    foreach(var file in update.Files) {
     string target=Path.Combine(root,file.Name),saved=Path.Combine(backup,file.Name),temp=target+".update-"+Guid.NewGuid().ToString("N");
     NoLinks(target);Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.CreateDirectory(Path.GetDirectoryName(saved));
     try {File.Copy(Path.Combine(update.Folder,file.Name),temp);if(new FileInfo(temp).Length!=file.Size||HashFile(temp)!=file.Sha256)throw new InvalidDataException("Update changed while copying.");if(File.Exists(target))File.Replace(temp,target,saved);else{File.Move(temp,target);newFiles.Add(file.Name);}written.Add(file.Name);if(afterWrite!=null)afterWrite(written.Count);}
     finally{if(File.Exists(temp))File.Delete(temp);}
    }
    File.WriteAllText(Path.Combine(backup,"update-result.json"),Serializer().Serialize(new{Version=update.Version,Completed=true,Files=written}),new UTF8Encoding(false));return backup;
   }catch(Exception failure) {
    var rollback=new List<string>();foreach(string name in Enumerable.Reverse(written))try{string target=Path.Combine(root,name),saved=Path.Combine(backup,name);if(newFiles.Contains(name))File.Delete(target);else File.Copy(saved,target,true);}catch{rollback.Add(name);}
    File.WriteAllText(Path.Combine(backup,"update-result.json"),Serializer().Serialize(new{Version=update.Version,Completed=false,RollbackFailed=rollback}),new UTF8Encoding(false));
    throw new IOException(rollback.Count==0?"更新未完成，已恢复原来的程序文件。用户数据未改动。":"更新未完成，请从备份恢复程序文件："+backup,failure);
   }
  }
 }
}
