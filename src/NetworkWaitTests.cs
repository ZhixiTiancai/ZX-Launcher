using System;using System.Threading;using System.Threading.Tasks;
namespace FoolLauncher {static class NetworkWaitTests {
 sealed class Late:IDisposable{public bool Disposed;public void Dispose(){Disposed=true;}}
 public static void Run(){
  bool timeout=false;try{NetworkWait.Bounded(new TaskCompletionSource<int>().Task,25,CancellationToken.None).GetAwaiter().GetResult();}catch(TimeoutException){timeout=true;}if(!timeout)throw new Exception("Stalled network operation was not bounded");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{NetworkWait.Bounded(new TaskCompletionSource<int>().Task,10000,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}if(!stopped)throw new Exception("Network wait ignored cancellation");}
  var source=new TaskCompletionSource<Late>();try{NetworkWait.Bounded(source.Task,25,CancellationToken.None).GetAwaiter().GetResult();}catch(TimeoutException){}
  var late=new Late();source.SetResult(late);if(!SpinWait.SpinUntil(()=>late.Disposed,2000))throw new Exception("Late network resource leaked");
  if(NetworkWait.Bounded(Task.FromResult(42),500,CancellationToken.None).GetAwaiter().GetResult()!=42)throw new Exception("Successful network result lost");
  Console.WriteLine("PASS: stalled network deadline, user cancellation, late-resource cleanup and successful-result preservation.");
 }
}}
