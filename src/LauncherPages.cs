using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
namespace FoolLauncher {
 public sealed class LauncherPages {
  readonly Window Window;readonly UiPreferences Preferences;readonly Action LanguageChanged;readonly SkinLibraryPage Skins;readonly CatalogPage Downloads;readonly JavaPage Java;string Page="library";bool Loading;
  T Get<T>(string name)where T:class{return Window.FindName(name) as T;}
  public LauncherPages(Window window,string root,Action languageChanged,AccountStore accounts=null,Func<Func<System.Threading.CancellationToken,System.Threading.Tasks.Task>,System.Threading.Tasks.Task> skinRunner=null,Func<bool> busy=null,PackService service=null,Func<Instance> selected=null,Func<Func<IProgress<Update>,System.Threading.CancellationToken,System.Threading.Tasks.Task>,System.Threading.Tasks.Task> runner=null,Action<Instance> imported=null,Func<bool> engineRunning=null,Action<string> log=null){
   Window=window;Preferences=new UiPreferences(root);LanguageChanged=languageChanged;Skins=new SkinLibraryPage(window,root,accounts,skinRunner,busy);Downloads=new CatalogPage(window,service??new PackService(root),selected,busy,engineRunning,runner,imported,log);
   Java=new JavaPage(window,root,selected,busy,runner);Get<Button>("JavaNav").Click+=(s,e)=>Navigate("java");
   Get<Button>("ModDownloadsNav").Click+=(s,e)=>Navigate("mods");Get<Button>("PackDownloadsNav").Click+=(s,e)=>Navigate("packs");Get<Button>("DownloadsBack").Click+=(s,e)=>Navigate("library");
   Get<Button>("LibraryNav").Click+=(s,e)=>Navigate("library");Get<Button>("SkinsNav").Click+=(s,e)=>Navigate("skins");Get<Button>("SettingsNav").Click+=(s,e)=>Navigate("settings");
   Loading=true;Get<ComboBox>("LanguageBox").SelectedIndex=Preferences.Settings.Language=="en-US"?1:0;Get<Slider>("BackgroundShade").Value=Preferences.Settings.Shade;Loading=false;
   Get<ComboBox>("LanguageBox").SelectionChanged+=(s,e)=>{if(Loading)return;Change(()=>Preferences.Settings.Language=Get<ComboBox>("LanguageBox").SelectedIndex==1?"en-US":"zh-CN",true);};
   Get<Slider>("BackgroundShade").ValueChanged+=(s,e)=>{if(Loading)return;Change(()=>Preferences.Settings.Shade=e.NewValue,false);};
   Get<Button>("ChooseBackground").Click+=(s,e)=>{var picker=new OpenFileDialog{Title=UiText.T("选择启动器背景"),Filter=UiText.T("图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif"),CheckFileExists=true};if(picker.ShowDialog(Window)==true)Change(()=>Preferences.SetBackground(picker.FileName),false);};
   Get<Button>("ResetBackground").Click+=(s,e)=>Change(()=>Preferences.Settings.Background="",false);
   Apply();
  }
  void Change(Action change,bool language){var previous=Json.Parse<UiSettings>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Preferences.Settings));try{change();Preferences.Save();Apply();if(language)LanguageChanged();Get<TextBlock>("SettingsStatus").Text=UiText.T("设置已保存。");}catch(Exception ex){Preferences.Settings.Language=previous.Language;Preferences.Settings.Theme=previous.Theme;Preferences.Settings.Background=previous.Background;Preferences.Settings.Shade=previous.Shade;Apply();Loading=true;Get<ComboBox>("LanguageBox").SelectedIndex=previous.Language=="en-US"?1:0;Get<Slider>("BackgroundShade").Value=previous.Shade;Loading=false;Get<TextBlock>("SettingsStatus").Text=UiText.T("设置未能保存：")+ex.Message;}}
  void Apply(){
   Preferences.Apply(Window);var image=Preferences.BackgroundImage();Get<Image>("LauncherBackground").Source=image;var veil=Get<Border>("BackgroundVeil");veil.Visibility=image==null?Visibility.Collapsed:Visibility.Visible;veil.Opacity=Preferences.Settings.Shade;
   Get<TextBlock>("BackgroundStatus").Text=image==null?UiText.T("当前：默认纯色背景"):UiText.T("当前：自定义图片（已保存在启动器中）");Get<Slider>("BackgroundShade").IsEnabled=image!=null;Get<Button>("ResetBackground").IsEnabled=image!=null||Preferences.Settings.Background!="";
   var choices=Get<WrapPanel>("ThemeChoices");choices.Children.Clear();
   foreach(var palette in UiPreferences.Themes){var p=palette;var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new Ellipse{Width=17,Height=17,Fill=(Brush)new BrushConverter().ConvertFromString(p.Id=="white"||p.Id=="black"?p.Base:p.Accent),Margin=new Thickness(0,0,8,0),Stroke=Brushes.Gray,StrokeThickness=0.5});row.Children.Add(new TextBlock{Text=UiText.T(p.Name)+(Preferences.Settings.Theme==p.Id?"  ✓":""),VerticalAlignment=VerticalAlignment.Center});var b=new Button{Content=row,Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,10,10),MinWidth=115,ToolTip=UiText.T(p.Name)};b.SetResourceReference(Control.BackgroundProperty,Preferences.Settings.Theme==p.Id?"Brush.Selection":"Brush.Button");b.Click+=(s,e)=>Change(()=>Preferences.Settings.Theme=p.Id,false);choices.Children.Add(b);}
   Skins.RefreshLanguage();Downloads.RefreshLanguage();Java.RefreshLanguage();Navigate(Page);
  }
  public void RefreshAccount(){Skins.AccountChanged();}
  public void Navigate(string page){if(page!="library"&&page!="mods"&&page!="packs"&&page!="skins"&&page!="settings"&&page!="java")page="library";Page=page;
   bool download=page=="mods"||page=="packs";Get<FrameworkElement>("DownloadsPage").Visibility=download?Visibility.Visible:Visibility.Collapsed;
   Downloads.Show(page=="mods"?"mod":"modpack",download);
   Java.Show(page=="java");Get<FrameworkElement>("JavaPage").Visibility=page=="java"?Visibility.Visible:Visibility.Collapsed;Skins.SetVisible(page=="skins");Get<FrameworkElement>("LibraryPage").Visibility=page=="library"?Visibility.Visible:Visibility.Collapsed;Get<FrameworkElement>("SkinsPage").Visibility=page=="skins"?Visibility.Visible:Visibility.Collapsed;Get<FrameworkElement>("SettingsPage").Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;foreach(var pair in new[]{new[]{"LibraryNav","library"},new[]{"ModDownloadsNav","mods"},new[]{"PackDownloadsNav","packs"},new[]{"SkinsNav","skins"},new[]{"JavaNav","java"},new[]{"SettingsNav","settings"}}){var button=Get<Button>(pair[0]);if(pair[1]==page){button.SetResourceReference(Control.BackgroundProperty,"Brush.Selection");button.SetResourceReference(Control.ForegroundProperty,"Brush.Accent");}else{button.Background=Brushes.Transparent;button.SetResourceReference(Control.ForegroundProperty,"Brush.Muted");}}}
 }
}
