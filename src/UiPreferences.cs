using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FoolLauncher {
 public class UiSettings {
  public string Language {get;set;} public string Theme {get;set;} public string Background {get;set;} public double Shade {get;set;}
  public UiSettings(){Language="zh-CN";Theme="green";Background="";Shade=0.45;}
 }
 public sealed class ThemePalette {
  public string Id,Name,Base,Panel,Hero,Log,Button,Selected,Text,Muted,Accent,AccentText;
  public ThemePalette(string id,string name,string basis,string panel,string hero,string log,string button,string selected,string text,string muted,string accent,string accentText){Id=id;Name=name;Base=basis;Panel=panel;Hero=hero;Log=log;Button=button;Selected=selected;Text=text;Muted=muted;Accent=accent;AccentText=accentText;}
 }
 public sealed class UiPreferences {
  public static readonly ThemePalette[] Themes={
   new ThemePalette("green","墨绿色","#10171E","#19232D","#1B2C34","#151F28","#263440","#2C4247","#EDF2F5","#ACBDC6","#75D5B7","#10251F"),
   new ThemePalette("blue","雾蓝色","#131A23","#1C2938","#24364A","#192433","#304259","#405973","#EDF2FA","#ACBDCF","#9EBBDB","#182B40"),
   new ThemePalette("red","灰玫红","#21191C","#30262B","#3C2D34","#2A2227","#4C3941","#654A56","#F8EFF2","#CCB5BF","#D5A1AE","#352028"),
   new ThemePalette("yellow","麦黄色","#201E18","#2D2B23","#39362B","#28261F","#484334","#615B42","#F7F4E9","#C9C2AA","#D7C58B","#302A17"),
   new ThemePalette("purple","烟紫色","#1C1923","#292536","#352E46","#242031","#443B55","#5B4E72","#F4EFF9","#C1B6D1","#BFA6D6","#2D203C"),
   new ThemePalette("black","黑色","#101012","#1C1C20","#26262B","#17171B","#34343B","#48484F","#F4F4F6","#BABAC5","#CCCCD4","#202026"),
   new ThemePalette("white","白色","#EFF1F4","#FFFFFF","#F6F8FA","#F9FAFB","#E4E9EF","#D6E4E2","#202D38","#506371","#3B786C","#FFFFFF")
  };
  readonly string Root,FilePath;public UiSettings Settings {get;private set;}
  public UiPreferences(string root){Root=Path.GetFullPath(root);FilePath=Path.Combine(Root,"data","zhixi-ui-settings.json");try{Settings=Json.Read<UiSettings>(FilePath)??new UiSettings();}catch{Settings=new UiSettings();}Normalize();}
  void Normalize(){if(Settings.Language!="en-US")Settings.Language="zh-CN";if(!Themes.Any(p=>p.Id==Settings.Theme))Settings.Theme="green";if(Double.IsNaN(Settings.Shade)||Double.IsInfinity(Settings.Shade)||Settings.Shade<0.15||Settings.Shade>0.85)Settings.Shade=0.45;if(String.IsNullOrWhiteSpace(Settings.Background)||Path.GetFileName(Settings.Background)!=Settings.Background||!Settings.Background.StartsWith("background-",StringComparison.Ordinal)||!Settings.Background.EndsWith(".png",StringComparison.OrdinalIgnoreCase))Settings.Background="";}
  public void Save(){Normalize();Json.Write(FilePath,Settings);}
  public string BackgroundPath {get{return Settings.Background==""?null:Path.Combine(Root,"data","appearance",Settings.Background);}}
  public BitmapSource BackgroundImage(){try{string path=BackgroundPath;if(path==null||!File.Exists(path))return null;return Decode(File.ReadAllBytes(path));}catch{return null;}}
  internal static BitmapSource Decode(byte[] bytes){
   if(bytes.Length==0||bytes.Length>32*1024*1024)throw new InvalidDataException(UiText.T("背景图片大小需在 32 MB 以内。"));
   try{int w,h;using(var s=new MemoryStream(bytes)){var f=BitmapFrame.Create(s,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);w=f.PixelWidth;h=f.PixelHeight;if(w<1||h<1||w>20000||h>20000||(long)w*h>64000000)throw new InvalidDataException();}
    using(var s=new MemoryStream(bytes)){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=s;if(w>=h)b.DecodePixelWidth=Math.Min(2560,w);else b.DecodePixelHeight=Math.Min(2560,h);b.EndInit();b.Freeze();return b;}
   }catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new InvalidDataException(UiText.T("无法读取背景图片，请选择 PNG、JPG、BMP 或 GIF。"),ex);}
  }
  public void SetBackground(string source){
   var info=new FileInfo(source);if(info.Length>32*1024*1024)throw new InvalidDataException(UiText.T("背景图片大小需在 32 MB 以内。"));var image=Decode(File.ReadAllBytes(source));
   string name="background-"+Guid.NewGuid().ToString("N")+".png",folder=Path.Combine(Root,"data","appearance"),path=Path.Combine(folder,name);Directory.CreateDirectory(folder);
   string old=Settings.Background;try{var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))png.Save(output);Settings.Background=name;Save();}
   catch{Settings.Background=old;if(File.Exists(path))File.Delete(path);throw;}
  }
  public void Apply(Window window){
   UiText.Language=Settings.Language;UiText.Apply(window);var p=Themes.First(t=>t.Id==Settings.Theme);
   var colors=new Dictionary<string,string>{{"Base",p.Base},{"Panel",p.Panel},{"Hero",p.Hero},{"Log",p.Log},{"Button",p.Button},{"Badge",p.Selected},{"Selection",p.Selected},{"Icon",p.Button},{"Editor",p.Log},{"Text",p.Text},{"Muted",p.Muted},{"Accent",p.Accent},{"AccentText",p.AccentText},{"Danger",p.Id=="white"?"#B92E41":"#EF8B8B"},{"Error",p.Id=="white"?"#AA3045":"#FFA49B"}};
   foreach(var pair in colors){var color=(Color)ColorConverter.ConvertFromString(pair.Value);if(BackgroundPath!=null&&File.Exists(BackgroundPath)&&(pair.Key=="Panel"||pair.Key=="Hero"||pair.Key=="Log"))color.A=232;var brush=new SolidColorBrush(color);brush.Freeze();window.Resources["Brush."+pair.Key]=brush;if(Application.Current!=null)Application.Current.Resources["Brush."+pair.Key]=brush;}
  }
 }
}
