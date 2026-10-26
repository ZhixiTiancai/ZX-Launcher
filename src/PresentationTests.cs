using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace FoolLauncher {
 public static class PresentationTests {
  static void Assert(bool value,string message){if(!value)throw new Exception(message);}
  public static void Run(string parent){
   string folder=Path.Combine(parent,"names-and-shortcuts");Directory.CreateDirectory(folder);string oldLanguage=UiText.Language;
   try{
    var names=new ModNameTranslations(folder);var project=new CatalogProject{Source="modrinth",Kind="mod",Slug="sodium",Id="AANobbMI",Name="Sodium",Summary="Original description stays English."};int notified=0;project.PropertyChanged+=(s,e)=>{if(e.PropertyName=="Name")notified++;};UiText.Language="zh-CN";names.Apply(new[]{project});Assert(project.Name=="钠"&&project.OriginalName=="Sodium"&&project.Summary=="Original description stays English."&&project.Id=="AANobbMI"&&notified>0,"Name localization changed non-name metadata or failed notification");
    UiText.Language="en-US";Assert(project.Name=="Sodium","English interface did not restore original name");UiText.Language="zh-CN";
    var pack=new CatalogProject{Source="modrinth",Kind="modpack",Slug="sodium",Name="Pack name"};var unknown=new CatalogProject{Source="curseforge",Kind="mod",Slug="unknown-fixture",Name="Unmapped title"};names.Apply(new[]{pack,unknown});Assert(pack.Name=="Pack name"&&unknown.Name=="Unmapped title","Modpack names or unknown projects were incorrectly translated");
    string data="cf-only|甲模组 (Alpha)\nboth@|乙模组*\ncf-old@mr-new|丙模组 (Gamma)\n@mr-only|丁模组\nunsafe@|<bad>\nmetadata@|不应作为名称读取";var parsed=ModNameTranslations.Parse(data);Assert(parsed["curseforge:cf-only"]=="甲模组"&&!parsed.ContainsKey("modrinth:cf-only")&&parsed["modrinth:both"]=="乙模组"&&parsed["modrinth:mr-new"]=="丙模组"&&parsed["curseforge:cf-old"]=="丙模组"&&parsed["modrinth:mr-only"]=="丁模组"&&!parsed.ContainsKey("curseforge:mr-only")&&!parsed.ContainsKey("modrinth:metadata"),"Name table platform mapping/English suffix/tail handling failed");
    string cache=Path.Combine(folder,"metadata","mod-names-v1.json");Json.Write(cache,new ModNameTranslations.Cache{Format=1,Source=ModNameTranslations.SourceUrl,UpdatedUtc=DateTime.UtcNow.ToString("o"),Names=parsed});var cached=new ModNameTranslations(folder);Assert(cached.Lookup("modrinth","mr-new")=="丙模组","Offline name cache did not reload");File.WriteAllText(cache,"broken cache");Assert(new ModNameTranslations(folder).Lookup("modrinth","sodium")=="钠","Corrupt cache blocked common name fallback");
    byte[] icon;using(var s=typeof(ShortcutIcons).Assembly.GetManifestResourceStream("Zhixi.LauncherIcon.ico"))using(var m=new MemoryStream()){s.CopyTo(m);icon=m.ToArray();}
    string root=Path.Combine(folder,"portable"),entry=Path.Combine(root,PortableLayout.EntryName);Directory.CreateDirectory(root);File.WriteAllText(entry,"synthetic executable; never run");string ico=ShortcutIcons.SaveIcon(root,icon);Assert(File.Exists(ico)&&ico==ShortcutIcons.SaveIcon(root,icon),"Versioned shortcut icon not persisted/idempotent");
    string link=Path.Combine(root,"test.lnk");CreateLink(link,entry,root,"--fixture \"keep arguments\"");Assert(ShortcutIcons.Repair(link,entry,ico,Path.Combine(root,"backups")),"Matching launcher shortcut was not updated");
    object com=new ShortcutIcons.ShellLink();try{((IPersistFile)com).Load(link,0);var shell=(ShortcutIcons.IShellLinkW)com;var text=new StringBuilder(32768);int index;shell.GetIconLocation(text,text.Capacity,out index);Assert(text.ToString()==ico&&index==0,"Shortcut icon reference was not updated");text.Clear();shell.GetPath(text,text.Capacity,IntPtr.Zero,4);Assert(text.ToString()==entry,"Shortcut target was changed");text.Clear();shell.GetWorkingDirectory(text,text.Capacity);Assert(text.ToString()==root,"Shortcut working directory changed");text.Clear();shell.GetArguments(text,text.Capacity);Assert(text.ToString()=="--fixture \"keep arguments\"","Shortcut arguments changed");}finally{Marshal.FinalReleaseComObject(com);}
    Assert(!ShortcutIcons.Repair(link,entry,ico,Path.Combine(root,"backups")),"Unchanged shortcut was rewritten");string unrelated=Path.Combine(root,"other.lnk");CreateLink(unrelated,Path.Combine(root,"other.exe"),root,"");string hash=LauncherUpdateCore.HashFile(unrelated);Assert(!ShortcutIcons.Repair(unrelated,entry,ico,Path.Combine(root,"backups"))&&LauncherUpdateCore.HashFile(unrelated)==hash,"Unrelated shortcut changed");
    string organized=Path.Combine(PortableLayout.ProgramFolder(root),"shortcuts");Directory.CreateDirectory(organized);File.WriteAllText(Path.Combine(organized,"test.lnk"),"existing shortcut must survive");string linkHash=LauncherUpdateCore.HashFile(link);PortableLayout.Prepare(root);Assert(File.ReadAllText(Path.Combine(organized,"test.lnk"))=="existing shortcut must survive"&&Array.Exists(Directory.GetFiles(organized,"*.lnk"),p=>LauncherUpdateCore.HashFile(p)==linkHash)&&Directory.GetFiles(root).Length==1,"Shortcut organization overwrote a same-name link or cluttered root");
   }finally{UiText.Language=oldLanguage;}
   Console.WriteLine("PASS: names-only Chinese display, original metadata/English/unknown/modpack preservation, platform aliases and offline/corrupt cache; matching-shortcut icon repair with backup, argument/target/workdir preservation, idempotence and unrelated-link protection.");
  }
  static void CreateLink(string path,string target,string work,string args){object com=new ShortcutIcons.ShellLink();try{var link=(ShortcutIcons.IShellLinkW)com;link.SetPath(target);link.SetWorkingDirectory(work);link.SetArguments(args);((IPersistFile)com).Save(path,true);}finally{Marshal.FinalReleaseComObject(com);}}
 }
}
