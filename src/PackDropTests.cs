using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace FoolLauncher {
 public static class PackDropTests {
  public static void Run(string root) {
   string zip=Path.Combine(root,"drop.zip"),mrpack=Path.Combine(root,"drop.MRPACK"),text=Path.Combine(root,"not-a-pack.txt");File.WriteAllText(zip,"fixture");File.WriteAllText(mrpack,"fixture");File.WriteAllText(text,"fixture");
   var data=new DataObject(DataFormats.FileDrop,new[]{zip,mrpack,zip.ToUpperInvariant()});var files=PackDrop.Files(data);
   if(files.Length!=2||!files.Contains(zip)||!files.Contains(mrpack))throw new Exception("File drop formats/deduplication failed");
   foreach(var invalid in new IDataObject[]{new DataObject(DataFormats.Text,zip),new DataObject(DataFormats.FileDrop,new[]{root}),new DataObject(DataFormats.FileDrop,new[]{zip,text}),new DataObject(DataFormats.FileDrop,new[]{Path.Combine(root,"missing.zip")}),new DataObject(DataFormats.FileDrop,Enumerable.Repeat(zip,21).ToArray())}) {
    bool denied=false;try{PackDrop.Files(invalid);}catch(InvalidDataException){denied=true;}catch(IOException){denied=true;}if(!denied)throw new Exception("Invalid file drop accepted");
   }
   if(File.ReadAllText(zip)!="fixture"||File.ReadAllText(mrpack)!="fixture")throw new Exception("File drop modified source archive");
   RoutedEvents(data);
   Console.WriteLine("PASS: native file-drop payloads, ZIP/MRPACK, duplicate paths, text/folder/mixed/missing-file rejection and source preservation.");
  }
  static DragEventArgs Event(IDataObject data,RoutedEvent kind,DependencyObject target) {
   var ctor=typeof(DragEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(IDataObject),typeof(DragDropKeyStates),typeof(DragDropEffects),typeof(DependencyObject),typeof(Point)},null);
   if(ctor==null)throw new Exception("WPF drag-event test constructor missing");
   var args=(DragEventArgs)ctor.Invoke(new object[]{data,DragDropKeyStates.LeftMouseButton,DragDropEffects.Copy|DragDropEffects.Move,target,new Point(5,5)});args.RoutedEvent=kind;return args;
  }
  static void RoutedEvents(IDataObject data) {
   var child=new Button();var zone=new Border{Child=child};var cue=new Border{Visibility=Visibility.Collapsed};var hint=new TextBlock();bool busy=false,engine=false;string[] imported=null;int warnings=0;
   new PackDrop(zone,cue,hint,()=>busy,()=>engine,paths=>{imported=paths;return Task.FromResult(0);},message=>warnings++);
   var hover=Event(data,DragDrop.PreviewDragEnterEvent,child);child.RaiseEvent(hover);
   if(!zone.AllowDrop||!hover.Handled||hover.Effects!=DragDropEffects.Copy||cue.Visibility!=Visibility.Visible||!hint.Text.Contains("2"))throw new Exception("Nested-control drag hover failed");
   child.RaiseEvent(Event(data,DragDrop.PreviewDragLeaveEvent,child));if(cue.Visibility!=Visibility.Collapsed)throw new Exception("Drag leave cue stuck");
   var drop=Event(data,DragDrop.PreviewDropEvent,child);child.RaiseEvent(drop);
   if(imported==null||imported.Length!=2||drop.Effects!=DragDropEffects.Copy||!drop.Handled||cue.Visibility!=Visibility.Collapsed)throw new Exception("Routed drop import failed");
   imported=null;busy=true;child.RaiseEvent(Event(data,DragDrop.PreviewDropEvent,child));busy=false;engine=true;child.RaiseEvent(Event(data,DragDrop.PreviewDropEvent,child));
   if(imported!=null||warnings!=2)throw new Exception("Busy/game-running drop guard failed");
   Console.WriteLine("PASS: WPF nested-control preview event routing, copy-only effects, hover/leave/drop cues, import dispatch and busy/game-running guards.");
  }
 }
}
