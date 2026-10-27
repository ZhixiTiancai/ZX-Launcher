using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace FoolLauncher {
 public sealed class JavaPage {
  readonly Window Window;readonly string Root;readonly Func<Instance> Selected;readonly Func<bool> Busy;readonly Func<Func<IProgress<Update>,CancellationToken,Task>,Task> Runner;JavaManager Manager;bool Loading,Working,Visible,Loaded;JavaRelease Release;CancellationTokenSource Cancel;
  T Get<T>(string name)where T:class{return Window.FindName(name) as T;}
  public JavaPage(Window window,string root,Func<Instance> selected,Func<bool> busy,Func<Func<IProgress<Update>,CancellationToken,Task>,Task> runner){Window=window;Root=root;Selected=selected??(()=>null);Busy=busy??(()=>false);Runner=runner;Manager=new JavaManager(root);
   Get<CheckBox>("JavaAuto").Click+=(s,e)=>{if(Loading)return;if(Busy()||Working){RefreshLanguage();return;}try{if(Get<CheckBox>("JavaAuto").IsChecked==true){Manager.Auto();Status(UiText.T("已启用自动选择：每次启动按整合包版本匹配 Java。"));}else{Get<CheckBox>("JavaAuto").IsChecked=Manager.Settings.Automatic;Status(UiText.T("请在列表中选择 Java，再点击“使用所选 Java”。"));}RefreshLanguage();}catch(Exception ex){Status(ex.Message);RefreshLanguage();}};
   Get<Button>("JavaScan").Click+=async(s,e)=>await Scan(true,null);Get<Button>("JavaRefresh").Click+=async(s,e)=>await Scan(false,null);
   Get<Button>("JavaScanFolder").Click+=async(s,e)=>{if(Working||Busy())return;using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description=UiText.T("选择要扫描 Java 的文件夹"),ShowNewFolderButton=false})if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)await Scan(false,picker.SelectedPath);};
   Get<Button>("JavaAdd").Click+=async(s,e)=>{if(Working||Busy())return;var picker=new OpenFileDialog{Title=UiText.T("选择 Java 的 javaw.exe 或 java.exe"),Filter="Java|javaw.exe;java.exe",CheckFileExists=true};if(picker.ShowDialog(Window)!=true)return;try{var j=JavaManager.Inspect(picker.FileName);if(j==null)throw new IOException(UiText.T("无法识别此 Java，请选择完整 Java 安装目录中的可执行文件。"));Manager.Remember(new[]{j});await Scan(false,null);Get<ListBox>("JavaList").SelectedItem=(Get<ListBox>("JavaList").ItemsSource as JavaRuntime[]).FirstOrDefault(x=>x.Path==j.Path);}catch(Exception ex){Status(ex.Message);}};
   Get<ListBox>("JavaList").SelectionChanged+=(s,e)=>{Get<Button>("JavaUse").IsEnabled=!Working&&!Busy()&&Get<ListBox>("JavaList").SelectedItem is JavaRuntime;};
   Get<Button>("JavaUse").Click+=(s,e)=>{if(Working||Busy())return;try{Manager.Use(Get<ListBox>("JavaList").SelectedItem as JavaRuntime);RefreshLanguage();Status(UiText.T("已保存手动 Java；版本不兼容时会在启动前提示。"));}catch(Exception ex){Status(ex.Message);}};
   foreach(int major in JavaDownload.Majors)Get<ComboBox>("JavaMajor").Items.Add(major);Get<ComboBox>("JavaMajor").SelectedItem=21;Get<ComboBox>("JavaMajor").SelectionChanged+=(s,e)=>{Release=null;Get<TextBlock>("JavaRelease").Text="";Get<Button>("JavaDownload").IsEnabled=false;};
   Get<Button>("JavaCheck").Click+=async(s,e)=>await Run(async(p,ct)=>{Release=null;int major=(int)Get<ComboBox>("JavaMajor").SelectedItem;Status(UiText.T("正在查询官方 Java 下载…"));Release=await JavaDownload.Latest(major,ct);Get<TextBlock>("JavaRelease").Text=Release.ToString();Status(UiText.T("已获取官方版本，可以下载。"));});
   Get<Button>("JavaDownload").Click+=async(s,e)=>{var release=Release;if(release==null)return;await Run(async(p,ct)=>{var j=await JavaDownload.Install(Root,release,p,ct);Manager.Remember(new[]{j});await Load(false,null,ct);Status(UiText.T("Java 已下载到启动器 runtime 文件夹。当前选择模式保持不变。"));});};
   Get<Button>("JavaCancel").Click+=(s,e)=>{if(Cancel!=null)Cancel.Cancel();};Window.Closed+=(s,e)=>{if(Cancel!=null)Cancel.Cancel();};RefreshLanguage();
  }
  void Status(string text){Get<TextBlock>("JavaStatus").Text=text;}
  async Task Run(Func<IProgress<Update>,CancellationToken,Task> job){if(Working||Busy())return;await Runner(async(p,ct)=>{Working=true;Cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);Controls();var progress=new Progress<Update>(u=>{if(Working)Status(u.Message);p.Report(u);});try{await job(progress,Cancel.Token);}catch(OperationCanceledException){Status(UiText.T("Java 任务已停止，原有 Java 保留。"));throw;}catch(Exception ex){Status(ex.Message);throw;}finally{Working=false;Cancel.Dispose();Cancel=null;Controls();}});Controls();}
  async Task Scan(bool computer,string folder){await Run(async(p,ct)=>{Status(UiText.T("正在检测 Java…"));await Load(computer,folder,ct);Loaded=true;Status(String.Format(UiText.T("已找到 {0} 个 Java；选择后可手动使用，或保留自动选择。"),Get<ListBox>("JavaList").Items.Count));});}
  async Task Load(bool computer,string folder,CancellationToken ct){Manager=new JavaManager(Root);var list=await Task.Run(()=>folder==null?Manager.List(computer,ct):JavaManager.ScanFolders(new[]{folder},8,ct),ct);ct.ThrowIfCancellationRequested();if(computer||folder!=null){Manager.Remember(list);list=await Task.Run(()=>Manager.List(false,ct),ct);}Get<ListBox>("JavaList").ItemsSource=list;string selected="";try{selected=Manager.Full(Manager.Settings.Selected);}catch{}Get<ListBox>("JavaList").SelectedItem=list.FirstOrDefault(j=>j.Path==selected);RefreshLanguage();}
  void Controls(){bool enabled=!Working&&!Busy();foreach(string name in new[]{"JavaScan","JavaRefresh","JavaAdd","JavaScanFolder","JavaCheck"})Get<Button>(name).IsEnabled=enabled;Get<ComboBox>("JavaMajor").IsEnabled=enabled;Get<CheckBox>("JavaAuto").IsEnabled=enabled;Get<Button>("JavaUse").IsEnabled=enabled&&Get<ListBox>("JavaList").SelectedItem is JavaRuntime;Get<Button>("JavaDownload").IsEnabled=enabled&&Release!=null;Get<Button>("JavaCancel").Visibility=Working?Visibility.Visible:Visibility.Collapsed;Get<ProgressBar>("JavaProgress").Visibility=Working?Visibility.Visible:Visibility.Collapsed;}
  public void RefreshLanguage(){Loading=true;Get<CheckBox>("JavaAuto").IsChecked=Manager.Settings.Automatic;Loading=false;string path;try{path=Manager.Full(Manager.Settings.Selected);}catch{path=Manager.Settings.Selected;}Get<TextBlock>("JavaMode").Text=Manager.Settings.Automatic?UiText.T("当前：自动选择，启动时重新检查兼容性。"):UiText.T("当前手动 Java：")+path;var pack=Selected();Get<TextBlock>("JavaTarget").Text=pack==null?UiText.T("还没有选择整合包。"):UiText.T("当前整合包：")+pack.Name+" · Minecraft "+pack.Minecraft+UiText.T(" · 建议 Java ")+String.Join(" / ",JavaRuntime.RequiredMajors(Root,pack.Minecraft));Controls();}
  public void Show(bool visible){Visible=visible;if(!visible)return;Manager=new JavaManager(Root);RefreshLanguage();if(!Loaded&&!Working&&!Busy()){var ignored=Scan(false,null);}}
 }
}
