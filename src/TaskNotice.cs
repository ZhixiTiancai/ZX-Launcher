using System;using System.Windows;using System.Windows.Controls;using System.Windows.Threading;
namespace FoolLauncher {
 static class TaskNotice {
  public static void Show(Window owner,string title,string message){
   var grid=owner.Content as Grid;if(grid==null)return;
   for(int n=grid.Children.Count-1;n>=0;n--)if((grid.Children[n] as FrameworkElement)!=null&&((FrameworkElement)grid.Children[n]).Tag as string=="TaskNotice")grid.Children.RemoveAt(n);
   var box=new Border{Tag="TaskNotice",Width=390,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,100,26,0),Padding=new Thickness(18),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)};
   box.SetResourceReference(Border.BackgroundProperty,"Brush.Dropdown");box.SetResourceReference(Border.BorderBrushProperty,"Brush.Accent");Panel.SetZIndex(box,100);
   var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var lines=new StackPanel();var heading=new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,FontSize=17};heading.SetResourceReference(TextBlock.ForegroundProperty,"Brush.Accent");lines.Children.Add(heading);
   var detail=new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,MaxHeight=110,Margin=new Thickness(0,8,8,0)};detail.SetResourceReference(TextBlock.ForegroundProperty,"Brush.Text");lines.Children.Add(detail);body.Children.Add(lines);
   var close=new Button{Content="×",Padding=new Thickness(8,3,8,3),Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Top,ToolTip="关闭提示"};Grid.SetColumn(close,1);body.Children.Add(close);box.Child=body;grid.Children.Add(box);
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(18)};Action dismiss=()=>{timer.Stop();grid.Children.Remove(box);};timer.Tick+=(s,e)=>dismiss();close.Click+=(s,e)=>dismiss();timer.Start();
  }
 }
}
