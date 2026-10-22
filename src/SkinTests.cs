using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FoolLauncher {
 public static class SkinTests {
  internal static byte[] Fixture(int height){var bytes=new byte[64*height*4];Action<int,int,int,int,byte,byte,byte> fill=(x,y,w,h,r,g,b)=>{for(int j=y;j<y+h;j++)for(int i=x;i<x+w;i++){int n=(j*64+i)*4;bytes[n]=b;bytes[n+1]=g;bytes[n+2]=r;bytes[n+3]=255;}};fill(8,8,8,8,223,174,135);fill(24,8,8,8,83,56,39);fill(20,20,8,12,88,147,173);fill(32,20,8,12,64,107,142);fill(44,20,12,12,180,133,104);fill(4,20,12,12,68,76,114);if(height==64){fill(36,52,12,12,180,133,104);fill(20,52,12,12,68,76,114);}var bitmap=BitmapSource.Create(64,height,96,96,PixelFormats.Bgra32,null,bytes,64*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}}
  public static void Run(string root){var store=new SkinStore(Path.Combine(root,"skin-check"));byte[] modern=Fixture(64),legacy=Fixture(32);var skin=store.Import(modern,"Fixture","https://namemc.com/skin/0123456789abcdef?tracking=discard");var same=store.Import(modern,"Renamed duplicate","");var old=store.Import(legacy,"Legacy","");if(same.Id!=skin.Id||store.List().Count!=2||skin.Source.Contains("?"))throw new Exception("Skin deduplication/metadata failed");if(!File.ReadAllBytes(store.ImagePath(skin)).SequenceEqual(modern))throw new Exception("Skin bytes changed");
   foreach(var bytes in new[]{Encoding.UTF8.GetBytes("<html>not a skin</html>"),new byte[SkinStore.MaxBytes+1]}){bool invalid=false;try{SkinStore.Decode(bytes);}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Invalid/oversized skin accepted");}
   var front=SkinPreview.Render(store.Image(skin),false,false);var back=SkinPreview.Render(store.Image(skin),false,true);var pixels=new byte[24*32*4];front.CopyPixels(pixels,24*4,0);if(pixels[(2*24+10)*4+2]!=223||front.PixelWidth!=24||front.PixelHeight!=32)throw new Exception("Front skin atlas assembly failed");back.CopyPixels(pixels,24*4,0);if(pixels[(2*24+10)*4+2]!=83)throw new Exception("Back skin atlas assembly failed");SkinPreview.Render(store.Image(old),false,true);
   string export=Path.Combine(root,"skin-export.png");store.Export(skin,export);if(!File.ReadAllBytes(export).SequenceEqual(modern)||new SkinStore(Path.Combine(root,"skin-check")).List().Count!=2)throw new Exception("Skin export/reload failed");
   // A record written by 0.1.12 has no Favorite field and must load unchanged.
   string record=Path.ChangeExtension(store.ImagePath(skin),".json");Json.Write(record,new{Id=skin.Id,Name=skin.Name,Source=skin.Source,Added="2000-01-01T00:00:00Z",Height=skin.Height,Slim=skin.Slim});
   if(store.List().First(x=>x.Id==skin.Id).Favorite)throw new Exception("Legacy skin was unexpectedly favorited");
   store.SetFavorite(skin,true);store.Rename(skin,"  我的皮肤  ");var latest=new SkinStore(Path.Combine(root,"skin-check")).List();
   if(latest[0].Id!=skin.Id||!latest[0].Favorite||latest[0].Name!="我的皮肤"||latest[0].Source!=skin.Source||latest[0].Added!="2000-01-01T00:00:00Z")throw new Exception("Skin favorite/rename persistence or metadata preservation failed");
   if(store.Import(modern,"duplicate download","").Name!="我的皮肤"||!store.Import(modern,"duplicate download","").Favorite)throw new Exception("Duplicate skin overwrote custom metadata");
   store.SetFavorite(skin,false);if(store.List()[0].Id!=old.Id)throw new Exception("Unfavorite did not restore ordering");
   foreach(string badName in new[]{"  ","bad\nname",new string('a',81)}){bool denied=false;try{store.Rename(skin,badName);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Invalid skin rename accepted");}
   foreach(string badId in new[]{"../outside",new string('a',63),"C:/elsewhere"}){bool denied=false;try{store.Recycle(new SavedSkin{Id=badId});}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Unsafe skin deletion path accepted");}
   byte[] metadata=File.ReadAllBytes(record);bool cancelled=false;try{store.Recycle(skin,path=>{throw new OperationCanceledException("synthetic cancel");});}catch(OperationCanceledException){cancelled=true;}
   if(!cancelled||!File.ReadAllBytes(record).SequenceEqual(metadata)||!File.ReadAllBytes(store.ImagePath(skin)).SequenceEqual(modern)||Directory.EnumerateDirectories(store.Folder,".recycle-*").Any())throw new Exception("Cancelled skin deletion did not restore both files");
   store.Recycle(old);if(store.List().Count!=1||File.Exists(store.ImagePath(old))||File.Exists(Path.ChangeExtension(store.ImagePath(old),".json"))||!File.ReadAllBytes(record).SequenceEqual(metadata)||!File.ReadAllBytes(store.ImagePath(skin)).SequenceEqual(modern))throw new Exception("Skin recycling changed another skin or left its record");
   Console.WriteLine("PASS: legacy skin metadata, rename/favorite ordering and persistence, deduplication preserving custom names/stars, invalid rename/path rejection, cancelled-delete rollback and recycling only a synthetic skin pair.");
   foreach(string url in new[]{"http://namemc.com/skin/a","https://namemc.com.attacker.test/skin/a","file:///C:/Windows/win.ini","https://user:pass@namemc.com","javascript:alert(1)"})if(SkinBrowser.IsPage(url)||SkinBrowser.IsSkinImage(url))throw new Exception("Unsafe skin site accepted");
   if(!SkinBrowser.IsPage("https://namemc.com/minecraft-skins")||!SkinBrowser.IsPage("https://zh.namemc.com/skin/0123456789abcdef")||!SkinBrowser.IsSkinImage("https://s.namemc.com/i/0123456789abcdef.png")||SkinBrowser.IsSkinImage("https://s.namemc.com/i/../payload.exe"))throw new Exception("Skin navigation/download URL checks failed");
   if(!SkinBrowser.IsDownload("blob:https://namemc.com/fixture", "https://namemc.com/skin/0123456789abcdef")||SkinBrowser.IsDownload("blob:https://evil.test/fixture","https://namemc.com/skin/0123456789abcdef")||SkinBrowser.IsDownload("blob:https://namemc.com/fixture","https://evil.test/"))throw new Exception("Browser blob origin checks failed");
   bool bounded=false;try{SkinBrowser.ReadBounded(new MemoryStream(new byte[SkinStore.MaxBytes+1])).GetAwaiter().GetResult();}catch(InvalidDataException){bounded=true;}if(!bounded)throw new Exception("Skin response size limit failed");
   Console.WriteLine("PASS: local skin PNG import/deduplication/reload/export, modern and legacy atlas previews, hostile content and URL rejection, bounded browser-response handling.");
  }
 }
}
