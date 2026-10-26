using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
namespace FoolLauncher {
 public static class AppearanceTests {
  public static void Run(string root){
   string folder=Path.Combine(root,"appearance-check");Directory.CreateDirectory(folder);var prefs=new UiPreferences(folder);
   if(prefs.Settings.Theme!="green"||prefs.Settings.Language!="zh-CN"||UiPreferences.Themes.Length!=7)throw new Exception("Appearance defaults/palette count failed");
   prefs.Settings.Theme="white";prefs.Settings.Language="en-US";prefs.Settings.Shade=0.65;prefs.Save();var reload=new UiPreferences(folder);if(reload.Settings.Theme!="white"||reload.Settings.Language!="en-US"||reload.Settings.Shade!=0.65)throw new Exception("Appearance persistence failed");
   string source=Path.Combine(folder,"source.png");using(var s=typeof(PackIcons).Assembly.GetManifestResourceStream("FoolLauncher.DefaultGrass.png"))using(var f=File.Create(source))s.CopyTo(f);prefs.SetBackground(source);string background=prefs.BackgroundPath;File.Delete(source);
   if(!File.Exists(background)||prefs.BackgroundImage()==null||new UiPreferences(folder).BackgroundImage()==null)throw new Exception("Copied background persistence failed");
   bool invalid=false;try{UiPreferences.Decode(new byte[]{1,2,3});}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Invalid background accepted");
   prefs.Settings.Background="..\\accounts.json";prefs.Settings.Theme="unknown";prefs.Settings.Language="bad";prefs.Settings.Shade=Double.NaN;prefs.Save();if(prefs.BackgroundPath!=null||prefs.Settings.Theme!="green"||prefs.Settings.Language!="zh-CN"||prefs.Settings.Shade!=0.45)throw new Exception("Appearance validation failed");
   UiText.Language="en-US";if(UiText.T("设置")!="Settings"||UiText.T("白色")!="White")throw new Exception("English localization failed");
   string markup=Path.Combine(Environment.CurrentDirectory,"src","App.xaml");
   if(File.Exists(markup))using(var input=File.OpenRead(markup)){
    var w=(Window)XamlReader.Load(input);var pages=new LauncherPages(w,folder,()=>{});
    pages.Navigate("settings");if(((FrameworkElement)w.FindName("SettingsPage")).Visibility!=Visibility.Visible||((FrameworkElement)w.FindName("LibraryPage")).Visibility!=Visibility.Collapsed)throw new Exception("Settings navigation failed");
    ((ComboBox)w.FindName("LanguageBox")).SelectedIndex=1;if(UiText.Language!="en-US"||w.Title!="Zhixi Launcher")throw new Exception("Live language resource update failed");
    foreach(string[] route in new[]{new[]{"ModDownloadsNav","Mod downloads"},new[]{"PackDownloadsNav","Modpack downloads"}}){((Button)w.FindName(route[0])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(((FrameworkElement)w.FindName("DownloadsPage")).Visibility!=Visibility.Visible||((TextBlock)w.FindName("DownloadsTitle")).Text!=route[1]||((FrameworkElement)w.FindName("LibraryPage")).Visibility!=Visibility.Collapsed||((FrameworkElement)w.FindName("SettingsPage")).Visibility!=Visibility.Collapsed)throw new Exception("Download entry navigation/localization failed");}
    ((Button)w.FindName("DownloadsBack")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(((FrameworkElement)w.FindName("DownloadsPage")).Visibility!=Visibility.Collapsed||((FrameworkElement)w.FindName("LibraryPage")).Visibility!=Visibility.Visible)throw new Exception("Download page back navigation failed");
    foreach(var theme in UiPreferences.Themes){var p=new UiPreferences(folder);p.Settings.Theme=theme.Id;p.Save();p.Apply(w);if(!w.Resources.Contains("Brush.Text")||!w.Resources.Contains("Brush.Danger"))throw new Exception("Theme resources missing");}
    pages.Navigate("skins");if(((FrameworkElement)w.FindName("SkinsPage")).Visibility!=Visibility.Visible)throw new Exception("Skins navigation failed");
    pages.Navigate("library");if(((FrameworkElement)w.FindName("PackDropZone")).Visibility!=Visibility.Visible)throw new Exception("Library/drop zone failed");w.Close();
   }
   UiText.Language="zh-CN";
   // Only this synthetic test instance is recycled; no user launcher data is accessed.
   var service=new PackService(Path.Combine(root,"recycle-check"));var instance=new Instance{Id="synthetic-delete-test",Name="Deletion test"};string pack=service.Folder(instance);Directory.CreateDirectory(Path.Combine(pack,"minecraft","saves","TestWorld"));Json.Write(Path.Combine(pack,"launcher-instance.json"),instance);File.WriteAllText(Path.Combine(pack,"minecraft","saves","TestWorld","level.dat"),"synthetic world");string sentinel=Path.Combine(service.Data,"accounts-sentinel.txt");File.WriteAllText(sentinel,"preserve");
   foreach(var id in new[]{"../outside","", "nested/pack"}){bool rejected=false;try{PackRemoval.CheckedFolder(service,new Instance{Id=id});}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Unsafe removal path accepted");}
   string session=Path.Combine(pack,"minecraft","saves","TestWorld","session.lock");File.WriteAllText(session,"test lock");using(var held=new FileStream(session,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){bool locked=false;try{PackRemoval.CheckedFolder(service,instance);}catch(IOException){locked=true;}if(!locked)throw new Exception("Active world deletion accepted");}
   PackRemoval.Recycle(service,instance);if(Directory.Exists(pack)||service.List().Any()||File.ReadAllText(sentinel)!="preserve")throw new Exception("Recycle scope failed");
   Console.WriteLine("PASS: seven themes, live bilingual resources, page navigation, appearance persistence/validation, copied backgrounds, invalid image rejection, guarded recycle of synthetic instance and sibling preservation.");
  }
 }
}
