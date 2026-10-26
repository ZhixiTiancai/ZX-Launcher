using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
namespace FoolLauncher {
 public sealed class CatalogPage {
  readonly Window Window;readonly PackService Service;readonly CatalogService Api;readonly CatalogInstaller Installer;readonly Func<Instance> Selected;readonly Func<bool> Busy,EngineRunning;
  readonly Func<Func<IProgress<Update>,CancellationToken,Task>,Task> Runner;readonly Action<Instance> Imported;readonly Action<string> Log;
  string Kind="",TargetId="";bool Visible,Loading,Working,More;int Page,VersionOffset;CancellationTokenSource SearchCancel,VersionCancel,InstallCancel;
  CatalogProject Project;readonly List<CatalogVersion> Versions=new List<CatalogVersion>();ModTarget Target;
  T Get<T>(string name)where T:class{return Window.FindName(name) as T;}
  public CatalogPage(Window window,PackService service,Func<Instance> selected,Func<bool> busy,Func<bool> engineRunning,Func<Func<IProgress<Update>,CancellationToken,Task>,Task> runner,Action<Instance> imported,Action<string> log){
   Window=window;Service=service;Api=new CatalogService();Selected=selected??(()=>null);Busy=busy??(()=>false);EngineRunning=engineRunning??(()=>false);Installer=new CatalogInstaller(service,Api,CatalogNetwork.Download,EngineRunning);Runner=runner??(action=>action(new Quiet(),CancellationToken.None));Imported=imported??(i=>{});Log=log??(s=>{});
   Loading=true;Fill("CatalogSource",new[]{"全部","Modrinth","CurseForge"},new[]{"all","modrinth","curseforge"});Fill("CatalogLoader",new[]{"全部","Forge","Fabric","NeoForge","Quilt"},new[]{"","forge","fabric","neoforge","quilt"});SetCategories(false);
   var game=Get<ComboBox>("CatalogGame");foreach(string version in new[]{"", "1.21.11","1.21.10","1.21.8","1.21.5","1.21.4","1.21.1","1.21","1.20.6","1.20.4","1.20.1","1.19.2","1.18.2","1.16.5","1.12.2"})game.Items.Add(version);game.SelectedIndex=0;Loading=false;
   Get<Button>("CatalogSearch").Click+=async(s,e)=>await Search(false);Get<TextBox>("CatalogName").KeyDown+=async(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await Search(false);}};
   Get<Button>("CatalogReset").Click+=async(s,e)=>{if(Working)return;Get<TextBox>("CatalogName").Clear();Get<ComboBox>("CatalogSource").SelectedIndex=0;Get<ComboBox>("CatalogCategory").SelectedIndex=0;if(Kind=="modpack"){Get<ComboBox>("CatalogGame").Text="";Get<ComboBox>("CatalogLoader").SelectedIndex=0;}await Search(false);};
   Get<Button>("CatalogPrev").Click+=async(s,e)=>{if(Page>0){Page--;await Search(true);}};Get<Button>("CatalogNext").Click+=async(s,e)=>{if(More){Page++;await Search(true);}};
   Get<ListBox>("CatalogResults").SelectionChanged+=async(s,e)=>{if(Loading)return;Project=Get<ListBox>("CatalogResults").SelectedItem as CatalogProject;await ShowProject(false);};
   Get<Button>("CatalogMoreVersions").Click+=async(s,e)=>await ShowProject(true);
   Get<ComboBox>("CatalogVersions").SelectionChanged+=(s,e)=>VersionChanged();Get<Button>("CatalogInstall").Click+=async(s,e)=>await Install();
   Get<Button>("CatalogWebsite").Click+=(s,e)=>{try{if(Project==null)return;Uri u=CatalogNetwork.Https(Project.Website);if(u.Host!="modrinth.com"&&u.Host!="www.curseforge.com")throw new IOException("项目网页无效。");Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){Status(ex.Message);}};
   Get<Button>("CatalogCancel").Click+=(s,e)=>{if(InstallCancel!=null){InstallCancel.Cancel();Status(UiText.T("正在停止任务…"));}};
   Window.Closed+=(s,e)=>{if(SearchCancel!=null)SearchCancel.Cancel();if(VersionCancel!=null)VersionCancel.Cancel();if(InstallCancel!=null)InstallCancel.Cancel();};
  }
  sealed class Quiet:IProgress<Update>{public void Report(Update u){}}
  void Fill(string name,string[] labels,string[] values){var box=Get<ComboBox>(name);box.Items.Clear();for(int n=0;n<labels.Length;n++)box.Items.Add(new ComboBoxItem{Content=UiText.T(labels[n]),Tag=values[n]});box.SelectedIndex=0;}
  void SetCategories(bool pack){if(pack)Fill("CatalogCategory",new[]{"全部","冒险","科技","魔法","任务","轻量","大型综合","硬核","多人"},new[]{"","adventure","technology","magic","quests","lightweight","kitchen-sink","hardcore","multiplayer"});else Fill("CatalogCategory",new[]{"全部","性能优化","前置库","科技","魔法","世界生成","冒险","装饰"},new[]{"","optimization","library","technology","magic","worldgen","adventure","decoration"});}
  string Tag(string name){var item=Get<ComboBox>(name).SelectedItem as ComboBoxItem;return item==null?"":Convert.ToString(item.Tag);}
  void SetTag(string name,string value){foreach(ComboBoxItem item in Get<ComboBox>(name).Items)if(Convert.ToString(item.Tag)==value){Get<ComboBox>(name).SelectedItem=item;return;}Get<ComboBox>(name).SelectedIndex=0;}
  void Status(string text){Get<TextBlock>("CatalogStatus").Text=text;}
  public void Show(string kind,bool visible){
   bool changed=Kind!=kind;Kind=kind;Visible=visible;if(!visible){if(SearchCancel!=null)SearchCancel.Cancel();if(VersionCancel!=null)VersionCancel.Cancel();Get<Button>("CatalogSearch").IsEnabled=!Working;return;}
   Get<TextBlock>("DownloadsTitle").Text=UiText.T(kind=="mod"?"Mod 下载":"整合包下载");Get<Button>("CatalogInstall").Content=UiText.T(kind=="mod"?"下载并安装 Mod":"安装到整合包库");
   string previous=TargetId;UpdateTarget();
   if(changed){Loading=true;Get<TextBox>("CatalogName").Clear();SetCategories(kind=="modpack");Get<ComboBox>("CatalogLoader").SelectedIndex=0;Get<ComboBox>("CatalogGame").Text="";Loading=false;UpdateTarget();}
   RefreshLanguage();if((changed||previous!=TargetId||Get<ListBox>("CatalogResults").Items.Count==0)&&Window.IsLoaded&&!Working){var ignored=Search(false);}
  }
  void UpdateTarget(){
   Target=null;TargetId="";if(Kind=="mod")try{Target=ModTarget.Capture(Service,Selected());TargetId=Target.Id;}catch(Exception ex){Get<TextBlock>("CatalogTarget").Text=ex.Message;}
   if(Kind=="mod"&&Target!=null){Get<TextBlock>("CatalogTarget").Text=UiText.T("安装目标：")+Target.Name+"  ·  Minecraft "+Target.Game+" / "+Target.Loader+"\n"+System.IO.Path.Combine(Target.Folder,"minecraft","mods");Get<ComboBox>("CatalogGame").Text=Target.Game;SetTag("CatalogLoader",Target.Loader);}
   if(Kind=="modpack")Get<TextBlock>("CatalogTarget").Text=UiText.T("下载完成后自动导入整合包库，独立保存各整合包的模组、配置和存档。");
   Get<ComboBox>("CatalogGame").IsEnabled=Get<ComboBox>("CatalogLoader").IsEnabled=Kind=="modpack"&&!Working;
  }
  public void RefreshLanguage(){
   foreach(string name in new[]{"CatalogSource","CatalogLoader","CatalogCategory"})foreach(ComboBoxItem item in Get<ComboBox>(name).Items){string key=Convert.ToString(item.Tag);string original=key=="all"||key==""?"全部":key=="modrinth"?"Modrinth":key=="curseforge"?"CurseForge":CategoryLabel(key);item.Content=UiText.T(original);}
   if(Visible){UpdateTarget();Get<TextBlock>("DownloadsTitle").Text=UiText.T(Kind=="mod"?"Mod 下载":"整合包下载");Get<Button>("CatalogInstall").Content=UiText.T(Kind=="mod"?"下载并安装 Mod":"安装到整合包库");}VersionChanged();
  }
  static string CategoryLabel(string key){var labels=new Dictionary<string,string>{{"forge","Forge"},{"fabric","Fabric"},{"neoforge","NeoForge"},{"quilt","Quilt"},{"optimization","性能优化"},{"library","前置库"},{"technology","科技"},{"magic","魔法"},{"worldgen","世界生成"},{"adventure","冒险"},{"decoration","装饰"},{"quests","任务"},{"lightweight","轻量"},{"kitchen-sink","大型综合"},{"hardcore","硬核"},{"multiplayer","多人"}};return labels.ContainsKey(key)?labels[key]:key;}
  async Task Search(bool paging){
   if(Working||!Visible)return;if(!paging)Page=0;if(SearchCancel!=null)SearchCancel.Cancel();SearchCancel=new CancellationTokenSource();var ct=SearchCancel.Token;string kind=Kind;
   if(VersionCancel!=null)VersionCancel.Cancel();Project=null;Versions.Clear();Get<ComboBox>("CatalogVersions").ItemsSource=null;Get<ListBox>("CatalogResults").ItemsSource=null;Get<FrameworkElement>("CatalogDetail").Visibility=Visibility.Collapsed;Get<FrameworkElement>("CatalogDetailEmpty").Visibility=Visibility.Visible;
   UpdateTarget();var query=new CatalogQuery{Kind=Kind,Source=Tag("CatalogSource"),Text=Get<TextBox>("CatalogName").Text.Trim(),Game=Get<ComboBox>("CatalogGame").Text.Trim(),Loader=Tag("CatalogLoader"),Category=Tag("CatalogCategory"),Page=Page};
   Get<Button>("CatalogSearch").IsEnabled=false;Status(UiText.T("正在搜索…"));
   try{var results=await Api.Search(query,ct);if(ct.IsCancellationRequested||Kind!=kind||!Visible)return;More=results.More;Get<ListBox>("CatalogResults").ItemsSource=results.Projects;Get<TextBlock>("CatalogPageNumber").Text=UiText.T("第 ")+(Page+1)+UiText.T(" 页")+" · "+results.Projects.Length+UiText.T(" 个项目");Status(results.Projects.Length==0?UiText.T("未找到符合条件的项目。可尝试名称、英文关键词或调整筛选。"):UiText.T("选择项目查看适配版本。"));if(results.Warning!="")Status(Get<TextBlock>("CatalogStatus").Text+"\n"+UiText.T("部分来源暂不可用：")+results.Warning);Get<Button>("CatalogPrev").IsEnabled=Page>0;Get<Button>("CatalogNext").IsEnabled=More;var ignored=LoadIcons(results.Projects,ct);}
   catch(OperationCanceledException){if(!ct.IsCancellationRequested)Status(UiText.T("查询超时，请重试。"));}catch(Exception ex){if(!ct.IsCancellationRequested)Status(ex.Message);}finally{if(!ct.IsCancellationRequested)Get<Button>("CatalogSearch").IsEnabled=!Working;}
  }
  async Task LoadIcons(CatalogProject[] projects,CancellationToken ct){using(var gate=new SemaphoreSlim(5))await Task.WhenAll(projects.Select(async p=>{await gate.WaitAsync();try{if(ct.IsCancellationRequested||String.IsNullOrEmpty(p.IconUrl))return;byte[] bytes=await CatalogNetwork.Icon(p.IconUrl,ct);if(ct.IsCancellationRequested)return;using(var stream=new MemoryStream(bytes)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=64;image.StreamSource=stream;image.EndInit();image.Freeze();p.IconSource=image;}}catch{}finally{gate.Release();}}));}
  async Task ShowProject(bool more){
   if(Working)return;if(VersionCancel!=null)VersionCancel.Cancel();VersionCancel=new CancellationTokenSource();var ct=VersionCancel.Token;var project=Project;string kind=Kind;
   if(!more){Versions.Clear();VersionOffset=0;Get<ComboBox>("CatalogVersions").ItemsSource=null;}
   Get<FrameworkElement>("CatalogDetail").Visibility=project==null?Visibility.Collapsed:Visibility.Visible;Get<FrameworkElement>("CatalogDetailEmpty").Visibility=project==null?Visibility.Visible:Visibility.Collapsed;if(project==null){VersionChanged();return;}
   Get<TextBlock>("CatalogProjectName").Text=project.Name;Get<TextBlock>("CatalogDescription").Text=project.Summary;Get<TextBlock>("CatalogProjectMeta").Text=project.Meta;Get<Button>("CatalogMoreVersions").IsEnabled=false;Status(UiText.T("正在获取适配版本…"));
   try{UpdateTarget();string game=Get<ComboBox>("CatalogGame").Text.Trim(),loader=Tag("CatalogLoader");var versions=await Api.Versions(project,game,loader,VersionOffset,ct);if(ct.IsCancellationRequested||project!=Project||kind!=Kind||!Visible)return;VersionOffset+=40;Versions.AddRange(versions);Get<ComboBox>("CatalogVersions").ItemsSource=Versions.ToArray();Get<ComboBox>("CatalogVersions").SelectedIndex=Versions.Count>0?0:-1;Get<Button>("CatalogMoreVersions").IsEnabled=versions.Length>0;Status(Versions.Count==0?UiText.T("这个项目没有适配当前筛选的版本。"):UiText.T("选择版本后下载，必需前置将一并解析。"));}
   catch(OperationCanceledException){if(!ct.IsCancellationRequested)Status(UiText.T("查询超时，请重试。"));}catch(Exception ex){if(!ct.IsCancellationRequested)Status(ex.Message);}finally{VersionChanged();}
  }
  void VersionChanged(){var v=Get<ComboBox>("CatalogVersions").SelectedItem as CatalogVersion;Get<Button>("CatalogInstall").IsEnabled=!Working&&!Busy()&&v!=null&&v.File!=null&&!String.IsNullOrEmpty(v.File.Url)&&(Kind!="mod"||Target!=null);Get<TextBlock>("CatalogFileInfo").Text=v==null||v.File==null?"":v.File.Name+"\n"+(v.File.Size/1048576.0).ToString("0.00")+" MB · "+v.Channel+(String.IsNullOrEmpty(v.File.Url)?"\n"+UiText.T("作者未开放自动下载，请前往项目网页。"):"");}
  void Controls(bool enabled){Working=!enabled;foreach(string name in new[]{"CatalogSearch","CatalogReset","CatalogName","CatalogSource","CatalogCategory","CatalogResults","CatalogVersions","CatalogWebsite","LibraryNav","ModDownloadsNav","PackDownloadsNav","SkinsNav","SettingsNav","DownloadsBack"})Get<Control>(name).IsEnabled=enabled;Get<Button>("CatalogCancel").Visibility=enabled?Visibility.Collapsed:Visibility.Visible;Get<ProgressBar>("CatalogProgress").Visibility=enabled?Visibility.Collapsed:Visibility.Visible;Get<ComboBox>("CatalogGame").IsEnabled=Get<ComboBox>("CatalogLoader").IsEnabled=enabled&&Kind=="modpack";Get<Button>("CatalogPrev").IsEnabled=enabled&&Page>0;Get<Button>("CatalogNext").IsEnabled=enabled&&More;Get<Button>("CatalogMoreVersions").IsEnabled=enabled&&Versions.Count>0;VersionChanged();}
  async Task Install(){
   if(Working||Busy())return;if(EngineRunning()){Status(UiText.T("请先退出 Prism 和游戏，再安装资源。"));return;}var project=Project;var version=Get<ComboBox>("CatalogVersions").SelectedItem as CatalogVersion;if(project==null||version==null)return;
   ModTarget target=null;try{if(Kind=="mod")target=ModTarget.Capture(Service,Selected());}catch(Exception ex){Status(ex.Message);return;}string kind=Kind;Instance installed=null;bool complete=false;Controls(false);
   try{await Runner(async(global,ct)=>{using(InstallCancel=CancellationTokenSource.CreateLinkedTokenSource(ct)){
    var uiProgress=new Progress<Update>(u=>{if(!Working)return;Status(u.Message);var bar=Get<ProgressBar>("CatalogProgress");bar.IsIndeterminate=u.Total<=0;if(u.Total>0){bar.Maximum=u.Total;bar.Value=u.Done;}});var progress=new Both(global,uiProgress);
    try{if(kind=="mod"){
      if(version.Source!=project.Source||version.ProjectId!=project.Id)throw new IOException("所选版本不属于此项目。");var fresh=await Api.Version(version.Source,version.ProjectId,version.Id,InstallCancel.Token);var result=await Installer.Install(target,fresh,progress,InstallCancel.Token);Log("已安装到“"+target.Name+"”："+result.Files+" 个新文件，解析 "+result.Dependencies+" 个必需前置，替换 "+result.Replaced+" 个旧文件。备份："+result.Backup);Window.Dispatcher.Invoke(()=>Imported(new Instance{Id=target.Id}));
     }else{var fresh=await Api.Version(version.Source,version.ProjectId,version.Id,InstallCancel.Token);installed=await Installer.InstallPack(project,fresh,progress,i=>Window.Dispatcher.Invoke(()=>Imported(i)),InstallCancel.Token);Window.Dispatcher.Invoke(()=>Imported(installed));Log("整合包“"+installed.Name+"”已添加到整合包库。"+(installed.Ready?"文件已补全。":"部分文件待补全，可在库中重试。"));}complete=true;}
    catch(OperationCanceledException){Window.Dispatcher.Invoke(()=>Status(UiText.T("下载已取消，原有 Mod 保持不变；已导入的整合包可在库中继续补全。")));throw;}
    catch(Exception ex){Window.Dispatcher.Invoke(()=>Status(ex.Message));throw;}
   }});
   }catch(Exception ex){Status(ex.Message);}finally{InstallCancel=null;Controls(true);UpdateTarget();if(complete)Status(kind=="mod"?UiText.T("安装完成，仅修改了所选整合包。"):(installed.Ready?UiText.T("整合包已添加到库，文件已补全。"):UiText.T("整合包已添加到库，部分文件需要补全。")));}
   if(complete&&kind=="modpack")Get<Button>("DownloadsBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  }
  sealed class Both:IProgress<Update>{readonly IProgress<Update> a,b;public Both(IProgress<Update> x,IProgress<Update> y){a=x;b=y;}public void Report(Update u){a.Report(u);b.Report(u);}}
 }
}
