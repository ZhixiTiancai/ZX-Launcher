using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace FoolLauncher {
 // Handle the preview events so list items and the import button share one drop target.
 public sealed class PackDrop {
  readonly Border Cue;readonly TextBlock Hint;readonly Func<bool> Busy,EngineRunning;readonly Func<string[],Task> Import;readonly Action<string> Log;
  DateTime CheckedEngine=DateTime.MinValue;bool EngineBusy;
  public PackDrop(Border zone,Border cue,TextBlock hint,Func<bool> busy,Func<bool> engineRunning,Func<string[],Task> import,Action<string> log) {
   Cue=cue;Hint=hint;Busy=busy;EngineRunning=engineRunning;Import=import;Log=log;zone.AllowDrop=true;
   zone.AddHandler(DragDrop.PreviewDragEnterEvent,new DragEventHandler(Hover),true);
   zone.AddHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler(Hover),true);
   zone.AddHandler(DragDrop.PreviewDragLeaveEvent,new DragEventHandler((s,e)=>{Hide();e.Handled=true;}),true);
   zone.AddHandler(DragDrop.PreviewDropEvent,new DragEventHandler(Drop),true);
  }
  internal static string[] Files(IDataObject data) {
   if(data==null||!data.GetDataPresent(DataFormats.FileDrop,false))throw new InvalidDataException(UiText.T("请从文件资源管理器拖入 .zip 或 .mrpack 整合包文件。"));
   var paths=data.GetData(DataFormats.FileDrop,false) as string[];
   if(paths==null||paths.Length==0)throw new InvalidDataException(UiText.T("没有可导入的整合包文件。"));
   if(paths.Length>20)throw new InvalidDataException(UiText.T("请每次最多拖入 20 个整合包。"));
   var files=paths.Select(path=>Path.GetFullPath(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   foreach(string file in files) {
    if(Directory.Exists(file))throw new InvalidDataException(UiText.T("请拖入文件夹里的整合包 ZIP 或 .mrpack 文件，而不是整个文件夹。"));
    if(!File.Exists(file))throw new FileNotFoundException(UiText.T("拖入的文件不存在或无法读取：")+Path.GetFileName(file));
    string extension=Path.GetExtension(file);if(!extension.Equals(".zip",StringComparison.OrdinalIgnoreCase)&&!extension.Equals(".mrpack",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.T("这里只接受 .zip 或 .mrpack 整合包文件。"));
   }
   return files;
  }
  void Hide(){Cue.Visibility=Visibility.Collapsed;}
  void Hover(object sender,DragEventArgs e) {
   e.Handled=true;e.Effects=DragDropEffects.None;string message;bool accepted=false;
   try {
    var files=Files(e.Data);
    if(Busy())message=UiText.T("正在处理任务\n完成后再拖入整合包");
    else {
     if((DateTime.UtcNow-CheckedEngine).TotalSeconds>1){EngineBusy=EngineRunning();CheckedEngine=DateTime.UtcNow;}
     if(EngineBusy)message=UiText.T("请先退出 Prism 和游戏\n再拖入整合包");
     else if((e.AllowedEffects&DragDropEffects.Copy)==0)message=UiText.T("此拖入来源不支持复制导入");
     else{accepted=true;e.Effects=DragDropEffects.Copy;message=files.Length==1?UiText.T("松开以导入整合包"):UiText.T("松开以导入 ")+files.Length+UiText.T(" 个整合包");}
    }
   }catch(Exception ex){message=ex.Message;}
   Hint.Text=message;Cue.BorderBrush=new SolidColorBrush(accepted?Color.FromRgb(117,213,183):Color.FromRgb(233,175,107));Cue.Visibility=Visibility.Visible;
  }
  async void Drop(object sender,DragEventArgs e) {
   e.Handled=true;e.Effects=DragDropEffects.None;Hide();
   try {
    if(Busy()){Log(UiText.T("当前任务尚未完成，请完成后再拖入整合包。"));return;}
    if(EngineRunning()){Log(UiText.T("请先退出 Prism 和游戏，再拖入整合包。"));return;}
    if((e.AllowedEffects&DragDropEffects.Copy)==0)throw new InvalidDataException(UiText.T("此拖入来源不支持复制导入。"));
    var files=Files(e.Data);e.Effects=DragDropEffects.Copy;await Import(files);
   }catch(Exception ex){Log(UiText.T("拖入未完成：")+ex.Message);MessageBox.Show(ex.Message,UiText.T("拖入整合包"));}
  }
 }
}
