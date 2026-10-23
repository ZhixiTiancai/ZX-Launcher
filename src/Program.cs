using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly:System.Reflection.AssemblyTitle("智熄的启动器")]
[assembly:System.Reflection.AssemblyProduct("智熄的启动器")]
[assembly:System.Reflection.AssemblyVersion("0.2.8.0")]
namespace FoolLauncher {
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   string program=AppDomain.CurrentDomain.BaseDirectory;
   string root=File.Exists(Path.Combine(program,"layout-v2"))?Directory.GetParent(program.TrimEnd(Path.DirectorySeparatorChar)).FullName:program;
   try {
    if(args.Length>0)return Command(args).GetAwaiter().GetResult();
    bool acquired;using(var mutex=new Mutex(true,"FoolLauncher-"+root.ToLowerInvariant().GetHashCode(),out acquired)) {
     if(!acquired){MessageBox.Show(UiText.T("此目录的智熄的启动器已经打开。"),UiText.T("智熄的启动器"));return 0;}
     var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,UiText.T("出现问题"));e.Handled=true;};
     var window=new LauncherWindow(root);app.Run(window.Window);
    }
    return 0;
   }catch(Exception ex){try{File.WriteAllText(Path.Combine(root,"startup-error.txt"),ex.ToString(),Encoding.UTF8);}catch{}if(args.Length==0)MessageBox.Show("启动失败："+ex.Message+"\n请查看启动目录的 startup-error.txt。",UiText.T("智熄的启动器"));else Console.Error.WriteLine(ex);return 1;}
  }
  static async Task<int> Command(string[] args) {
   if(args[0]=="--self-test")return SelfTests.Run();
   if(args[0]=="--verify-update"&&args.Length==2){string stage=Path.Combine(Path.GetTempPath(),"Zhixi-verify-"+Guid.NewGuid().ToString("N"));var update=LauncherUpdateCore.Stage(args[1],stage,CancellationToken.None);Console.WriteLine("VERIFIED_UPDATE="+update.Version+" FILES="+update.Files.Length);return 0;}
   if(args[0]=="--apply-update"&&args.Length==3){if(PortableLayout.Running(args[1]))throw new IOException("请先退出这个目录中的启动器、Prism 和游戏。");string stage=Path.Combine(Path.GetTempPath(),"Zhixi-install-"+Guid.NewGuid().ToString("N"));var update=LauncherUpdateCore.Stage(args[2],stage,CancellationToken.None);Console.WriteLine("UPDATE_BACKUP="+LauncherUpdateCore.Apply(update,args[1]));return 0;}
   if(args.Length<3)throw new ArgumentException("用法：--prepare <root> <pack>、--repair <root> <instance-id> 或 --metadata <root> <instance-id>");
   if(args[0]=="--import-only"){var imported=await new PackService(args[1]).Import(args[2],new Progress<Update>(p=>Console.WriteLine(p.Message)),CancellationToken.None);Console.WriteLine("IMPORTED="+imported.Id+" FILES="+imported.FileCount);return 0;}
   var service=new PackService(args[1]);var progress=new Progress<Update>(p=>Console.WriteLine(p.Done+"/"+p.Total+" "+p.Message));Instance i;
   if(args[0]=="--prepare")i=await service.Import(args[2],progress,CancellationToken.None);
   else if(args[0]=="--repair"||args[0]=="--metadata"||args[0]=="--game-files"||args[0]=="--runtime"||args[0]=="--export"||args[0]=="--export-saves")i=service.List().First(x=>x.Id==args[2]);
   else throw new ArgumentException("未知命令。");
   Console.WriteLine("INSTANCE="+i.Id);
   if(args[0]=="--export-saves"){if(args.Length!=4)throw new ArgumentException("用法：--export-saves <root> <instance-id> <output.zip>");var result=await new SaveExporter(service).Export(i,args[3],progress,CancellationToken.None);Console.WriteLine("EXPORTED_SAVES="+result.Path);return 0;}
   if(args[0]=="--export"){if(args.Length!=4)throw new ArgumentException("用法：--export <root> <instance-id> <output.zip>");var result=await new PackExporter(service).Export(i,args[3],progress,CancellationToken.None);Console.WriteLine("EXPORTED="+result.Path);return 0;}
   if(args[0]=="--runtime"){service.ConfigureInstance(i,8192,true);return 0;}
   if(args[0]=="--metadata"||args[0]=="--game-files"){var metadata=new MetadataCache(service.Data);await metadata.Ensure(service.Folder(i),progress,CancellationToken.None);if(args[0]=="--game-files")await new GameFiles(service).Ensure(metadata.VersionFiles,progress,CancellationToken.None);return 0;}
   return await service.Repair(i,progress,CancellationToken.None)==0?0:2;
  }
 }
 public sealed class LauncherWindow {
  LauncherPages Pages;Button DeletePackButton;
  public Window Window;readonly PackService Service;readonly PackIcons Icons;readonly AccountStore Accounts;Button AccountButton;
  ListBox PackList;TextBlock PackName,PackInfo,AccountText,RenameError;TextBox LogBox,RenameInput;ComboBox MemoryBox;ProgressBar TaskProgress;
  Button ImportButton,LaunchButton,RepairButton,FolderButton,EngineButton,CancelButton,LocalButton,MissingButton,RenameButton,RenameSaveButton,RenameCancelButton;
  CancellationTokenSource TaskCancel;bool Busy;DateTime LastProgress=DateTime.MinValue;string LastMessage="";bool MemoryLoading;
  Button IconButton,IconLocalButton,IconDownloadButton,IconResetButton,IconCloseButton,ExportButton,ModsButton,SaveExportButton;Image PackIcon;Border IconEditor;TextBox IconUrl;FrameworkElement PackActions;
  public LauncherWindow(string root) {
   Service=new PackService(root);
   Icons=new PackIcons(root);
   Accounts=new AccountStore(Service.Data,EngineRunning);
   // Fail early on a read-only extraction location.
   File.WriteAllText(Path.Combine(Service.Data,".write-test"),"");File.Delete(Path.Combine(Service.Data,".write-test"));
   string program=File.Exists(Path.Combine(root,"launcher","layout-v2"))?Path.Combine(root,"launcher"):root;
   using(var stream=File.OpenRead(Path.Combine(program,"App.xaml")))Window=(Window)XamlReader.Load(stream);
   using(var stream=typeof(Program).Assembly.GetManifestResourceStream("FoolLauncher.Logo.png")){var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();Window.Icon=bitmap;}
   PackList=Get<ListBox>("PackList");PackName=Get<TextBlock>("PackName");PackInfo=Get<TextBlock>("PackInfo");AccountText=Get<TextBlock>("AccountText");LogBox=Get<TextBox>("LogBox");MemoryBox=Get<ComboBox>("MemoryBox");TaskProgress=Get<ProgressBar>("TaskProgress");
   ImportButton=Get<Button>("ImportButton");LaunchButton=Get<Button>("LaunchButton");RepairButton=Get<Button>("RepairButton");FolderButton=Get<Button>("FolderButton");EngineButton=Get<Button>("EngineButton");CancelButton=Get<Button>("CancelButton");LocalButton=Get<Button>("LocalButton");MissingButton=Get<Button>("MissingButton");
   AccountButton=Get<Button>("AccountButton");AccountButton.Click+=(s,e)=>Try(()=>new AccountWindow(Window,Accounts,EngineRunning,RefreshAccount).Show());
   RenameButton=Get<Button>("RenameButton");RenameButton.Click+=(s,e)=>Try(RenamePack);
   RenameInput=Get<TextBox>("RenameInput");RenameError=Get<TextBlock>("RenameError");RenameSaveButton=Get<Button>("RenameSaveButton");RenameCancelButton=Get<Button>("RenameCancelButton");
   RenameSaveButton.Click+=(s,e)=>SaveRename();RenameCancelButton.Click+=(s,e)=>ShowPack();
   RenameInput.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){SaveRename();e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Escape){ShowPack();e.Handled=true;}};
   ExportButton=Get<Button>("ExportButton");ExportButton.Click+=async(s,e)=>{
    var instance=Current;if(instance==null||Busy)return;
    if(EngineRunning()){Log(UiText.T("请先退出 Prism 和游戏，再导出整合包。"));return;}
    string exports=Path.Combine(Service.Root,"exports");Directory.CreateDirectory(exports);
    var picker=new SaveFileDialog{Title=UiText.T("导出整合包"),Filter=UiText.T("整合包 ZIP|*.zip|Modrinth 整合包|*.mrpack"),DefaultExt=".zip",FileName=PackExporter.SuggestedName(instance),InitialDirectory=exports};if(picker.ShowDialog(Window)!=true)return;
    await Run(async p=>{var result=await new PackExporter(Service).Export(instance,picker.FileName,p,TaskCancel.Token);Log("已导出 "+result.Files+" 个文件："+result.Path+"。朋友可直接导入此整合包。");Process.Start(new ProcessStartInfo("explorer.exe","/select,"+Quote(result.Path)){UseShellExecute=true});});
   };
   ModsButton=Get<Button>("ModsButton");ModsButton.Click+=(s,e)=>{var instance=Current;if(instance!=null)Try(()=>{string mods=Path.Combine(Service.Game(instance),"mods");Directory.CreateDirectory(mods);OpenPath(mods);});};
   SaveExportButton=Get<Button>("SaveExportButton");SaveExportButton.Click+=async(s,e)=>{
    var instance=Current;if(instance==null||Busy)return;
    if(EngineRunning()){Log(UiText.T("请先保存世界并退出 Prism 和游戏，再导出存档。"));return;}
    string exports=Path.Combine(Service.Root,"exports");Directory.CreateDirectory(exports);
    var picker=new SaveFileDialog{Title=UiText.T("导出当前整合包的全部存档"),Filter=UiText.T("存档备份 ZIP|*.zip"),DefaultExt=".zip",FileName=SaveExporter.SuggestedName(instance),InitialDirectory=exports};if(picker.ShowDialog(Window)!=true)return;
    await Run(async p=>{var result=await new SaveExporter(Service).Export(instance,picker.FileName,p,TaskCancel.Token);Log("存档已导出（"+result.Files+" 个文件）："+result.Path+"。ZIP 内附有恢复说明。");Process.Start(new ProcessStartInfo("explorer.exe","/select,"+Quote(result.Path)){UseShellExecute=true});});
   };
   PackIcon=Get<Image>("PackIcon");IconButton=Get<Button>("IconButton");IconEditor=Get<Border>("IconEditor");IconUrl=Get<TextBox>("IconUrl");PackActions=Get<WrapPanel>("PackActions");
   IconLocalButton=Get<Button>("IconLocalButton");IconDownloadButton=Get<Button>("IconDownloadButton");IconResetButton=Get<Button>("IconResetButton");IconCloseButton=Get<Button>("IconCloseButton");
   IconButton.Click+=(s,e)=>{if(Current!=null&&!Busy){bool open=IconEditor.Visibility!=Visibility.Visible;ShowPack();ShowIconEditor(open);}};
   IconCloseButton.Click+=(s,e)=>ShowIconEditor(false);
   IconLocalButton.Click+=async(s,e)=>{
    var instance=Current;if(instance==null)return;
    var picker=new OpenFileDialog{Title=UiText.T("选择整合包图标"),Filter=UiText.T("图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico|所有文件|*.*")};if(picker.ShowDialog(Window)!=true)return;
    await Run(async p=>{p.Report(new Update(UiText.T("正在保存整合包图标…")));await Icons.SetLocal(instance,picker.FileName,TaskCancel.Token);Refresh(instance.Id);Log(UiText.T("已使用自定义图标，左侧列表已同步。"));},false);
   };
   IconDownloadButton.Click+=async(s,e)=>{
    var instance=Current;if(instance==null)return;string url=IconUrl.Text;
    await Run(async p=>{p.Report(new Update(UiText.T("正在下载整合包图标…")));await Icons.Download(instance,url,TaskCancel.Token);Refresh(instance.Id);Log(UiText.T("图标已下载保存，左侧列表已同步。"));},false);
   };
   IconResetButton.Click+=(s,e)=>Try(()=>{var instance=Current;if(instance==null||Busy)return;Icons.Reset(instance);Refresh(instance.Id);Log(UiText.T("已恢复默认草方块图标。"));});
   foreach(int gb in new[]{4,6,8,10,12,16,24})MemoryBox.Items.Add(new ComboBoxItem{Content=gb+" GB",Tag=gb*1024});MemoryBox.SelectedIndex=2;
   PackList.SelectionChanged+=(s,e)=>ShowPack();
   MemoryBox.SelectionChanged+=(s,e)=>{if(Current!=null&&!MemoryLoading&&!EngineRunning()){Service.ConfigureInstance(Current,Memory);Log("内存已设为 "+Memory/1024+" GB。");}};
   ImportButton.Click+=async(s,e)=>{
    var picker=new OpenFileDialog{Filter=UiText.T("Modrinth / CurseForge 整合包|*.mrpack;*.zip|所有文件|*.*"),Title=UiText.T("选择整合包")};if(picker.ShowDialog(Window)!=true)return;
    await ImportPackages(new[]{picker.FileName});
   };
   new PackDrop(Get<Border>("PackDropZone"),Get<Border>("PackDropCue"),Get<TextBlock>("PackDropHint"),()=>Busy,EngineRunning,ImportPackages,Log);
   RepairButton.Click+=async(s,e)=>{var i=Current;if(i==null)return;await Run(async p=>{await Service.Repair(i,p,TaskCancel.Token);var metadata=new MetadataCache(Service.Data);await metadata.Ensure(Service.Folder(i),p,TaskCancel.Token);await new GameFiles(Service).Ensure(metadata.VersionFiles,p,TaskCancel.Token);Refresh(i.Id);});};
   CancelButton.Click+=(s,e)=>{if(TaskCancel!=null){TaskCancel.Cancel();Log(UiText.T("正在停止任务，已完成的文件会保留。 "));}};
   LaunchButton.Click+=async(s,e)=>{
    var i=Current;if(i==null)return;
    string accountArguments;try{accountArguments=Accounts.LaunchArguments();}catch(Exception ex){Log(ex.Message);new AccountWindow(Window,Accounts,EngineRunning,RefreshAccount).Show();return;}
    if(EngineRunning()){Log("Prism 正在运行。请在 Prism 内启动该整合包，或退出 Prism 后再点击启动。");OpenEngine(null);return;}
    await Run(async p=>{int missing=await Service.Repair(i,p,TaskCancel.Token);Refresh(i.Id);if(missing!=0)throw new IOException(UiText.T("整合包仍有缺失文件，已阻止启动。可查看缺失清单或重试补全。"));var metadata=new MetadataCache(Service.Data);await metadata.Ensure(Service.Folder(i),p,TaskCancel.Token);await new GameFiles(Service).Ensure(metadata.VersionFiles,p,TaskCancel.Token);Service.ConfigureInstance(i,Memory,true);ConfigureEngine();OpenEngine(i.Id,accountArguments);Log("已交给 Prism：游戏文件已预检，继续启动模组加载器。 ");});
   };
   EngineButton.Click+=(s,e)=>Try(()=>{if(!EngineRunning())ConfigureEngine();OpenEngine(null);Log("已打开游戏引擎高级设置。账号可从本启动器右上角管理。");});
   FolderButton.Click+=(s,e)=>{if(Current!=null)Try(()=>OpenPath(Service.Game(Current)));};
   MissingButton.Click+=(s,e)=>Try(ShowMissing);
   LocalButton.Click+=async(s,e)=>{
    if(Current==null)return;var i=Current;
    var picker=new OpenFileDialog{Multiselect=true,Filter="模组和资源文件|*.jar;*.zip|所有文件|*.*",Title="选择手动下载的文件（会校验哈希）"};if(picker.ShowDialog(Window)!=true)return;
    await Run(async p=>{int n=await Service.AddLocalFiles(i,picker.FileNames,TaskCancel.Token);Log("已匹配并补入 "+n+" 个文件。点击校验 / 补全文件检查剩余内容。");});
   };
   Get<Button>("HelpButton").Click+=(s,e)=>Try(()=>OpenPath(Path.Combine(program,"使用说明.txt")));
   Window.Closing+=(s,e)=>{if(Busy){if(MessageBox.Show(Window,UiText.T("当前任务尚未完成。停止并退出？已完成文件会保留。"),UiText.T("退出启动器"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes){e.Cancel=true;return;}TaskCancel.Cancel();}};
   DeletePackButton=Get<Button>("DeletePackButton");DeletePackButton.Click+=async(s,e)=>{
    var instance=Current;if(instance==null||Busy)return;
    if(EngineRunning()){Log(UiText.T("请先退出 Prism 和游戏，再删除整合包。"));return;}
    if(!DeleteConfirmation.Show(Window,UiText.T("删除整合包"),instance.Name,UiText.T("此整合包中的模组、配置和全部存档都会一起移入回收站。"),UiText.T("需要恢复时，可在 Windows 回收站中还原此整合包文件夹。")))return;
    await Run(async progress=>{await Task.Run(()=>PackRemoval.Recycle(Service,instance));Refresh(null);Log(UiText.T("整合包已移入 Windows 回收站。"));});
   };
   Pages=new LauncherPages(Window,root,()=>{ShowPack();RefreshAccount();},Accounts,action=>Run(async p=>await action(TaskCancel.Token)),()=>Busy);
   new LauncherUpdateUi(Window,root,()=>!Busy,Log,()=>Pages.Navigate("settings"));
   Refresh(null);Log(UiText.T("启动器已就绪。先校验整合包文件，再完成正版登录，即可尝试启动。 "));
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};timer.Tick+=(s,e)=>RefreshAccount();timer.Start();
   RefreshAccount();
  }
  T Get<T>(string name) where T:class {return Window.FindName(name) as T;}
  Instance Current {get{return PackList.SelectedItem as Instance;}}
  int Memory {get{return MemoryBox.SelectedItem==null?8192:(int)((ComboBoxItem)MemoryBox.SelectedItem).Tag;}}
  async Task ImportPackages(string[] sources) {
   await Run(async progress=>{
    var failures=new List<string>();int imported=0;
    foreach(string source in sources) {
     TaskCancel.Token.ThrowIfCancellationRequested();
     try{Log(UiText.T("正在导入：")+Path.GetFileName(source));var i=await Service.Import(source,progress,TaskCancel.Token);Refresh(i.Id);imported++;await Service.Repair(i,progress,TaskCancel.Token);Refresh(i.Id);}
     catch(OperationCanceledException){throw;}
     catch(Exception ex){if(sources.Length==1)throw;string failure=Path.GetFileName(source)+"："+ex.Message;failures.Add(failure);Log(UiText.T("导入未完成：")+failure);}
    }
    if(sources.Length>1)Log("批量导入结束：已添加 "+imported+" / "+sources.Length+" 个整合包。请检查各包的补全状态。");
    if(failures.Count>0)MessageBox.Show(Window,String.Join("\n\n",failures),"以下整合包未能完成导入");
   });
  }
  void RenamePack() {
   var instance=Current;if(instance==null||Busy)return;
   if(EngineRunning()){Log(UiText.T("请先退出 Prism 和游戏，再修改整合包名称。"));return;}
   PackName.Visibility=Visibility.Collapsed;RenameButton.Visibility=Visibility.Collapsed;
   RenameInput.Text=instance.Name;RenameInput.Visibility=RenameSaveButton.Visibility=RenameCancelButton.Visibility=Visibility.Visible;
   RenameError.Visibility=Visibility.Collapsed;RenameInput.Focus();RenameInput.SelectAll();
  }
  void SaveRename() {
   var instance=Current;if(instance==null||Busy)return;
   try {
    if(EngineRunning())throw new IOException(UiText.T("请先退出 Prism 和游戏，再修改整合包名称。"));
    Service.Rename(instance,RenameInput.Text);Refresh(instance.Id);Log("整合包已重命名为：“"+instance.Name+"”。");
   }catch(Exception ex){RenameError.Text=ex.Message;RenameError.Visibility=Visibility.Visible;RenameInput.Focus();}
  }
  void Refresh(string id) {var items=Service.List();foreach(var item in items)item.IconSource=Icons.Load(item);PackList.ItemsSource=items;PackList.SelectedItem=items.FirstOrDefault(i=>i.Id==id)??items.FirstOrDefault();ShowPack();}
  void ShowIconEditor(bool visible) {IconEditor.Visibility=visible?Visibility.Visible:Visibility.Collapsed;PackInfo.Visibility=PackActions.Visibility=visible?Visibility.Collapsed:Visibility.Visible;}
  void ShowPack() {
   ShowIconEditor(false);
   PackName.Visibility=Visibility.Visible;RenameInput.Visibility=RenameSaveButton.Visibility=RenameCancelButton.Visibility=RenameError.Visibility=Visibility.Collapsed;
   var i=Current;PackName.Text=i==null?UiText.T("先导入一个整合包"):i.Name;
   PackIcon.Source=i==null?null:i.IconSource;
   PackInfo.Text=i==null?UiText.T("将整合包拖到左侧，或点击“导入整合包”选择文件。"):String.Format(UiText.T("版本 {0}   ·   Minecraft {1}\n{2}   ·   {3}\n{4}"),i.Version,i.Minecraft,i.Loader,i.BundledFileCount>0?String.Format(UiText.T("{0} 个包内文件"),i.BundledFileCount):String.Format(UiText.T("{0} 个清单文件"),i.FileCount),UiText.T(i.Ready?UiText.T("整合包文件已补全 · 启动时检查游戏依赖"):UiText.T("待校验 / 补全 · 完成后可启动")));
   if(i!=null){MemoryLoading=true;try{string file=Path.Combine(Service.Folder(i),"instance.cfg");if(File.Exists(file)){var line=File.ReadAllLines(file).FirstOrDefault(l=>l.StartsWith("MaxMemAlloc="));int m;if(line!=null&&int.TryParse(line.Substring(12),out m)){foreach(ComboBoxItem item in MemoryBox.Items)if((int)item.Tag==m)MemoryBox.SelectedItem=item;}}}finally{MemoryLoading=false;}}
   SetEnabled();
  }
  void SetEnabled() {bool selected=Current!=null;if(DeletePackButton!=null)DeletePackButton.IsEnabled=!Busy&&selected;ImportButton.IsEnabled=!Busy;PackList.IsEnabled=!Busy;MemoryBox.IsEnabled=!Busy;EngineButton.IsEnabled=AccountButton.IsEnabled=!Busy;LaunchButton.IsEnabled=!Busy&&selected;RepairButton.IsEnabled=!Busy&&selected;FolderButton.IsEnabled=selected;ModsButton.IsEnabled=!Busy&&selected;ExportButton.IsEnabled=SaveExportButton.IsEnabled=!Busy&&selected;LocalButton.IsEnabled=!Busy&&selected;MissingButton.IsEnabled=selected;CancelButton.IsEnabled=Busy;IconButton.IsEnabled=!Busy&&selected;IconButton.Visibility=selected?Visibility.Visible:Visibility.Collapsed;IconLocalButton.IsEnabled=IconDownloadButton.IsEnabled=IconResetButton.IsEnabled=IconCloseButton.IsEnabled=IconUrl.IsEnabled=!Busy;RenameInput.IsEnabled=RenameSaveButton.IsEnabled=RenameCancelButton.IsEnabled=!Busy;RenameButton.IsEnabled=!Busy&&selected;RenameButton.Visibility=selected&&RenameInput.Visibility!=Visibility.Visible?Visibility.Visible:Visibility.Collapsed;}
  async Task Run(Func<IProgress<Update>,Task> action,bool needsEngineStopped=true) {
   if(Busy)return;if(needsEngineStopped&&EngineRunning()){Log(UiText.T("请先退出随附的 Prism 和游戏，再修改整合包文件。 "));return;}
   Busy=true;TaskCancel=new CancellationTokenSource();SetEnabled();TaskProgress.IsIndeterminate=true;
   var progress=new Progress<Update>(u=>{
    TaskProgress.IsIndeterminate=u.Total==0;if(u.Total>0){TaskProgress.Maximum=u.Total;TaskProgress.Value=u.Done;}
    if((DateTime.UtcNow-LastProgress).TotalMilliseconds>300||u.Done==u.Total&&u.Total>0){Log(u.Message);LastProgress=DateTime.UtcNow;}
   });
   try{await action(progress);}catch(OperationCanceledException){Log(UiText.T("任务已停止。再次补全时会跳过已校验完成的文件。"));}catch(Exception ex){Log("操作未完成："+ex.Message);MessageBox.Show(Window,ex.Message,UiText.T("操作未完成"));}
   finally{Busy=false;TaskCancel.Dispose();TaskCancel=null;TaskProgress.IsIndeterminate=false;SetEnabled();ShowPack();}
  }
  void Try(Action action){try{action();}catch(Exception ex){Log(ex.Message);MessageBox.Show(Window,ex.Message,UiText.T("操作未完成"));}}
  void Log(string message) {
   if(message==LastMessage)return;LastMessage=message;string line=DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine;
   LogBox.AppendText(line);if(LogBox.Text.Length>50000)LogBox.Text=LogBox.Text.Substring(LogBox.Text.Length-30000);LogBox.ScrollToEnd();
   try{string logs=Path.Combine(Service.Root,"launcher","logs");Directory.CreateDirectory(logs);File.AppendAllText(Path.Combine(logs,"launcher.log"),line,new UTF8Encoding(false));}catch{}
  }
  bool EngineRunning() {
   foreach(var p in Process.GetProcessesByName("prismlauncher")) {
    try{if(String.Equals(p.MainModule.FileName,Path.Combine(Service.Root,"launcher","engine","prismlauncher.exe"),StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}finally{p.Dispose();}
   }
   return false;
  }
  void ConfigureEngine() {
   string exe=Path.Combine(Service.Root,"launcher","engine","prismlauncher.exe");if(!File.Exists(exe))throw new FileNotFoundException("缺少启动引擎。请完整解压整个压缩包。 ");
   var java=JavaRuntime.Find(Service.Root,new[]{17,21});if(java==null)throw new FileNotFoundException("缺少 Java 运行时，请完整解压。 ");
   string file=Path.Combine(Service.Data,"prismlauncher.cfg");
   var settings=new Dictionary<string,string>{{"JavaPath",java.Path.Replace('\\','/')},{"JavaVersion",java.Version},{"JavaArchitecture","64"},{"MinMemAlloc","1024"},{"MaxMemAlloc",Memory.ToString()},{"Language",UiText.Language=="en-US"?"en_US":"zh"},{"InstanceDir",Service.InstanceRoot.Replace('\\','/')},{"ShowConsole","false"},{"ShowConsoleOnError","true"},{"IgnoreJavaWizard","true"},{"UserAskedAboutAutomaticJavaDownload","true"}};
   string selectedClient=Accounts.LaunchClientId();if(!String.IsNullOrWhiteSpace(selectedClient))settings["MSAClientIDOverride"]=selectedClient;
   IniConfig.UpdateGeneral(file,settings);
  }
  static string Quote(string s) {return "\""+s.Replace("\"","")+"\"";}
  void OpenEngine(string id,string accountArguments="") {
   var info=new ProcessStartInfo(Path.Combine(Service.Root,"launcher","engine","prismlauncher.exe"),"--dir "+Quote(Service.Data)+(id==null?"":" --launch "+Quote(id)+accountArguments)){WorkingDirectory=Service.Root,UseShellExecute=false};
   Process.Start(info);
  }
  void RefreshAccount() {
   if(Pages!=null)Pages.RefreshAccount();
   try{var account=Accounts.Current();bool offline=Accounts.Mode()=="offline";AccountButton.Content=account==null?UiText.T("账号 · 未登录"):UiText.T("账号 · ")+account.Name+(offline?UiText.T("（离线）"):"");AccountText.Text=account==null?UiText.T("未选择账号，点击右上角“账号”登录或选择角色。"):UiText.T("当前账号：")+account.Name+UiText.T(offline?UiText.T(" · 离线模式"):UiText.T(" · 微软账号（启动时验证）"));}
   catch{AccountButton.Content=UiText.T("账号 · 读取失败");AccountText.Text=UiText.T("账号文件无法读取，原文件已保留。");}
  }
  void ShowMissing() {
   if(Current==null)return;string file=Path.Combine(Service.Folder(Current),"missing-files.json");if(!File.Exists(file)){Log("请先执行一次校验 / 补全文件。 ");return;}
   var missing=Json.Read<List<MissingFile>>(file);if(missing.Count==0){Log("当前缺失清单为空。 ");return;}
   string report=Path.Combine(Service.Folder(Current),"缺失文件清单.txt");var text=new StringBuilder("以下文件尚未完成下载。可在浏览器下载后使用“补入已下载文件”，程序会校验哈希。\r\n\r\n");foreach(var m in missing){text.AppendLine(m.path);text.AppendLine(m.reason);foreach(string url in m.downloads)text.AppendLine(url);text.AppendLine();}File.WriteAllText(report,text.ToString(),Encoding.UTF8);OpenPath(report);
  }
  void OpenPath(string path) {if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("路径不存在："+path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
 }
}
