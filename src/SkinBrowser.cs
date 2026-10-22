using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace FoolLauncher {
 public sealed class SkinBrowser:IDisposable {
  const string Home="https://namemc.com/minecraft-skins";readonly Window Window;readonly string Root;readonly SkinStore Store;readonly Action<string> Status;readonly Action Refresh;
  WebView2 View;bool Initializing,Closed,Downloading,Visible,HasResult;string LastSkin="",PendingUri="",PendingName="";DateTime PendingAt;CoreWebView2DownloadOperation ActiveDownload;
  T Get<T>(string n)where T:class{return Window.FindName(n) as T;}
  public SkinBrowser(Window window,string root,SkinStore store,Action<string> status,Action refresh){Window=window;Root=root;Store=store;Status=status;Refresh=refresh;
   Get<TextBox>("SkinBrowserAddress").Text=Home;Get<Button>("SkinBrowserGo").Click+=(s,e)=>Go(Get<TextBox>("SkinBrowserAddress").Text);
   Get<TextBox>("SkinBrowserAddress").KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Go(Get<TextBox>("SkinBrowserAddress").Text);e.Handled=true;}};
   Get<Button>("SkinBrowserHome").Click+=(s,e)=>Go(Home);Get<Button>("SkinBrowserBack").Click+=(s,e)=>{if(View!=null&&View.CoreWebView2!=null&&View.CanGoBack)View.GoBack();};Get<Button>("SkinBrowserReload").Click+=async(s,e)=>{if(View!=null&&View.CoreWebView2!=null)View.Reload();else await Ensure();};
   Get<Button>("SaveCurrentSkin").Click+=async(s,e)=>await SaveCurrent();Get<Button>("SkinExternalBrowser").Click+=(s,e)=>{string url=View!=null&&View.Source!=null?View.Source.AbsoluteUri:Home;if(IsPage(url))Process.Start(new ProcessStartInfo(url){UseShellExecute=true});};
  }
  public static bool IsPage(string value){Uri u;return Uri.TryCreate(value,UriKind.Absolute,out u)&&u.Scheme=="https"&&String.IsNullOrEmpty(u.UserInfo)&&u.IsDefaultPort&&(u.Host.Equals("namemc.com",StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith(".namemc.com",StringComparison.OrdinalIgnoreCase));}
  public static bool IsSkinImage(string value){Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||!String.IsNullOrEmpty(u.UserInfo)||!u.IsDefaultPort)return false;return (u.Host.Equals("s.namemc.com",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(u.AbsolutePath,@"\A/i/[a-fA-F0-9]{16,64}\.png\z"))||(u.Host.Equals("textures.minecraft.net",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(u.AbsolutePath,@"\A/texture/[a-fA-F0-9]{32,64}\z"));}
  internal static bool IsDownload(string value,string page){if(IsSkinImage(value))return true;if(!IsPage(page)||value==null||!value.StartsWith("blob:",StringComparison.OrdinalIgnoreCase))return false;Uri blob,source;return Uri.TryCreate(value.Substring(5),UriKind.Absolute,out blob)&&Uri.TryCreate(page,UriKind.Absolute,out source)&&IsPage(blob.AbsoluteUri)&&blob.Host.Equals(source.Host,StringComparison.OrdinalIgnoreCase);}
  public async Task Ensure(){
   if(Closed||Initializing||View!=null&&View.CoreWebView2!=null)return;Initializing=true;Get<TextBlock>("SkinBrowserPlaceholder").Visibility=Visibility.Visible;Get<TextBlock>("SkinBrowserPlaceholder").Text=UiText.T("正在打开内置浏览器…");
   try{
    if(View!=null){View.Dispose();Get<Grid>("SkinBrowserHost").Children.Remove(View);}View=new WebView2();Get<Grid>("SkinBrowserHost").Children.Add(View);
    var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Root,"data","skin-browser"));if(Closed)return;await View.EnsureCoreWebView2Async(environment);if(Closed)return;
    var core=View.CoreWebView2;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsStatusBarEnabled=false;core.IsMuted=true;
    core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
    core.NewWindowRequested+=(s,e)=>{e.Handled=true;if(IsPage(e.Uri))Go(e.Uri);else Status(UiText.T("该链接不属于 NameMC，可用外部浏览器打开网站。"));};
    core.NavigationStarting+=(s,e)=>{if(IsSkinImage(e.Uri)&&PendingUri==""){e.Cancel=true;string imageUri=e.Uri;Window.Dispatcher.BeginInvoke(new Action(()=>BeginImage(imageUri,UiText.T("NameMC 皮肤"))));return;}if(!IsPage(e.Uri)&&!IsSkinImage(e.Uri)){e.Cancel=true;Status(UiText.T("该链接不属于 NameMC，可用外部浏览器打开网站。"));return;}if(IsPage(e.Uri)&&Regex.IsMatch(new Uri(e.Uri).AbsolutePath,@"\A/skin/[a-fA-F0-9]{16,64}\z"))LastSkin=e.Uri;};
    core.SourceChanged+=(s,e)=>{Get<TextBox>("SkinBrowserAddress").Text=core.Source;Buttons();};
    core.NavigationCompleted+=(s,e)=>{if(Closed)return;Get<TextBlock>("SkinBrowserPlaceholder").Visibility=Visibility.Collapsed;if(!e.IsSuccess)Status(UiText.T("网页未能加载，请检查网络后刷新，或用外部浏览器打开。"));else if(PendingUri==""&&!Downloading&&!HasResult)Status(UiText.T("打开皮肤详情后，可点击“收藏当前皮肤”或网站下载按钮。"));Buttons();};
    core.WebResourceResponseReceived+=Response;core.DownloadStarting+=Download;core.Navigate(Home);Get<TextBlock>("SkinBrowserPlaceholder").Visibility=Visibility.Collapsed;SetVisible(Visible);Buttons();
   }catch(Exception ex){if(!Closed){if(View!=null){Get<Grid>("SkinBrowserHost").Children.Remove(View);View.Dispose();View=null;}Get<TextBlock>("SkinBrowserPlaceholder").Visibility=Visibility.Visible;Get<TextBlock>("SkinBrowserPlaceholder").Text=UiText.T("内置浏览器不可用。请安装 Microsoft Edge WebView2 Runtime，再点击刷新重试。")+"\n"+ex.Message;Status(UiText.T("本地皮肤收藏仍可使用。"));}}finally{Initializing=false;}
  }
  void Buttons(){bool ready=View!=null&&View.CoreWebView2!=null;Get<Button>("SkinBrowserBack").IsEnabled=ready&&View.CanGoBack;Get<Button>("SaveCurrentSkin").IsEnabled=ready&&!Downloading&&PendingUri=="";}
  public void SetVisible(bool visible){Visible=visible;if(View!=null)View.Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
  public void RefreshLanguage(){Buttons();}
  void Go(string url){if(!IsPage(url)){Status(UiText.T("请输入 HTTPS 的 NameMC 网站或皮肤详情链接。"));return;}if(View==null||View.CoreWebView2==null){Status(UiText.T("浏览器还在准备，请稍后重试。"));return;}HasResult=false;View.CoreWebView2.Navigate(url);}
  async Task SaveCurrent(){
   if(Closed||View==null||View.CoreWebView2==null||Downloading||PendingUri!="")return;
   if(!IsPage(View.CoreWebView2.Source)){Status(UiText.T("请先打开一个 NameMC 皮肤详情页。"));return;}
   try{
    // Only read public download links; no host object or credential bridge is exposed.
    string result=await View.ExecuteScriptAsync(@"(()=>{const links=[...document.querySelectorAll('a[href]')].map(a=>a.href);const url=links.find(u=>/^https:\/\/s\.namemc\.com\/i\/[a-f0-9]{16,64}\.png(?:\?|$)/i.test(u)||/^https:\/\/textures\.minecraft\.net\/texture\/[a-f0-9]{32,64}/i.test(u));return {url:url||'',name:document.title||'NameMC'};})()");
    if(Closed)return;var item=Json.Parse<System.Collections.Generic.Dictionary<string,string>>(result);string uri;if(item==null||!item.TryGetValue("url",out uri)||!IsSkinImage(uri)){Status(UiText.T("未找到皮肤下载链接。请打开皮肤详情，或点击网站自己的下载按钮。"));return;}BeginImage(uri,item.ContainsKey("name")?item["name"]:UiText.T("NameMC 皮肤"));
   }catch(Exception ex){Status(UiText.T("收藏未完成：")+ex.Message);}
  }
  void BeginImage(string uri,string name){if(Downloading||PendingUri!=""||Closed)return;PendingUri=uri;PendingName=name;PendingAt=DateTime.UtcNow;Buttons();Status(UiText.T("正在下载并检查皮肤…"));View.CoreWebView2.Navigate(uri);var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(45)};timer.Tick+=(s,e)=>{timer.Stop();if(!Closed&&PendingUri==uri&&(DateTime.UtcNow-PendingAt).TotalSeconds>=44){PendingUri="";Status(UiText.T("皮肤下载超时，请刷新后重试。"));Buttons();}};timer.Start();}
  async void Response(object sender,CoreWebView2WebResourceResponseReceivedEventArgs e){
   if(Closed||PendingUri==""||e.Request.Uri!=PendingUri||!IsSkinImage(e.Request.Uri))return;if(e.Response.Headers.Contains("Content-Disposition")&&e.Response.Headers.GetHeader("Content-Disposition").IndexOf("attachment",StringComparison.OrdinalIgnoreCase)>=0)return;string uri=PendingUri,name=PendingName;PendingUri="";Downloading=true;HasResult=true;Buttons();
   try{if(e.Response.StatusCode!=200)throw new IOException(UiText.T("皮肤网站返回错误，可能需要先完成网站访问验证。")+" HTTP "+e.Response.StatusCode);using(var input=await e.Response.GetContentAsync()){if(input==null)throw new IOException(UiText.T("网站没有返回皮肤图片。"));byte[] bytes=await ReadBounded(input);if(Closed)return;Save(bytes,name,LastSkin==""?uri:LastSkin);}}
   catch(Exception ex){if(!Closed)Status(UiText.T("收藏未完成：")+ex.Message);}finally{Downloading=false;if(!Closed){Buttons();if(LastSkin!=""&&View!=null&&View.CoreWebView2!=null)View.CoreWebView2.Navigate(LastSkin);}}
  }
  internal static async Task<byte[]> ReadBounded(Stream input){using(var output=new MemoryStream()){var buffer=new byte[8192];int n;while((n=await input.ReadAsync(buffer,0,buffer.Length))>0){if(output.Length+n>SkinStore.MaxBytes)throw new InvalidDataException(UiText.T("皮肤文件超过 1 MB。"));output.Write(buffer,0,n);}return output.ToArray();}}
  void Save(byte[] bytes,string name,string origin){HasResult=true;var saved=Store.Import(bytes,name,origin);Refresh();Status(UiText.T("已加入本地收藏：")+saved.Name);}
  void Download(object sender,CoreWebView2DownloadStartingEventArgs e){
   e.Handled=true;if(Closed||Downloading){e.Cancel=true;return;}
   if(!IsDownload(e.DownloadOperation.Uri,View.CoreWebView2.Source)){
    e.Cancel=true;
    // NameMC may synthesize a data-URI download. Resolve the page's original
    // public texture link through the same validated Save-current workflow.
    Uri page;if(IsPage(View.CoreWebView2.Source)&&Uri.TryCreate(View.CoreWebView2.Source,UriKind.Absolute,out page)&&Regex.IsMatch(page.AbsolutePath,@"\A/skin/[a-fA-F0-9]{16,64}\z"))Window.Dispatcher.BeginInvoke(new Action(async()=>await SaveCurrent()));
    else Status(UiText.T("这里只接收皮肤 PNG 下载。"));return;
   }
   Downloading=true;HasResult=true;PendingUri="";Buttons();var operation=e.DownloadOperation;ActiveDownload=operation;string folder=Path.Combine(Root,"data","skin-downloads");Directory.CreateDirectory(folder);string temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".download");e.ResultFilePath=temp;string name=View.CoreWebView2.DocumentTitle,origin=LastSkin;Status(UiText.T("正在下载并检查皮肤…"));
   operation.BytesReceivedChanged+=(s,args)=>{if(operation.BytesReceived>SkinStore.MaxBytes)operation.Cancel();};
   EventHandler<object> changed=null;changed=(s,args)=>{if(operation.State==CoreWebView2DownloadState.InProgress)return;operation.StateChanged-=changed;try{if(!Closed){if(operation.State==CoreWebView2DownloadState.Completed){var info=new FileInfo(temp);if(info.Length>SkinStore.MaxBytes)throw new IOException(UiText.T("皮肤文件超过 1 MB。"));Save(File.ReadAllBytes(temp),name,origin);}else Status(UiText.T("皮肤下载未完成，请重试。"));}}catch(Exception ex){if(!Closed)Status(UiText.T("收藏未完成：")+ex.Message);}finally{Downloading=false;ActiveDownload=null;try{if(File.Exists(temp))File.Delete(temp);}catch{}if(!Closed)Buttons();}};operation.StateChanged+=changed;
  }
  public void Dispose(){Closed=true;try{if(ActiveDownload!=null)ActiveDownload.Cancel();}catch{}if(View!=null)View.Dispose();}
 }
}
