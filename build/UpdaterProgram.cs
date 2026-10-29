using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;
using FoolLauncher;

[assembly:AssemblyTitle("智熄的启动器更新器")]
[assembly:AssemblyVersion("0.3.4.0")]
internal static class UpdaterProgram {
 [STAThread] static int Main(string[] args) {
  var app=new Application();var ui=(Window)XamlReader.Parse(@"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Title='智熄的启动器 · 程序更新' Width='650' SizeToContent='Height' ResizeMode='NoResize' WindowStartupLocation='CenterScreen' Background='#10171E' Foreground='#EDF2F5' FontFamily='Microsoft YaHei UI' FontSize='14'>
 <Window.Resources><Style TargetType='Button'><Setter Property='Background' Value='#263440'/><Setter Property='Foreground' Value='#EDF2F5'/><Setter Property='Padding' Value='18,11'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></Window.Resources>
 <StackPanel Margin='28'><TextBlock Text='保留数据，更新启动器' FontSize='26' FontWeight='SemiBold'/><TextBlock Text='只更新程序文件 · 保留整合包、模组、存档、皮肤与账号' Foreground='#ACBDC6' Margin='0,9,0,22'/>
  <Border Background='#19232D' CornerRadius='12' Padding='20'><StackPanel><TextBlock Text='原启动器文件夹' FontWeight='SemiBold'/><Grid Margin='0,10,0,16'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='Root' Background='#10171E' Foreground='#EDF2F5' CaretBrush='#EDF2F5' BorderBrush='#2C4247' Padding='8' VerticalContentAlignment='Center'/><Button x:Name='Browse' Grid.Column='1' Content='选择…' Margin='8,0,0,0' Padding='14,8'/></Grid><TextBlock x:Name='Details' TextWrapping='Wrap' LineHeight='22' Foreground='#ACBDC6'/></StackPanel></Border>
  <TextBlock x:Name='Status' Margin='0,18,0,16' TextWrapping='Wrap' LineHeight='22' Foreground='#75D5B7'/>
  <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'><Button x:Name='Cancel' Content='关闭' IsCancel='True' Margin='0,0,10,0'/><Button x:Name='Start' Content='更新此目录' Background='#75D5B7' Foreground='#10251F' FontWeight='SemiBold'/></StackPanel>
 </StackPanel>
</Window>");
  var folder=(TextBox)ui.FindName("Root");var browse=(Button)ui.FindName("Browse");var start=(Button)ui.FindName("Start");var close=(Button)ui.FindName("Cancel");var details=(TextBlock)ui.FindName("Details");var status=(TextBlock)ui.FindName("Status");
  start.IsEnabled=false;bool busy=false,complete=false;string package=null,installed=null;int result=0;
  close.Click+=(s,e)=>ui.Close();ui.Closing+=(s,e)=>{if(busy)e.Cancel=true;};
  browse.Click+=(s,e)=>{using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description="选择原来的启动器文件夹，不要新建或删除旧目录。",SelectedPath=folder.Text,ShowNewFolderButton=false})if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)folder.Text=picker.SelectedPath;};
  Func<Task> install=async()=>{
   if(busy)return;
   if(complete){try{Process.Start(new ProcessStartInfo(Path.Combine(installed,"智熄的启动器.exe")){UseShellExecute=true,WorkingDirectory=installed});ui.Close();}catch(Exception ex){status.Text=ex.Message;}return;}
   try {
    LauncherUpdateCore.ValidateRoot(folder.Text);string root=Path.GetFullPath(folder.Text);
    if(args.Length==0) {
     // Work from a temporary copy so the installed updater can also be replaced.
     string session=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhixiLauncher","updates",Guid.NewGuid().ToString("N"));LauncherUpdateCore.NoLinks(session);Directory.CreateDirectory(session);
     string worker=Path.Combine(session,"updater.exe"),copy=Path.Combine(session,"package.zip");File.Copy(Assembly.GetExecutingAssembly().Location,worker);File.Copy(package,copy);
     Process.Start(new ProcessStartInfo(worker,"--install "+Quote(copy)+" "+Quote(root)){UseShellExecute=false,WorkingDirectory=session});ui.Close();return;
    }
    busy=true;start.IsEnabled=false;close.IsEnabled=false;folder.IsReadOnly=true;browse.IsEnabled=false;status.Text="正在等待原启动器退出…请关闭原启动器窗口。";
    await Task.Run(async()=>{
     for(int attempt=0;attempt<120;attempt++){if(!LauncherRunning(root))return;await Task.Delay(500);}throw new IOException("原启动器仍在运行。请先退出它，再重新安装更新。");
    });
    status.Text="正在验证更新签名与文件…";
    string stage=Path.Combine(Path.GetDirectoryName(package),"verified-"+Guid.NewGuid().ToString("N"));
    var update=await Task.Run(()=>LauncherUpdateCore.Stage(package,stage,CancellationToken.None));
    status.Text="正在备份并替换程序文件…";
    string backup=await Task.Run(()=>LauncherUpdateCore.Apply(update,root));
    installed=root;complete=true;start.Content="启动启动器";details.Text="已更新到 "+update.Version+"\n旧程序备份："+backup;status.Text="更新完成。整合包、模组、存档和账号均保留在原目录。";
   }catch(Exception ex){status.Text="更新未完成："+ex.Message;result=1;}
   finally{busy=false;start.IsEnabled=true;close.IsEnabled=true;}
  };
  start.Click+=async(s,e)=>await install();
  ui.Loaded+=async(s,e)=>{
   try {
    if(args.Length>0){if(args.Length!=3||args[0]!="--install")throw new ArgumentException("无效的更新参数。");package=Path.GetFullPath(args[1]);folder.Text=args[2];await install();return;}
    var embedded=Assembly.GetExecutingAssembly().GetManifestResourceStream("FoolLauncher.Update.zip");
    if(embedded!=null){string session=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhixiLauncher","updates",Guid.NewGuid().ToString("N"));LauncherUpdateCore.NoLinks(session);Directory.CreateDirectory(session);package=Path.Combine(session,"package.zip");using(embedded)using(var file=File.Create(package))LauncherUpdateCore.CopyBounded(embedded,file,LauncherUpdateCore.MaxPackage,CancellationToken.None);}
    else{var picker=new OpenFileDialog{Title="选择签名的启动器更新 ZIP",Filter="启动器更新包|*.zip"};if(picker.ShowDialog(ui)!=true){ui.Close();return;}package=picker.FileName;}
    string stage=Path.Combine(Path.GetDirectoryName(package),".zhixi-preview-"+Guid.NewGuid().ToString("N"));
    // Preview extraction goes to private application cache, never next to a shared package.
    stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhixiLauncher","updates",Guid.NewGuid().ToString("N"));
    var update=await Task.Run(()=>LauncherUpdateCore.Stage(package,stage,CancellationToken.None));
    details.Text="更新版本："+update.Version+"\n"+update.Notes;
    foreach(string candidate in new[]{AppDomain.CurrentDomain.BaseDirectory,Environment.CurrentDirectory})try{LauncherUpdateCore.ValidateRoot(candidate);folder.Text=candidate;break;}catch{}
    status.Text="选择原来的启动器目录即可更新，无需删除或重新导入整合包。";start.IsEnabled=true;
   }catch(Exception ex){status.Text="无法加载更新："+ex.Message;start.IsEnabled=false;result=1;}
  };
  app.Run(ui);return result;
 }
 static string Quote(string s){if(s.Contains('"'))throw new ArgumentException("Invalid path.");return "\""+s.TrimEnd('\\')+"\"";}
 static bool LauncherRunning(string root) {return PortableLayout.Running(root);}
}
