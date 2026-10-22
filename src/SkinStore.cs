using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;
namespace FoolLauncher {
 public class SavedSkin {public string Id{get;set;}public string Name{get;set;}public string Source{get;set;}public string Added{get;set;}public int Height{get;set;}public bool Slim{get;set;}public bool Favorite{get;set;}}
 public sealed class SkinStore {
  public const int MaxBytes=1024*1024;public readonly string Folder;
  public SkinStore(string root){Folder=Path.Combine(Path.GetFullPath(root),"data","skins");}
  public string ImagePath(SavedSkin skin){if(skin==null||!Regex.IsMatch(skin.Id??"",@"\A[a-f0-9]{64}\z"))throw new InvalidDataException(UiText.T("无效的皮肤编号。"));return Path.Combine(Folder,skin.Id+".png");}
  public List<SavedSkin> List(){var list=new List<SavedSkin>();if(!Directory.Exists(Folder))return list;foreach(var path in Directory.EnumerateFiles(Folder,"*.json")){try{var s=Json.Read<SavedSkin>(path);if(File.Exists(ImagePath(s))&&Path.GetFileNameWithoutExtension(path)==s.Id)list.Add(s);}catch{}}return list.OrderByDescending(s=>s.Favorite).ThenByDescending(s=>s.Added).ThenBy(s=>s.Id,StringComparer.Ordinal).ToList();}
  SavedSkin Existing(SavedSkin skin){string image=ImagePath(skin),record=Path.ChangeExtension(image,".json");if(!File.Exists(image)||!File.Exists(record))throw new IOException(UiText.T("找不到此皮肤，请刷新列表后重试。"));var current=Json.Read<SavedSkin>(record);if(current==null||current.Id!=skin.Id)throw new InvalidDataException(UiText.T("无效的皮肤编号。"));return current;}
  public void Rename(SavedSkin skin,string name){name=(name??"").Trim();if(name.Length<1||name.Length>80||Regex.IsMatch(name,@"[\p{Cc}\p{Cf}]"))throw new InvalidDataException(UiText.T("皮肤名称需为 1–80 个字符，不能包含控制字符。"));var current=Existing(skin);current.Name=name;Json.Write(Path.ChangeExtension(ImagePath(current),".json"),current);}
  public void SetFavorite(SavedSkin skin,bool favorite){var current=Existing(skin);current.Favorite=favorite;Json.Write(Path.ChangeExtension(ImagePath(current),".json"),current);}
  public void Recycle(SavedSkin skin){Recycle(skin,path=>FileSystem.DeleteDirectory(path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException));}
  internal void Recycle(SavedSkin skin,Action<string> recycle){
   var current=Existing(skin);string image=ImagePath(current),record=Path.ChangeExtension(image,".json");
   for(var dir=new DirectoryInfo(Folder);dir!=null;dir=dir.Parent)if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(UiText.T("皮肤目录含有文件链接，无法安全删除。"));
   foreach(string file in new[]{image,record})if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException(UiText.T("皮肤目录含有文件链接，无法安全删除。"));
   // Recycle the PNG and its metadata as one recoverable folder on the same volume.
   string staging=Path.Combine(Folder,".recycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
   try{File.Move(image,Path.Combine(staging,Path.GetFileName(image)));File.Move(record,Path.Combine(staging,Path.GetFileName(record)));recycle(staging);}
   catch{try{foreach(string file in new[]{image,record}){string moved=Path.Combine(staging,Path.GetFileName(file));if(File.Exists(moved)&&!File.Exists(file))File.Move(moved,file);}if(Directory.Exists(staging)&&!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);}catch(Exception restore){throw new IOException(UiText.T("删除未完成，皮肤文件保留在：")+staging,restore);}throw;}
  }
  public static BitmapSource Decode(byte[] bytes){
   byte[] magic={137,80,78,71,13,10,26,10};if(bytes==null||bytes.Length<33||bytes.Length>MaxBytes||!bytes.Take(8).SequenceEqual(magic))throw new InvalidDataException(UiText.T("请选择 Minecraft 皮肤 PNG（64×64 或 64×32，1 MB 以内）。"));
   if(bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82||bytes[16]!=0||bytes[17]!=0||bytes[18]!=0||bytes[19]!=64||bytes[20]!=0||bytes[21]!=0||bytes[22]!=0||(bytes[23]!=32&&bytes[23]!=64))throw new InvalidDataException(UiText.T("请选择 Minecraft 皮肤 PNG（64×64 或 64×32，1 MB 以内）。"));
   try{using(var stream=new MemoryStream(bytes)){var frame=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);if(frame.PixelWidth!=64||(frame.PixelHeight!=64&&frame.PixelHeight!=32))throw new InvalidDataException();var converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);converted.Freeze();return converted;}}
   catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new InvalidDataException(UiText.T("请选择 Minecraft 皮肤 PNG（64×64 或 64×32，1 MB 以内）。"),ex);}
  }
  static string SafeName(string name){name=Regex.Replace(name??"",@"[\p{Cc}\p{Cf}]","").Trim();if(name.Length>80)name=name.Substring(0,80);return String.IsNullOrWhiteSpace(name)?UiText.T("未命名皮肤"):name;}
  public SavedSkin ImportFile(string path){var info=new FileInfo(path);if(info.Length>MaxBytes)throw new InvalidDataException(UiText.T("皮肤文件超过 1 MB。"));return Import(File.ReadAllBytes(path),Path.GetFileNameWithoutExtension(path),"");}
  public SavedSkin Import(byte[] bytes,string name,string source){
   var image=Decode(bytes);string id;using(var hash=SHA256.Create())id=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
   Directory.CreateDirectory(Folder);string file=Path.Combine(Folder,id+".png"),record=Path.Combine(Folder,id+".json");
   if(File.Exists(record)&&File.Exists(file)){var existing=Json.Read<SavedSkin>(record);if(existing.Id==id)return existing;}
   byte[] pixels=new byte[64*image.PixelHeight*4];image.CopyPixels(pixels,64*4,0);bool slim=image.PixelHeight==64&&pixels[(20*64+54)*4+3]==0&&pixels[(52*64+46)*4+3]==0;
   Uri uri;string origin=Uri.TryCreate(source,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&String.IsNullOrEmpty(uri.UserInfo)?uri.GetLeftPart(UriPartial.Path):"";
   var skin=new SavedSkin{Id=id,Name=SafeName(name),Source=origin,Added=DateTime.UtcNow.ToString("o"),Height=image.PixelHeight,Slim=slim};
   string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);Json.Write(record,skin);}finally{if(File.Exists(temp))File.Delete(temp);}return skin;
  }
  public BitmapSource Image(SavedSkin skin){return Decode(File.ReadAllBytes(ImagePath(skin)));}
  public void Export(SavedSkin skin,string target){byte[] bytes=File.ReadAllBytes(ImagePath(skin));Decode(bytes);if(Path.GetFullPath(target).Equals(ImagePath(skin),StringComparison.OrdinalIgnoreCase))return;string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
 public static class SkinPreview {
  public static BitmapSource Render(BitmapSource texture,bool slim,bool back){
   var visual=new DrawingVisual();int arm=slim?3:4;bool modern=texture.PixelHeight==64;
   using(var dc=visual.RenderOpen()){
    // Draw the actual atlas pieces with nearest-neighbor sampling, then the clothing layer.
    Action<int,int,int,int,int,int,bool> part=(sx,sy,w,h,x,y,mirror)=>{var crop=new CroppedBitmap(texture,new Int32Rect(sx,sy,w,h));crop.Freeze();var img=new ImageDrawing(crop,new Rect(x,y,w,h));if(mirror){dc.PushTransform(new ScaleTransform(-1,1,x+w/2.0,0));dc.DrawDrawing(img);dc.Pop();}else dc.DrawDrawing(img);};
    int head=back?24:8,body=back?32:20,rightArm=back?(slim?51:52):44,rightLeg=back?12:4;
    part(head,8,8,8,8,0,false);part(body,20,8,12,8,8,false);part(rightArm,20,arm,12,8-arm,8,false);part(rightLeg,20,4,12,8,20,false);
    if(modern){part(back?(slim?43:44):36,52,arm,12,16,8,false);part(back?28:20,52,4,12,12,20,false);}else{part(rightArm,20,4,12,16,8,true);part(rightLeg,20,4,12,12,20,true);}
    part(back?56:40,8,8,8,8,0,false);
    if(modern){part(body,36,8,12,8,8,false);part(rightArm,36,arm,12,8-arm,8,false);part(back?(slim?59:60):52,52,arm,12,16,8,false);part(rightLeg,36,4,12,8,20,false);part(back?12:4,52,4,12,12,20,false);}
   }
   RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);var bitmap=new RenderTargetBitmap(24,32,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
  }
 }
}
