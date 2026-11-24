using System;using System.IO;using System.Threading;using System.Threading.Tasks;
namespace FoolLauncher {
 internal static class NetworkWait {
  internal static async Task<T> Bounded<T>(Task<T> work,int milliseconds,CancellationToken ct){
   using(var stop=CancellationTokenSource.CreateLinkedTokenSource(ct)){
    var timer=Task.Delay(milliseconds,stop.Token);
    if(await Task.WhenAny(work,timer).ConfigureAwait(false)==work){stop.Cancel();return await work.ConfigureAwait(false);}
    var observe=work.ContinueWith(t=>{if(t.IsFaulted){var ignored=t.Exception;}else if(t.Status==TaskStatus.RanToCompletion){var disposable=t.Result as IDisposable;if(disposable!=null)disposable.Dispose();}},TaskScheduler.Default);
    ct.ThrowIfCancellationRequested();throw new TimeoutException("网络等待超时。");
   }
  }
 }
}
