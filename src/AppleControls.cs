using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace SshScreen {
sealed class WorkspaceComboBox:ComboBox {
 public string Placeholder {get;set;}
 public int EmptyChoiceIndex {get;set;}
 bool hovered;

 public WorkspaceComboBox(){
  DropDownStyle=ComboBoxStyle.DropDownList;
  DrawMode=DrawMode.OwnerDrawFixed;
  FlatStyle=FlatStyle.Flat;
  BackColor=AppUi.Field;ForeColor=AppUi.Text;
  EmptyChoiceIndex=-1;
  UpdateItemHeight();
 }
 void UpdateItemHeight(){ItemHeight=Font.Height+10;}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);UpdateItemHeight();}
 protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
 protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
 protected override void OnDropDown(EventArgs e){
  int width=Width;
  foreach(var item in Items)width=Math.Max(width,TextRenderer.MeasureText(GetItemText(item),Font).Width+45);
  DropDownWidth=Math.Max(Width,Math.Min(width,Screen.FromControl(this).WorkingArea.Width/2));
  base.OnDropDown(e);Invalidate();
 }
 protected override void OnDropDownClosed(EventArgs e){base.OnDropDownClosed(e);Invalidate();}

 protected override void WndProc(ref Message m){
  base.WndProc(ref m);
  if(IsDisposed||!IsHandleCreated||SystemInformation.HighContrast)return;
  if(m.Msg==0x000F){using(var graphics=Graphics.FromHwnd(Handle))DrawField(graphics);}
  else if((m.Msg==0x0317||m.Msg==0x0318)&&m.WParam!=IntPtr.Zero){
   using(var graphics=Graphics.FromHdc(m.WParam))DrawField(graphics);
  }
 }
 void DrawField(Graphics graphics){
  if(Width<8||Height<8)return;
  bool active=Enabled&&(Focused||DroppedDown);
  using(var background=new SolidBrush(Parent==null?AppUi.Background:Parent.BackColor))
   graphics.FillRectangle(background,0,0,Width,Height);
  graphics.SmoothingMode=SmoothingMode.AntiAlias;
  using(var outline=AppUi.Rounded(new Rectangle(0,0,Width-1,Height-1),7))
  using(var fill=new SolidBrush(!Enabled?AppUi.Surface:AppUi.Field))
  using(var border=new Pen(active?AppUi.Blue:hovered?AppUi.Muted:AppUi.Border)){
   graphics.FillPath(fill,outline);graphics.DrawPath(border,outline);
  }
  bool selected=SelectedIndex>=0;
  string text=selected?GetItemText(SelectedItem):Placeholder??"请选择";
  DrawText(graphics,text,new Rectangle(10,0,Math.Max(0,Width-38),Height),
   Enabled&&selected&&SelectedIndex!=EmptyChoiceIndex?AppUi.Text:AppUi.Muted);
  int x=Width-15,y=Height/2;
  using(var pen=new Pen(active?AppUi.Blue:AppUi.Muted,1.5f)){
   pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
   graphics.DrawLines(pen,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});
  }
 }
 protected override void OnDrawItem(DrawItemEventArgs e){
  if(e.Index<0||e.Index>=Items.Count)return;
  bool selected=(e.State&DrawItemState.Selected)!=0;
  bool highContrast=SystemInformation.HighContrast;
  var background=highContrast?(selected?SystemColors.Highlight:SystemColors.Window):(selected?AppUi.Selection:AppUi.Surface);
  var foreground=highContrast?(selected?SystemColors.HighlightText:SystemColors.WindowText):
   e.Index==EmptyChoiceIndex?AppUi.Muted:AppUi.Text;
  using(var fill=new SolidBrush(background))e.Graphics.FillRectangle(fill,e.Bounds);
  DrawText(e.Graphics,GetItemText(Items[e.Index]),new Rectangle(e.Bounds.X+10,e.Bounds.Y,Math.Max(0,e.Bounds.Width-33),e.Bounds.Height),foreground);
  if(e.Index==SelectedIndex&&e.Index!=EmptyChoiceIndex){
   int x=e.Bounds.Right-15,y=e.Bounds.Top+e.Bounds.Height/2;
   using(var pen=new Pen(highContrast?foreground:AppUi.Blue,1.5f))
    e.Graphics.DrawLines(pen,new[]{new Point(x-4,y),new Point(x-1,y+3),new Point(x+5,y-3)});
  }
  if(highContrast)e.DrawFocusRectangle();
 }
 void DrawText(Graphics graphics,string text,Rectangle area,Color color){
  TextRenderer.DrawText(graphics,text,Font,area,color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
 }
}
public class AppleButton:Button {
 bool over,pressed;
 public AppleButton(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;}
 protected override void OnMouseEnter(EventArgs e){over=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){over=pressed=false;Invalidate();base.OnMouseLeave(e);}protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?AppUi.Background:Parent.BackColor);Color color=Enabled?(pressed?FlatAppearance.MouseDownBackColor:over?FlatAppearance.MouseOverBackColor:BackColor):AppUi.Surface;if(color.IsEmpty)color=BackColor;using(var path=AppUi.Rounded(new Rectangle(0,0,Math.Max(2,Width-1),Math.Max(2,Height-1)),8))using(var brush=new SolidBrush(color))g.FillPath(brush,path);TextRenderer.DrawText(g,Text,Font,new Rectangle(4,0,Math.Max(1,Width-8),Height),Enabled?ForeColor:AppUi.Muted,(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if(Focused&&ShowFocusCues)using(var path=AppUi.Rounded(new Rectangle(2,2,Math.Max(2,Width-5),Math.Max(2,Height-5)),7))using(var pen=new Pen(Color.FromArgb(125,185,255),1.5f))g.DrawPath(pen,path);}
}
sealed class MapMarkerButton:AppleButton {
 const int LeftDown=0x201,LeftUp=0x202,LeftDoubleClick=0x203;
 bool pointerDown;
 public MapMarkerButton(){
  SetStyle(ControlStyles.Selectable,false);
  TabStop=false;
 }
 protected override void OnMouseCaptureChanged(EventArgs e){
  if(!Capture)pointerDown=false;
  base.OnMouseCaptureChanged(e);
 }
 protected override void WndProc(ref Message message){
  // The standard button mouse handler can take focus from the remote preview.
  // Keep normal lost-focus protection and handle only this local mouse gesture.
  if(message.Msg==LeftDown||message.Msg==LeftDoubleClick){
   if(Enabled){pointerDown=true;Capture=true;}
   message.Result=IntPtr.Zero;
   return;
  }
  if(message.Msg==LeftUp){
   long bits=message.LParam.ToInt64();
   var location=new Point(unchecked((short)bits),unchecked((short)(bits>>16)));
   bool click=pointerDown&&Enabled&&ClientRectangle.Contains(location);
   pointerDown=false;
   if(Capture)Capture=false;
   if(click)OnClick(EventArgs.Empty);
   message.Result=IntPtr.Zero;
   return;
  }
  base.WndProc(ref message);
 }
}
public class AppleCard:TableLayoutPanel {public AppleCard(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?AppUi.Background:Parent.BackColor);using(var path=AppUi.Rounded(new Rectangle(0,0,Math.Max(2,Width-1),Math.Max(2,Height-1)),12))using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);}}
public class AppleFlowCard:FlowLayoutPanel {public AppleFlowCard(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?AppUi.Background:Parent.BackColor);using(var path=AppUi.Rounded(new Rectangle(0,0,Math.Max(2,Width-1),Math.Max(2,Height-1)),12))using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);}}
public class AppleForm:Form {
 public bool AllowResize=true;
 [DllImport("user32.dll")]static extern bool ReleaseCapture();[DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public AppleForm(){FormBorderStyle=FormBorderStyle.None;BackColor=AppUi.Background;Resize+=delegate{if(WindowState==FormWindowState.Normal&&Width>2&&Height>2){using(var p=AppUi.Rounded(new Rectangle(0,0,Width,Height),12)){var old=Region;Region=new Region(p);if(old!=null)old.Dispose();}}else{var old=Region;Region=null;if(old!=null)old.Dispose();}};}
 public void InstallCaption(string title,bool traffic=true){var caption=new Panel{Dock=DockStyle.Top,Height=38,BackColor=AppUi.Background};var text=new Label{Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=AppUi.Muted,Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold)};caption.Controls.Add(text);var buttons=new Panel{Dock=DockStyle.Left,Width=102};for(int i=0;i<3;i++){int which=i;var dot=new TrafficDot{Location=new Point(18+i*24,12),Size=new Size(14,14),DotColor=i==0?Color.FromArgb(255,95,87):i==1?Color.FromArgb(254,188,46):Color.FromArgb(40,200,64),Symbol=i==0?"×":i==1?"−":"+"};dot.Click+=delegate{if(which==0)Close();else if(which==1)WindowState=FormWindowState.Minimized;else WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;};buttons.Controls.Add(dot);}if(traffic)caption.Controls.Add(buttons);else buttons.Dispose();MouseEventHandler drag=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};caption.MouseDown+=drag;text.MouseDown+=drag;text.DoubleClick+=delegate{if(!AllowResize)return;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;};Controls.Add(caption);}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x84&&m.Result.ToInt32()==1&&AllowResize&&WindowState==FormWindowState.Normal){long value=m.LParam.ToInt64();var p=PointToClient(new Point((short)(value&65535),(short)((value>>16)&65535)));bool l=p.X<6,r=p.X>=ClientSize.Width-6,t=p.Y<6,b=p.Y>=ClientSize.Height-6;m.Result=new IntPtr(t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:r?11:1);}}
 sealed class TrafficDot:Control {public Color DotColor;public string Symbol;bool over;public TrafficDot(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);Cursor=Cursors.Hand;}protected override void OnMouseEnter(EventArgs e){over=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){over=false;Invalidate();base.OnMouseLeave(e);}protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent.BackColor);using(var b=new SolidBrush(DotColor))e.Graphics.FillEllipse(b,0,0,13,13);if(over)TextRenderer.DrawText(e.Graphics,Symbol,new Font("Segoe UI",8,FontStyle.Bold),new Rectangle(-1,-1,15,15),Color.FromArgb(80,65,50),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}}
}
}
