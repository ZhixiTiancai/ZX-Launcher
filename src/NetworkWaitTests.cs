using System;using System.IO;using System.Net;using System.Net.Http;using System.Security.Cryptography;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
namespace FoolLauncher {static class NetworkWaitTests {
 sealed class Late:IDisposable{public bool Disposed;public void Dispose(){Disposed=true;}}
 sealed class Slow:HttpMessageHandler{
  public int Calls;public bool Fail;public string Last;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Last=request.RequestUri.Host;await Task.Delay(Fail?1:1250,ct);return new HttpResponseMessage(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})};}
 }
 static void DownloadCases(){
  string root=Path.Combine(Environment.CurrentDirectory,"build","manual-retry-"+Guid.NewGuid().ToString("N"));var handler=new Slow();
  using(var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan}){
   var service=new PackService(root,client);string hash;using(var sha=SHA512.Create())hash=BitConverter.ToString(sha.ComputeHash(new byte[]{1,2,3})).Replace("-","").ToLowerInvariant();
   var file=new PackFile{path="mods/test.jar",fileSize=3,hashes=new Dictionary<string,string>{{"sha512",hash}},downloads=new[]{"https://mediafilez.forgecdn.net/files/fixture.jar"}};
   string dest=Path.Combine(root,"test.jar");
   service.Download(file,dest,CancellationToken.None,1).GetAwaiter().GetResult();
   if(handler.Calls!=1||!PackService.Verify(dest,file))throw new Exception("Slow download was restarted by elapsed time");
   handler.Fail=true;bool failed=false;try{service.Download(file,dest,CancellationToken.None,1).GetAwaiter().GetResult();}catch(IOException){failed=true;}
   if(!failed||handler.Calls!=2||!PackService.Verify(dest,file))throw new Exception("Failed request retried automatically or changed a verified file");
   service.NextDownloadSource();try{service.Download(file,dest,CancellationToken.None,1).GetAwaiter().GetResult();}catch(IOException){}
   if(handler.Calls!=3||handler.Last!="media.forgecdn.net")throw new Exception("Explicit retry did not choose the alternate source");
  }
  Console.WriteLine("PASS: slow download survives former deadline, failed request never retries automatically, verified file preserved, explicit source retry.");
 }
 public static void Run(){
  bool timeout=false;try{NetworkWait.Bounded(new TaskCompletionSource<int>().Task,25,CancellationToken.None).GetAwaiter().GetResult();}catch(TimeoutException){timeout=true;}if(!timeout)throw new Exception("Stalled network operation was not bounded");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{NetworkWait.Bounded(new TaskCompletionSource<int>().Task,10000,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}if(!stopped)throw new Exception("Network wait ignored cancellation");}
  var source=new TaskCompletionSource<Late>();try{NetworkWait.Bounded(source.Task,25,CancellationToken.None).GetAwaiter().GetResult();}catch(TimeoutException){}
  var late=new Late();source.SetResult(late);if(!SpinWait.SpinUntil(()=>late.Disposed,2000))throw new Exception("Late network resource leaked");
  if(NetworkWait.Bounded(Task.FromResult(42),500,CancellationToken.None).GetAwaiter().GetResult()!=42)throw new Exception("Successful network result lost");
  Console.WriteLine("PASS: stalled network deadline, user cancellation, late-resource cleanup and successful-result preservation.");
  DownloadCases();
 }
}}
