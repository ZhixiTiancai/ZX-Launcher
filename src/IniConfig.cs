using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace FoolLauncher {
 public static class IniConfig {
  public static string StringValue(string value) {
   if(value.StartsWith("@"))value="@"+value;
   return "\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
  }
  public static string[] GeneralLines(IEnumerable<string> original,IDictionary<string,string> settings) {
   var keys=new HashSet<string>(settings.Keys,StringComparer.OrdinalIgnoreCase);
   // Remove obsolete misplaced copies from 0.1.0, while preserving unrelated sections.
   var lines=original.Where(line=>{int equals=line.IndexOf('=');return equals<=0||!keys.Contains(line.Substring(0,equals).Trim());}).ToList();
   int start=lines.FindIndex(l=>l.Trim().Equals("[General]",StringComparison.OrdinalIgnoreCase));
   if(start<0){lines.Insert(0,"[General]");start=0;}
   int end=lines.FindIndex(start+1,l=>l.TrimStart().StartsWith("["));if(end<0)end=lines.Count;
   lines.InsertRange(end,settings.Select(k=>k.Key+"="+k.Value));return lines.ToArray();
  }
  public static void UpdateGeneral(string file,IDictionary<string,string> settings) {
   var original=File.Exists(file)?File.ReadAllLines(file):new string[0];
   File.WriteAllLines(file,GeneralLines(original,settings),new UTF8Encoding(false));
  }
 }
}
