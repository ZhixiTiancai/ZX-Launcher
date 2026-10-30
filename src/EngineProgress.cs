using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace FoolLauncher {
 // Only the progress dialogs of the process started for this launch are owned here.
 // Login prompts, error dialogs, consoles and Minecraft windows remain interactive.
 public sealed class EngineProgress : IDisposable {
  readonly int pid; readonly Dictionary<IntPtr,bool> hidden=new Dictionary<IntPtr,bool>();readonly object gate=new object();bool disposed;
  public EngineProgress(int processId){pid=processId;}
  internal static bool IsProgressTitle(string title){return new[]{"Please wait...","Please wait…","请稍候…","请稍候...","请稍等…","请稍等..."}.Any(prefix=>title==prefix||title.StartsWith(prefix+" - Prism Launcher",StringComparison.Ordinal));}
  public Update Read(){Update latest=null;foreach(var handle in Windows(pid)){
   if(!IsProgressTitle(Title(handle)))continue;
   try {var root=AutomationElement.FromHandle(handle);var all=root.FindAll(TreeScope.Descendants,System.Windows.Automation.Condition.TrueCondition);var labels=new List<string>();double value=0;bool hasValue=false;
    for(int n=0;n<Math.Min(all.Count,150);n++){var element=all[n];var type=element.Current.ControlType;
     if(type==ControlType.Text){string name=element.Current.Name;if(!String.IsNullOrWhiteSpace(name)&&name.Length<400&&!name.TrimEnd().EndsWith("%"))labels.Add(name);}
     object pattern;if(!hasValue&&type==ControlType.ProgressBar&&element.TryGetCurrentPattern(RangeValuePattern.Pattern,out pattern)){var range=((RangeValuePattern)pattern).Current;if(range.Maximum>range.Minimum){value=100*(range.Value-range.Minimum)/(range.Maximum-range.Minimum);hasValue=true;}}
    }
    // If accessibility is unavailable, keep the original dialog usable.
    if(labels.Count==0&&!hasValue)continue;
    latest=new Update(labels.Count==0?"正在准备游戏…":String.Join(" · ",labels.Distinct().Take(3)),hasValue?(int)Math.Max(0,Math.Min(100,value)):0,hasValue?100:0);
    lock(gate){if(!disposed&&IsWindowVisible(handle)){hidden[handle]=true;ShowWindowAsync(handle,0);}}
   }catch(ElementNotAvailableException){}catch(InvalidOperationException){}catch(COMException){}
  }return latest;}
  public void Dispose(){lock(gate){disposed=true;foreach(var handle in hidden.Keys) {uint owner;GetWindowThreadProcessId(handle,out owner);if(owner==pid&&IsProgressTitle(Title(handle)))ShowWindowAsync(handle,5);}hidden.Clear();}}
  public bool NeedsInteraction(){return Windows(pid).Any(h=>IsWindowVisible(h)&&!String.IsNullOrWhiteSpace(Title(h))&&!IsProgressTitle(Title(h)));}
  internal static List<IntPtr> Windows(int process){var result=new List<IntPtr>();EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==process)result.Add(h);return true;},IntPtr.Zero);return result;}
  static string Title(IntPtr h){var text=new StringBuilder(512);GetWindowText(h,text,text.Capacity);return text.ToString();}
  public static bool HasGameWindow(int parent){var children=new HashSet<int>{parent};var snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==new IntPtr(-1))return false;try{var entries=new List<ProcessEntry>();var e=new ProcessEntry();e.Size=(uint)Marshal.SizeOf(e);if(Process32First(snapshot,ref e))do{entries.Add(e);e.Size=(uint)Marshal.SizeOf(e);}while(Process32Next(snapshot,ref e));for(int depth=0;depth<5;depth++)foreach(var item in entries)if(children.Contains((int)item.Parent))children.Add((int)item.Id);foreach(var item in entries)if(children.Contains((int)item.Id)&&(item.Exe.Equals("javaw.exe",StringComparison.OrdinalIgnoreCase)||item.Exe.Equals("java.exe",StringComparison.OrdinalIgnoreCase)))if(Windows((int)item.Id).Any(h=>IsWindowVisible(h)&&GetWindow(h,4)==IntPtr.Zero))return true;return false;}finally{CloseHandle(snapshot);}}
  delegate bool EnumProc(IntPtr h,IntPtr p);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr p);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr h,int command);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessEntry {public uint Size,Usage,Id;public IntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;}
  [DllImport("kernel32.dll")]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
 public static class EngineLaunchUi {
  public static async Task Wait(Window owner,Process engine,string name,IProgress<Update> progress,CancellationToken ct){
   var dialog=new ThemedDialog(owner,"正在启动游戏",name);var status=ThemedDialog.Text("正在验证账号并准备游戏，请稍候…");dialog.Body.Children.Add(status);var bar=new ProgressBar{Height=7,Margin=new Thickness(0,18,0,14),IsIndeterminate=true,Maximum=100};bar.SetResourceReference(Control.ForegroundProperty,"Brush.Accent");bar.SetResourceReference(Control.BackgroundProperty,"Brush.Hero");dialog.Body.Children.Add(bar);var hint=ThemedDialog.Text("需要重新登录或处理错误时，请完成引擎弹出的提示。收起此窗口后，仍可在任务区域查看进度。");hint.SetResourceReference(TextBlock.ForegroundProperty,"Brush.Muted");dialog.Body.Children.Add(hint);bool reveal=false;dialog.Buttons.Children.Add(dialog.Button("显示引擎进度",()=>{reveal=true;dialog.Window.Close();},false));dialog.Buttons.Children.Add(dialog.Button("收起",()=>dialog.Window.Close(),true));dialog.Window.Show();string last=null;int lastDone=-1,lastTotal=-1;var start=DateTime.UtcNow;
   progress.Report(new Update("正在验证账号并准备游戏…"));using(var bridge=new EngineProgress(engine.Id))try{while(!engine.HasExited){ct.ThrowIfCancellationRequested();if(reveal){progress.Report(new Update("已恢复引擎进度窗口，可在其中继续操作。"));return;}var read=Task.Run(()=>bridge.Read());if(await Task.WhenAny(read,Task.Delay(3000,ct))!=read){ct.ThrowIfCancellationRequested();progress.Report(new Update("暂时无法读取引擎进度，已恢复原进度窗口。"));return;}var update=await read;if(update!=null){status.Text=update.Message;bar.IsIndeterminate=update.Total==0;if(update.Total>0)bar.Value=update.Done;if(last!=update.Message||lastDone!=update.Done||lastTotal!=update.Total){last=update.Message;lastDone=update.Done;lastTotal=update.Total;progress.Report(update);}}
     if(await Task.Run(()=>EngineProgress.HasGameWindow(engine.Id))){progress.Report(new Update("游戏窗口已打开，模组继续在游戏内加载。",100,100));return;}
     if(bridge.NeedsInteraction()){progress.Report(new Update("引擎需要您处理登录或错误提示，请查看已显示的引擎窗口。"));return;}
     if(DateTime.UtcNow-start>TimeSpan.FromMinutes(5)){progress.Report(new Update("准备时间较长，已恢复引擎窗口，请查看网络、登录或错误提示。"));return;}
     await Task.Delay(400,ct);
    }progress.Report(new Update("启动引擎已退出。若游戏未打开，请检查账号或引擎日志。"));
   }catch(OperationCanceledException){progress.Report(new Update("已停止进度监视并恢复引擎窗口；启动可能仍在继续，请在引擎中取消。"));}finally{dialog.Window.Close();}
  }
 }
}
