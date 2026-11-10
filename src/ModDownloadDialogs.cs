using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FoolLauncher {
 static class ModDownloadDialogs {
  static void List(ThemedDialog dialog,IEnumerable<string> lines){
   var items=new StackPanel();
   foreach(string line in lines){
    var text=ThemedDialog.Text(line);text.Margin=new Thickness(0,0,0,8);items.Children.Add(text);
   }
   dialog.Body.Children.Add(new ScrollViewer{Content=items,MaxHeight=280,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  }
  static bool Confirm(ThemedDialog dialog,string yes,string no){
   bool accepted=false;
   dialog.Buttons.Children.Add(dialog.Button(no,()=>dialog.Window.Close(),false));
   dialog.Buttons.Children.Add(dialog.Button(yes,()=>{accepted=true;dialog.Window.Close();},true));
   dialog.Window.ShowDialog();return accepted;
  }
  internal static bool Download(Window owner,ModTarget target,CatalogVersion main,List<CatalogVersion> versions){
   var dialog=new ThemedDialog(owner,"下载 Mod 和前置","包含 "+(versions.Count-1)+" 个必需前置 · 安装到「"+target.Name+"」");
   var rows=new[]{"主模组："+Label(main)}.Concat(versions.Where(v=>!Object.ReferenceEquals(v,main)).Select(v=>"必需前置："+Label(v)));
   List(dialog,rows);
   return Confirm(dialog,"下载并安装","返回");
  }
  static string Label(CatalogVersion v){
   string title=String.IsNullOrWhiteSpace(v.Name)?v.Number:v.Name;
   return String.IsNullOrWhiteSpace(title)||title==v.File.Name?v.File.Name:title+"\n"+v.File.Name;
  }
  internal static bool Replace(Window owner,ModTarget target,ModReplacement[] changes){
   var dialog=new ThemedDialog(owner,"发现重复模组","「"+target.Name+"」已包含下面的模组。可以备份并覆盖，也可以保留旧文件，继续安装其他可用模组。");
   List(dialog,changes.Select(c=>c.OldFile+"\n→ "+c.NewFile));
   return Confirm(dialog,"备份并覆盖","保留已有，继续");
  }
 }
}
