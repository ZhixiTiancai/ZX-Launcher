using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace FoolLauncher {
 public static class DeleteConfirmation {
  // This dialog only returns a decision. The caller retains its existing deletion checks.
  public static bool Show(Window owner,string title,string name,string description,string recovery) {
   var dialog=new Window {
    Owner=owner,Title=title,Width=570,SizeToContent=SizeToContent.Height,
    WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
    ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,
    WindowStartupLocation=WindowStartupLocation.CenterOwner,
    FontFamily=owner.FontFamily,FontSize=14
   };
   foreach(string key in new[]{"Panel","Hero","Button","Selection","Text","Muted","Accent","Danger"}) {
    var brush=((SolidColorBrush)owner.FindResource("Brush."+key)).Clone();
    var color=brush.Color;color.A=255;brush.Color=color;brush.Freeze();
    dialog.Resources["Brush."+key]=brush;
   }
   dialog.Resources[typeof(Button)]=owner.FindResource(typeof(Button));
   dialog.SetResourceReference(Control.ForegroundProperty,"Brush.Text");
   var card=(Border)XamlReader.Parse(@"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 Margin='18' CornerRadius='16' Background='{DynamicResource Brush.Panel}'
 BorderBrush='{DynamicResource Brush.Selection}' BorderThickness='1'>
 <Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='5' Opacity='0.28'/></Border.Effect>
 <StackPanel Margin='28,24,28,24'>
  <Grid Margin='0,0,0,22'>
   <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
   <Border Width='44' Height='44' CornerRadius='12' Background='{DynamicResource Brush.Hero}' Margin='0,0,14,0'>
    <Path Width='19' Height='22' Stretch='Uniform' Stroke='{DynamicResource Brush.Danger}' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
     Data='M 1,5 L 19,5 M 7,5 L 7,2 L 13,2 L 13,5 M 3,5 L 4,22 L 16,22 L 17,5 M 8,9 L 8,18 M 12,9 L 12,18'/>
   </Border>
   <StackPanel x:Name='DragArea' Grid.Column='1' VerticalAlignment='Center'>
    <TextBlock x:Name='Heading' FontSize='23' FontWeight='SemiBold'/>
    <TextBlock x:Name='Subtitle' FontSize='12' Foreground='{DynamicResource Brush.Muted}' Margin='0,5,0,0'/>
   </StackPanel>
   <Button x:Name='CloseButton' Grid.Column='2' Width='30' Height='30' Padding='9' Margin='12,0,0,0' Background='Transparent' VerticalAlignment='Top'>
    <Path Data='M 1,1 L 11,11 M 1,11 L 11,1' Stroke='{DynamicResource Brush.Muted}' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
   </Button>
  </Grid>
  <Border Background='{DynamicResource Brush.Hero}' CornerRadius='9' Padding='16,13' Margin='0,0,0,16'>
   <TextBlock x:Name='ItemName' FontSize='17' FontWeight='SemiBold' TextWrapping='Wrap' MaxHeight='110'/>
  </Border>
  <TextBlock x:Name='Description' TextWrapping='Wrap' LineHeight='23'/>
  <TextBlock x:Name='Recovery' TextWrapping='Wrap' FontSize='12' LineHeight='20' Foreground='{DynamicResource Brush.Muted}' Margin='0,10,0,0'/>
  <Border Height='1' Background='{DynamicResource Brush.Selection}' Margin='0,23,0,18'/>
  <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
   <Button x:Name='CancelButton' IsDefault='True' IsCancel='True' Padding='23,11' Margin='0,0,10,0'/>
   <Button x:Name='ConfirmButton' Padding='21,11' Margin='0' Background='{DynamicResource Brush.Danger}' Foreground='{DynamicResource Brush.Panel}' FontWeight='SemiBold'/>
  </StackPanel>
 </StackPanel>
</Border>");
   Func<string,TextBlock> text=n=>(TextBlock)card.FindName(n);
   text("Heading").Text=title;text("Subtitle").Text=UiText.T("请确认要移除的内容");
   text("ItemName").Text=name;text("ItemName").ToolTip=name;
   text("Description").Text=description;text("Recovery").Text=recovery;
   var cancel=(Button)card.FindName("CancelButton");cancel.Content=UiText.T("取消");
   var confirm=(Button)card.FindName("ConfirmButton");confirm.Content=UiText.T("移入回收站");
   var close=(Button)card.FindName("CloseButton");close.ToolTip=UiText.T("取消");
   System.Windows.Automation.AutomationProperties.SetName(close,UiText.T("取消"));
   cancel.Click+=(s,e)=>dialog.DialogResult=false;
   close.Click+=(s,e)=>dialog.DialogResult=false;
   confirm.Click+=(s,e)=>dialog.DialogResult=true;
   ((StackPanel)card.FindName("DragArea")).MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed)dialog.DragMove();};
   dialog.Content=card;dialog.Loaded+=(s,e)=>cancel.Focus();
   return dialog.ShowDialog()==true;
  }
 }
}
