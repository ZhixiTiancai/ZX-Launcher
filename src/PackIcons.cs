using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FoolLauncher {
 public sealed class PackIcons {
  const int MaxBytes=16*1024*1024;
  readonly string DirectoryPath;
  static readonly HttpClient Http=CreateHttp();
  static readonly Lazy<ImageSource> Default=new Lazy<ImageSource>(LoadDefault);
  public PackIcons(string root){DirectoryPath=Path.Combine(root,"data","instance-icons");}
  static HttpClient CreateHttp() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=Timeout.InfiniteTimeSpan};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("ZhixiLauncher/0.1.5");return client;
  }
  string FileFor(Instance instance){return PackService.SafePath(DirectoryPath,instance.Id+".png");}
  static ImageSource LoadDefault() {
   using(var stream=typeof(PackIcons).Assembly.GetManifestResourceStream("FoolLauncher.DefaultGrass.png")) {
    if(stream==null)throw new IOException("缺少默认草方块图标。");
    var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
   }
  }
  public ImageSource Load(Instance instance) {
   try{string file=FileFor(instance);if(File.Exists(file))return Decode(ReadLocal(file));}catch{}
   return Default.Value;
  }
  static byte[] ReadLocal(string file) {
   using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(stream.Length==0||stream.Length>MaxBytes)throw new IOException("图片大小须在 16 MB 以内。");
    var bytes=new byte[(int)stream.Length];int total=0,n;
    while(total<bytes.Length&&(n=stream.Read(bytes,total,bytes.Length-total))>0)total+=n;
    if(total!=bytes.Length)throw new IOException("图片读取不完整，请重试。");return bytes;
   }
  }
  static BitmapSource Decode(byte[] bytes) {
   try {
    if(bytes.Length==0||bytes.Length>MaxBytes)throw new InvalidDataException();
    int width,height;
    using(var stream=new MemoryStream(bytes,false)) {
     var frame=BitmapFrame.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);width=frame.PixelWidth;height=frame.PixelHeight;
     if(width<1||height<1||width>16384||height>16384||(long)width*height>32000000)throw new InvalidDataException();
    }
    using(var stream=new MemoryStream(bytes,false)) {
     // Each load owns its stream; URI caching is not used (IgnoreImageCache
     // with a null URI throws on .NET Framework's WPF implementation).
     var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
     if(width>=height)bitmap.DecodePixelWidth=Math.Min(width,256);else bitmap.DecodePixelHeight=Math.Min(height,256);
     bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
   }catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new InvalidDataException("无法读取图片。请选择 PNG、JPG、GIF、BMP 或 ICO，且图片不超过 16 MB / 3200 万像素。",ex);}
  }
  void Save(Instance instance,byte[] bytes,CancellationToken ct) {
   var bitmap=Decode(bytes);ct.ThrowIfCancellationRequested();
   string file=FileFor(instance);Directory.CreateDirectory(DirectoryPath);string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(temp))encoder.Save(output);
    ct.ThrowIfCancellationRequested();if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public Task SetLocal(Instance instance,string file,CancellationToken ct) {return Task.Run(()=>{ct.ThrowIfCancellationRequested();Save(instance,ReadLocal(file),ct);},ct);}
  internal void SetBytes(Instance instance,byte[] bytes,CancellationToken ct){Save(instance,bytes,ct);}
  internal byte[] CustomBytes(Instance instance){string file=FileFor(instance);if(!File.Exists(file))return null;byte[] bytes=ReadLocal(file);Decode(bytes);return bytes;}
  static Uri ImageUri(string value) {
   Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("请粘贴 HTTPS 图片直链，例如以 .png 或 .jpg 结尾的图片地址。");return uri;
  }
  public async Task Download(Instance instance,string url,CancellationToken ct) {
   Uri uri=ImageUri((url??"").Trim());
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    try {
     for(int redirects=0;redirects<=5;redirects++)using(var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)) {
      int status=(int)response.StatusCode;
      if(status>=300&&status<=399) {
       if(response.Headers.Location==null||redirects==5)throw new IOException("图片链接重定向次数过多或地址无效。");
       uri=ImageUri(new Uri(uri,response.Headers.Location).AbsoluteUri);continue;
      }
      if(!response.IsSuccessStatusCode)throw new IOException("图标下载失败（HTTP "+status+"）。请检查图片链接后重试。");
      if(response.Content.Headers.ContentLength>MaxBytes)throw new IOException("图片超过 16 MB，请选择较小的图片。");
      if(response.Content.Headers.ContentType!=null&&response.Content.Headers.ContentType.MediaType=="text/html")throw new IOException("此链接返回网页，请使用图片直链。");
      using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var bytes=new MemoryStream()) {
       var buffer=new byte[81920];int n;
       while((n=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){if(bytes.Length+n>MaxBytes)throw new IOException("图片超过 16 MB，请选择较小的图片。");bytes.Write(buffer,0,n);}
       Save(instance,bytes.ToArray(),deadline.Token);return;
      }
     }
    }catch(OperationCanceledException){if(ct.IsCancellationRequested)throw;throw new IOException("图标下载超时，请检查网络后重试。");}
   }
  }
  public void Reset(Instance instance){string file=FileFor(instance);if(File.Exists(file))File.Delete(file);}
 }
}
