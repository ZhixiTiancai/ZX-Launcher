using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
namespace FoolLauncher {
 public sealed class AccountWindow {
  readonly AccountStore Store;readonly Window Dialog;readonly Action Changed;readonly Func<bool> EngineRunning;
  ListBox List;TextBlock Current,Status,Code;TextBox OfflineName,ClientId;FrameworkElement LoginPanel;Button Begin,Refresh,Use,Offline,AddOffline,Remove,SignOut,SaveConfig,OpenPage,CopyCode;CancellationTokenSource LoginCancel;bool Working;DeviceLogin Device;
  public AccountWindow(Window owner,AccountStore store,Func<bool> engineRunning,Action changed) {
   Store=store;Changed=changed;EngineRunning=engineRunning;Dialog=(Window)XamlReader.Parse(Markup);Dialog.Owner=owner;
   List=Get<ListBox>("Accounts");Current=Get<TextBlock>("CurrentAccount");Status=Get<TextBlock>("Status");Code=Get<TextBlock>("DeviceCode");OfflineName=Get<TextBox>("OfflineName");ClientId=Get<TextBox>("ClientId");LoginPanel=Get<FrameworkElement>("LoginPanel");
   Begin=Get<Button>("Begin");Refresh=Get<Button>("Refresh");Use=Get<Button>("Use");Offline=Get<Button>("Offline");AddOffline=Get<Button>("AddOffline");Remove=Get<Button>("Remove");SignOut=Get<Button>("SignOut");SaveConfig=Get<Button>("SaveConfig");OpenPage=Get<Button>("OpenPage");CopyCode=Get<Button>("CopyCode");
   ClientId.Text=Store.Settings().ClientId;List.SelectionChanged+=(s,e)=>Enabled();
   Use.Click+=(s,e)=>Attempt(()=>{var account=Selected;if(account==null)return;Store.Select(account.Id,account.Type=="Offline");Reload(account.Id);Status.Text=UiText.T("已切换到 ")+account.Name+UiText.T("，启动游戏会使用此账号。");});
   Offline.Click+=(s,e)=>Attempt(()=>{var account=Selected;if(account==null)return;Store.Select(account.Id,true);Reload(account.Id);Status.Text="已切换到离线模式。可用于本机单人游戏；启动时仍遵循原版引擎的游戏资格检查。";});
   AddOffline.Click+=(s,e)=>Attempt(()=>{var account=Store.AddOffline(OfflineName.Text);OfflineName.Clear();Reload(account.Id);Status.Text="离线角色已添加。离线使用需要保留一个已验证拥有 Java 版的微软账号。";});
   SignOut.Click+=(s,e)=>Attempt(()=>{Store.SignOut();Reload(null);Status.Text=UiText.T("已退出当前使用状态，账号仍保存在列表中。");});
   Remove.Click+=(s,e)=>Attempt(()=>{var account=Selected;if(account==null)return;if(MessageBox.Show(Dialog,"从此电脑移除“"+account.Name+"”的已保存账号？\n不会删除微软账号、整合包或存档。",UiText.T("移除已保存账号"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;Store.Remove(account.Id);Reload(null);Status.Text=UiText.T("已移除该账号的本地登录记录。");});
   SaveConfig.Click+=(s,e)=>Attempt(()=>{Store.SetClientId(ClientId.Text);Status.Text=String.IsNullOrWhiteSpace(ClientId.Text)?"已清除应用编号，新增微软登录处于待配置状态。":"应用编号已保存，可以点击“登录 / 添加微软账号”。";});
   Get<Button>("RegistrationHelp").Click+=(s,e)=>Attempt(()=>Process.Start(new ProcessStartInfo("https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app"){UseShellExecute=true}));
   Begin.Click+=async(s,e)=>await Login(false);Refresh.Click+=async(s,e)=>await Login(true);
   OpenPage.Click+=(s,e)=>Attempt(()=>{if(Device!=null)Process.Start(new ProcessStartInfo(Device.VerificationUrl){UseShellExecute=true});});
   CopyCode.Click+=(s,e)=>Attempt(()=>{if(Device!=null)Clipboard.SetText(Device.UserCode);});
   Get<Button>("CancelLogin").Click+=(s,e)=>{if(LoginCancel!=null)LoginCancel.Cancel();};
   Get<Button>("Close").Click+=(s,e)=>Dialog.Close();Dialog.Closing+=(s,e)=>{if(LoginCancel!=null)LoginCancel.Cancel();};
   Reload(null);
  }
  T Get<T>(string name) where T:class{return Dialog.FindName(name) as T;}
  AccountChoice Selected{get{return List.SelectedItem as AccountChoice;}}
  void Reload(string id) {
   var selected=Store.Current();var settings=Store.Settings();var items=Store.List();foreach(var item in items)item.Active=selected!=null&&item.Id==selected.Id;
   List.ItemsSource=items;List.SelectedItem=items.FirstOrDefault(a=>a.Id==id)??items.FirstOrDefault(a=>selected!=null&&a.Id==selected.Id)??items.FirstOrDefault();
   Current.Text=selected==null?UiText.T("当前：未选择账号"):UiText.T("当前：")+selected.Name+"  ·  "+(Store.Mode()=="offline"?UiText.T("离线模式"):UiText.T("微软账号（启动时验证）"));
   if(!Working)Status.Text=String.IsNullOrWhiteSpace(settings.ClientId)?UiText.T("现有账号可直接选择使用。新增微软登录待配置应用 Client ID，可展开下方“登录配置”。"):UiText.T("选择已保存的账号，或登录并添加新的微软账号。");
   Enabled();Changed();
  }
  void Enabled(){bool available=!Working&&!EngineRunning(),selected=Selected!=null;List.IsEnabled=ClientId.IsEnabled=OfflineName.IsEnabled=available;Begin.IsEnabled=AddOffline.IsEnabled=SaveConfig.IsEnabled=available;Use.IsEnabled=Offline.IsEnabled=Remove.IsEnabled=available&&selected;Refresh.IsEnabled=available&&selected&&Selected.Type=="MSA";SignOut.IsEnabled=available&&Store.Current()!=null;}
  void Attempt(Action action){try{action();}catch(Exception ex){Status.Text=ex.Message;}}
  async Task Login(bool refresh) {
   if(Working)return;var account=Selected;if(EngineRunning()){Status.Text=UiText.T("请先退出 Prism 和游戏，再登录或刷新账号。");return;}
   try{Store.SetClientId(ClientId.Text);Guid configured;if(!Guid.TryParse(Store.Settings().ClientId,out configured)){Get<Expander>("Config").IsExpanded=true;Status.Text="新增微软登录需要自己的应用 Client ID。它位于 Entra → 应用注册 → 你的应用 → 概述。";return;}}
   catch(Exception ex){Status.Text=ex.Message;return;}
   Working=true;LoginCancel=new CancellationTokenSource();var cancel=LoginCancel;Enabled();
   try {
    using(var auth=new MicrosoftLogin()) {
     var progress=new Progress<string>(text=>{if(Dialog.IsVisible)Status.Text=text;});System.Collections.Generic.Dictionary<string,object> record;
     if(refresh){if(account==null)return;Status.Text=UiText.T("正在刷新账号…");record=await auth.Refresh(Store.Settings().ClientId,account,progress,cancel.Token);}
     else {
      Status.Text=UiText.T("正在向微软申请登录代码…");Device=await auth.Begin(Store.Settings().ClientId,cancel.Token);cancel.Token.ThrowIfCancellationRequested();
      Code.Text=Device.UserCode;LoginPanel.Visibility=Visibility.Visible;Status.Text=UiText.T("点击“打开微软登录网页”，在官方网页输入上方代码并完成授权。完成后此窗口会自动更新。");
      record=await auth.Complete(Store.Settings().ClientId,Device,progress,cancel.Token);
     }
     cancel.Token.ThrowIfCancellationRequested();Store.SaveMicrosoft(record);string id=AccountStore.Text(AccountStore.Object(record,"profile"),"id");Reload(id);Status.Text=UiText.T("登录成功，已选中账号：")+AccountStore.Text(AccountStore.Object(record,"profile"),"name")+"。";
    }
   }catch(OperationCanceledException){if(Dialog.IsVisible)Status.Text=UiText.T("登录已取消，原有账号保持不变。");}
   catch(Exception ex){if(Dialog.IsVisible)Status.Text=ex.Message;}
   finally{Working=false;Device=null;Code.Text="";LoginPanel.Visibility=Visibility.Collapsed;LoginCancel=null;cancel.Dispose();if(Dialog.IsVisible)Enabled();Changed();}
  }
  public void Show(){Dialog.ShowDialog();}
  const string Markup=@"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Title='{DynamicResource Text.62}' Width='800' Height='720' MinWidth='700' MinHeight='620' WindowStartupLocation='CenterOwner' Background='{DynamicResource Brush.Base}' Foreground='{DynamicResource Brush.Text}' FontFamily='Microsoft YaHei UI' FontSize='14'>
 <Window.Resources>
  <Style TargetType='Button'><Setter Property='Padding' Value='13,9'/><Setter Property='Margin' Value='0,0,8,8'/><Setter Property='Background' Value='{DynamicResource Brush.Button}'/><Setter Property='Foreground' Value='{DynamicResource Brush.Text}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/></Style>
  <Style TargetType='TextBox'><Setter Property='Background' Value='{DynamicResource Brush.Editor}'/><Setter Property='Foreground' Value='{DynamicResource Brush.Text}'/><Setter Property='CaretBrush' Value='{DynamicResource Brush.Text}'/><Setter Property='Padding' Value='8,6'/><Setter Property='BorderBrush' Value='{DynamicResource Brush.Selection}'/></Style>
  <Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style>
  <Style TargetType='ListBoxItem'><Setter Property='Padding' Value='12,10'/><Setter Property='Foreground' Value='{DynamicResource Brush.Text}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Row' Background='Transparent' Padding='{TemplateBinding Padding}' CornerRadius='6'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Brush.Selection}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Brush.Icon}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 </Window.Resources>
 <Grid Margin='24'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
 <StackPanel Margin='0,0,0,16'><TextBlock Text='{DynamicResource Text.63}' FontSize='25' FontWeight='SemiBold'/><TextBlock x:Name='CurrentAccount' Margin='0,8,0,0' Foreground='{DynamicResource Brush.Accent}'/></StackPanel>
 <ScrollViewer Grid.Row='1' VerticalScrollBarVisibility='Auto'><StackPanel>
 <TextBlock Text='{DynamicResource Text.64}' FontWeight='SemiBold' Margin='0,0,0,8'/><ListBox x:Name='Accounts' Height='160' Background='{DynamicResource Brush.Panel}' BorderThickness='0' ScrollViewer.HorizontalScrollBarVisibility='Disabled'/>
 <WrapPanel Margin='0,12,0,0'><Button x:Name='Use' Content='{DynamicResource Text.65}' Background='{DynamicResource Brush.Accent}' Foreground='{DynamicResource Brush.AccentText}'/><Button x:Name='Offline' Content='{DynamicResource Text.66}'/><Button x:Name='Refresh' Content='{DynamicResource Text.67}'/><Button x:Name='SignOut' Content='{DynamicResource Text.68}'/><Button x:Name='Remove' Content='{DynamicResource Text.69}'/></WrapPanel>
 <Button x:Name='Begin' Content='{DynamicResource Text.70}' HorizontalAlignment='Left' Background='{DynamicResource Brush.Selection}'/>
 <Border x:Name='LoginPanel' Visibility='Collapsed' Background='{DynamicResource Brush.Hero}' Padding='14' Margin='0,4,0,12'><StackPanel><TextBlock Text='{DynamicResource Text.71}' Foreground='{DynamicResource Brush.Muted}'/><TextBlock x:Name='DeviceCode' FontSize='28' FontWeight='Bold' Margin='0,6'/><WrapPanel><Button x:Name='OpenPage' Content='{DynamicResource Text.72}'/><Button x:Name='CopyCode' Content='{DynamicResource Text.73}'/><Button x:Name='CancelLogin' Content='{DynamicResource Text.74}'/></WrapPanel><TextBlock Text='{DynamicResource Text.75}' Foreground='{DynamicResource Brush.Muted}' FontSize='12'/></StackPanel></Border>
 <Grid Margin='0,8,0,6'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='OfflineName' MaxLength='16' ToolTip='{DynamicResource Text.76}' AutomationProperties.Name='{DynamicResource Text.77}'/><Button x:Name='AddOffline' Grid.Column='1' Content='{DynamicResource Text.78}' Margin='8,0,0,0'/></Grid>
 <TextBlock Text='{DynamicResource Text.79}' Foreground='{DynamicResource Brush.Muted}' FontSize='12' Margin='0,0,0,14'/>
 <Expander x:Name='Config' Header='{DynamicResource Text.80}' Foreground='{DynamicResource Brush.Text}'><StackPanel Margin='0,10,0,0'><TextBlock Text='{DynamicResource Text.81}' Foreground='{DynamicResource Brush.Muted}' FontSize='12' Margin='0,0,0,8'/><TextBox x:Name='ClientId' MaxLength='36' AutomationProperties.Name='{DynamicResource Text.82}'/><WrapPanel Margin='0,8,0,0'><Button x:Name='SaveConfig' Content='{DynamicResource Text.83}'/><Button x:Name='RegistrationHelp' Content='{DynamicResource Text.84}'/></WrapPanel></StackPanel></Expander>
 </StackPanel></ScrollViewer>
 <Grid Grid.Row='2' Margin='0,15,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBlock x:Name='Status' Foreground='{DynamicResource Brush.Muted}' FontSize='12' Margin='0,0,16,0' VerticalAlignment='Center'/><Button x:Name='Close' Grid.Column='1' Content='{DynamicResource Text.22}' Margin='0' VerticalAlignment='Bottom'/></Grid>
 </Grid></Window>";
 }
}
