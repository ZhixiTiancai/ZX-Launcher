using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FoolLauncher {
 public sealed class LauncherUpdateSettings {public string FeedUrl{get;set;}public bool AutoCheck{get;set;}public const string DefaultFeed="https://github.com/ZhixiTiancai/ZX-Launcher/releases/latest/download/update.json";public LauncherUpdateSettings(){FeedUrl=DefaultFeed;AutoCheck=true;}}
 public sealed class LauncherUpdateUi {
  readonly Window Window;readonly string Root,SettingsFile;readonly Func<bool> Ready;readonly Action<string> Log;readonly Action ShowSettings;
  LauncherUpdateSettings Settings;UpdateManifest Feed;string Package;VerifiedUpdate Candidate;bool Working,Loading;
  T Get<T>(string name)where T:class{return Window.FindName(name) as T;}
  public LauncherUpdateUi(Window window,string root,Func<bool> ready,Action<string> log,Action showSettings) {
   Window=window;Root=root;Ready=ready;Log=log;ShowSettings=showSettings;SettingsFile=Path.Combine(root,"data","launcher-update-settings.json");
   try{Settings=Json.Read<LauncherUpdateSettings>(SettingsFile)??new LauncherUpdateSettings();if(String.IsNullOrWhiteSpace(Settings.FeedUrl)&&Settings.AutoCheck)Settings.FeedUrl=LauncherUpdateSettings.DefaultFeed;if(!String.IsNullOrWhiteSpace(Settings.FeedUrl))LauncherUpdateCore.Https(Settings.FeedUrl);}catch{Settings=new LauncherUpdateSettings();}
   Loading=true;Get<TextBox>("UpdateSource").Text=Settings.FeedUrl;Get<CheckBox>("AutoCheckUpdates").IsChecked=Settings.AutoCheck;Loading=false;
   Status(Settings.FeedUrl==""?"尚未配置发布地址，可先使用本地更新包。":"启动时会按设置检查更新。");
   Get<Button>("SaveUpdateSource").Click+=(s,e)=>{try{string url=Get<TextBox>("UpdateSource").Text.Trim();if(url!="")LauncherUpdateCore.Https(url);Settings.FeedUrl=url;Save();Feed=null;Package=null;Candidate=null;Get<Button>("InstallUpdate").Visibility=Visibility.Collapsed;Get<Button>("UpdateAvailableButton").Visibility=Visibility.Collapsed;Status(url==""?"尚未配置发布地址，可先使用本地更新包。":"更新地址已保存。");}catch(Exception ex){Status(ex.Message);}};
   Get<CheckBox>("AutoCheckUpdates").Checked+=(s,e)=>SaveAuto();Get<CheckBox>("AutoCheckUpdates").Unchecked+=(s,e)=>SaveAuto();
   Get<Button>("CheckUpdates").Click+=async(s,e)=>await Check(false);
   Get<Button>("LocalUpdate").Click+=async(s,e)=>await Local();
   Get<Button>("InstallUpdate").Click+=async(s,e)=>await Install();
   Get<Button>("UpdateAvailableButton").Click+=(s,e)=>ShowSettings();
   Window.Loaded+=async(s,e)=>{if(Settings.AutoCheck&&!String.IsNullOrEmpty(Settings.FeedUrl)){await Task.Delay(1500);await Check(true);}};
  }
  void Save(){Json.Write(SettingsFile,Settings);}
  void SaveAuto(){if(Loading)return;try{Settings.AutoCheck=Get<CheckBox>("AutoCheckUpdates").IsChecked==true;Save();}catch(Exception ex){Status(ex.Message);}}
  void Status(string text){Get<TextBlock>("UpdateStatus").Text=UiText.T(text);}
  void Controls(bool enabled){Working=!enabled;foreach(string name in new[]{"CheckUpdates","LocalUpdate","InstallUpdate","SaveUpdateSource"})Get<Button>(name).IsEnabled=enabled;}
  static string Cache(){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhixiLauncher","updates",Guid.NewGuid().ToString("N"));LauncherUpdateCore.NoLinks(folder);Directory.CreateDirectory(folder);return folder;}
  static async Task Download(string url,string target,long max,int seconds) {
   using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
   using(var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false})
   using(var http=new HttpClient(handler)) {
    http.Timeout=TimeSpan.FromSeconds(seconds);http.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher-Updater/1.0");Uri uri=LauncherUpdateCore.Https(url);
    for(int attempt=0;attempt<6;attempt++)using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancel.Token)) {
     if((int)response.StatusCode>=300&&(int)response.StatusCode<400){if(response.Headers.Location==null)throw new IOException("更新地址重定向无效。");uri=LauncherUpdateCore.Https(new Uri(uri,response.Headers.Location).AbsoluteUri);continue;}
     response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>max)throw new IOException("更新文件过大。");
     using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)) {
      byte[] buffer=new byte[65536];long total=0;int read;while((read=await input.ReadAsync(buffer,0,buffer.Length,cancel.Token))>0){total+=read;if(total>max)throw new IOException("更新文件超出大小限制。");await output.WriteAsync(buffer,0,read,cancel.Token);}
     }return;
    }
    throw new IOException("更新地址重定向次数过多。");
   }
  }
  async Task Check(bool automatic) {
   if(Working)return;if(Settings.FeedUrl==""){Status("尚未配置发布地址，可先使用本地更新包。");return;}
   Controls(false);Status("正在检查启动器更新…");
   try {
    string record=Path.Combine(Cache(),"feed.json");await Download(Settings.FeedUrl,record,256*1024,20);
    var feed=LauncherUpdateCore.ReadSigned(File.ReadAllBytes(record),"feed");
    if(LauncherUpdateCore.VersionOf(feed.Version)<=CurrentVersion()){Status("当前已是最新版本。");return;}
    Feed=feed;Package=null;Candidate=null;Offer(feed.Version,feed.Notes);Log(String.Format(UiText.T("发现启动器新版本 {0}，可在设置中安装。"),feed.Version));
   }catch(Exception ex){Status(UiText.T("检查更新未完成：")+ex.Message);if(!automatic)Log(Get<TextBlock>("UpdateStatus").Text);}
   finally{Controls(true);}
  }
  Version CurrentVersion(){var v=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;return new Version(v.Major,v.Minor,v.Build);}
  void Offer(string version,string notes){Get<TextBlock>("UpdateNotes").Text=version+"\n"+(notes??"");Get<Button>("InstallUpdate").Visibility=Visibility.Visible;Get<Button>("UpdateAvailableButton").Content=String.Format(UiText.T(LauncherUpdateCore.VersionOf(version)>CurrentVersion()?"发现新版本 {0}":"待安装版本 {0}"),version);Get<Button>("UpdateAvailableButton").Visibility=Visibility.Visible;Status("已验证更新签名。安装只替换程序文件，保留游戏数据。");}
  async Task Local() {
   if(Working)return;var picker=new OpenFileDialog{Title=UiText.T("选择启动器更新包"),Filter=UiText.T("签名的启动器更新包|*.zip")};if(picker.ShowDialog(Window)!=true)return;
   Controls(false);Feed=null;Package=null;Candidate=null;Get<Button>("InstallUpdate").Visibility=Visibility.Collapsed;Status("正在验证本地更新包…");
   try {
    string cache=Cache(),copy=Path.Combine(cache,"package.zip");if(new FileInfo(picker.FileName).Length>LauncherUpdateCore.MaxPackage)throw new IOException("更新文件过大。");File.Copy(picker.FileName,copy);
    var staged=await Task.Run(()=>LauncherUpdateCore.Stage(copy,Path.Combine(cache,"verified"),CancellationToken.None));
    if(LauncherUpdateCore.VersionOf(staged.Version)<CurrentVersion())throw new IOException(UiText.T("此更新包版本较旧，已阻止降级。"));
    Feed=null;Package=copy;Candidate=staged;Offer(staged.Version,staged.Notes);
   }catch(Exception ex){Status(UiText.T("更新包未能载入：")+ex.Message);}
   finally{Controls(true);}
  }
  async Task Install() {
   if(Working)return;if(!Ready()){Status("请先等待当前任务完成，再安装启动器更新。");return;}
   Controls(false);
   try {
    if(Package==null) {
     if(Feed==null)return;Status("正在下载启动器更新…");string cache=Cache(),download=Path.Combine(cache,"package.zip");await Download(Feed.Url,download,Feed.Size,180);
     if(new FileInfo(download).Length!=Feed.Size||LauncherUpdateCore.HashFile(download)!=Feed.Sha256)throw new IOException("更新下载校验失败。");
     var staged=await Task.Run(()=>LauncherUpdateCore.Stage(download,Path.Combine(cache,"verified"),CancellationToken.None));if(staged.Version!=Feed.Version)throw new IOException("更新版本与发布信息不一致。");Package=download;Candidate=staged;
    }
    if(!Ready()){Status("请先等待当前任务完成，再安装启动器更新。");return;}
    string install=PortableLayout.InstallRoot(Root);
    string session=Cache(),worker=Path.Combine(session,"updater.exe"),package=Path.Combine(session,"package.zip");File.Copy(PortableLayout.UpdateTarget(install,"智熄更新器.exe"),worker);File.Copy(Package,package);
    Process.Start(new ProcessStartInfo(worker,"--install "+Quote(package)+" "+Quote(install)){UseShellExecute=false,WorkingDirectory=session});
    Application.Current.Shutdown();
   }catch(Exception ex){Status(UiText.T("更新未能开始：")+ex.Message);}
   finally{Controls(true);}
  }
  static string Quote(string value){if(value.IndexOf('"')>=0)throw new IOException("Invalid path.");return "\""+value.TrimEnd('\\')+"\"";}
 }
}
