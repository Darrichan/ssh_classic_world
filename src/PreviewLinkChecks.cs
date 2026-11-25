using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SshScreen {
public static class PreviewLinkChecks {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(ControllerForm form,string name,object value){typeof(ControllerForm).GetField(name,Private).SetValue(form,value);}
    static T Get<T>(ControllerForm form,string name){return (T)typeof(ControllerForm).GetField(name,Private).GetValue(form);}
    static object Call(ControllerForm form,string name,params object[] args){
        return typeof(ControllerForm).GetMethod(name,Private).Invoke(form,args);
    }
    static void Need(bool ok,string description){if(!ok)throw new IOException(description);}
    static void WaitUntil(Func<bool> done,string description){
        var timer=Stopwatch.StartNew();
        while(!done()&&timer.ElapsedMilliseconds<1800){Application.DoEvents();Thread.Sleep(5);}
        Need(done(),description);
    }
    static ControllerForm Create(string root){
        Directory.CreateDirectory(root);
        var form=new ControllerForm(root,false,false);
        var handle=form.Handle;
        Set(form,"running",true);
        Set(form,"generation",1);
        return form;
    }
    static void SendFrame(NetworkStream stream,long seq){
        var data=new byte[16*16*3];
        byte[] compressed=Wire.Deflate(data);
        var fields=Wire.Map("ok",true,"width",16,"height",16,"window",0L,"sequence",seq,
            "raw_sha256",Wire.Hash(data),"encoding","bgr_deflate",
            "capture_ms",1.0,"encode_ms",1.0,"hash_ms",1.0,"processing_ms",3.0);
        Wire.Send(stream,fields,compressed);
    }
    static void CheckFrameEof(string root){
        using(var form=Create(root))
        using(var releaseFrame=new ManualResetEvent(false))
        using(var dataSent=new ManualResetEvent(false)){
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            var server=new Thread(()=>{
                try{
                    using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                        Wire.Receive(stream);
                        Wire.Send(stream,Wire.Map("ok",true));
                        Wire.Receive(stream);
                        SendFrame(stream,1);
                        dataSent.Set();
                        releaseFrame.WaitOne(2500);
                    }
                }catch(SocketException){}catch(IOException){}
            }){IsBackground=true};
            server.Start();
            try{
                using(var link=new Link(((IPEndPoint)listener.LocalEndpoint).Port,"offline-preview-test","frame")){
                    Set(form,"frameLink",link);
                    Set(form,"autoRunning",true);
                    var worker=new Thread(()=>Call(form,"FrameWorker",1,link)){IsBackground=true};
                    worker.Start();
                    Need(dataSent.WaitOne(1800),"Fixture did not send the frame");
                    var view=Get<RemoteView>(form,"view");
                    WaitUntil(()=>view.Frame!=null,"The valid frame did not reach the preview");
                    Call(form,"UpdateConnectionButton");
                    var button=Get<Button>(form,"connect");
                    Need(button.Text=="断开连接","A live frame did not mark the preview connected");
                    releaseFrame.Set();
                    Need(worker.Join(1800),"EOF loop kept the frame reader alive");
                    WaitUntil(()=>!Get<bool>(form,"running")&&view.Frame==null&&button.Text=="连接",
                        "The UI retained a connected button or stale frame");
                    Need(button.Enabled&&!Get<bool>(form,"autoRunning")&&!Get<bool>(form,"armed"),"EOF left input armed");
                    Need(view.EmptyMessage.Contains("已断开"),"The preview surface did not report the failure");
                    var log=Wire.Load(Path.Combine(root,"last-connection-error.json"));
                    Need(Wire.Str(log,"channel")=="frame"&&Wire.Str(log,"kind")=="eof","EOF reason was not recorded");
                }
            }finally{releaseFrame.Set();listener.Stop();server.Join(1000);}
        }
    }
    static void CheckSilentFrame(string root){
        using(var form=Create(root)){
            long now=Stopwatch.GetTimestamp();
            Set(form,"lastDecodedPreviewTick",now-7L*Stopwatch.Frequency);
            Call(form,"CheckFrameProgress",now);
            WaitUntil(()=>!Get<bool>(form,"running"),"A stalled preview was still connected");
            var log=Wire.Load(Path.Combine(root,"last-connection-error.json"));
            Need(Wire.Str(log,"channel")=="frame"&&Wire.Str(log,"kind")=="timeout","Stall reason was not recorded");
        }
    }
    static void CheckOldFailure(string root){
        using(var form=Create(root)){
            Set(form,"generation",2);
            Call(form,"FailConnection",1,"frame",new EndOfStreamException(),null);
            Need(Get<bool>(form,"running")&&Get<string>(form,"connectionFault")==null,
                "A late old-channel error changed the next connection");
            Need(!File.Exists(Path.Combine(root,"last-connection-error.json")),"A stale failure overwrote the log");
        }
    }
    static void CheckRevokedControl(string root){
        using(var form=Create(root))
        using(var viewing=new ManualResetEvent(false))
        using(var closeTransport=new ManualResetEvent(false)){
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            const string reason="电脑1本机鼠标已接管，远程输入已暂停";
            bool released=false;
            var server=new Thread(()=>{
                try{
                    using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                        Wire.Receive(stream);Wire.Send(stream,Wire.Map("ok",true));
                        while(true){
                            var op=Wire.Str(Wire.Receive(stream).Header,"op");
                            if(op=="heartbeat"){Wire.Send(stream,Wire.Map("ok",false,"error",reason));continue;}
                            if(op=="release")released=true;
                            Wire.Send(stream,Wire.Map("ok",true));
                            if(op=="ping"&&released){viewing.Set();closeTransport.WaitOne(2500);return;}
                        }
                    }
                }catch(SocketException){}catch(IOException){}
            }){IsBackground=true};
            server.Start();
            try{
                using(var link=new Link(((IPEndPoint)listener.LocalEndpoint).Port,"offline-preview-test","control")){
                    Set(form,"controlLink",link);
                    Set(form,"armed",true);
                    var view=Get<RemoteView>(form,"view");
                    view.Frame=new Bitmap(16,16);
                    var worker=new Thread(()=>Call(form,"ControlWorker",1,link)){IsBackground=true};
                    worker.Start();
                    WaitUntil(()=>viewing.WaitOne(0)&&Get<string>(form,"message")==reason,
                        "Rejected heartbeat did not release input, show its reason, and resume viewing pings");
                    Need(Get<bool>(form,"running")&&!Get<bool>(form,"armed")&&view.Frame!=null&&
                        !File.Exists(Path.Combine(root,"last-connection-error.json")),"Rejected heartbeat disconnected a healthy link");
                    closeTransport.Set();
                    Need(worker.Join(1800),"Real control EOF did not exit the worker");
                    WaitUntil(()=>!Get<bool>(form,"running")&&view.Frame==null,"Real control EOF did not clear the preview");
                }
            }finally{closeTransport.Set();listener.Stop();server.Join(1000);}
        }
    }
    [STAThread]public static int Main(string[] args){
        string root=Path.Combine(args[0],"preview-check-"+Guid.NewGuid().ToString("N"));
        var cases=new List<object>();
        var checks=new Dictionary<string,Action<string>>{
            {"frame-eof-clears-preview-and-switches-button",CheckFrameEof},
            {"silent-frame-times-out-even-with-session-flag",CheckSilentFrame},
            {"old-channel-cannot-disconnect-next-generation",CheckOldFailure},
            {"revoked-input-keeps-preview-connected-until-real-control-eof",CheckRevokedControl}
        };
        bool passed=true;
        foreach(var check in checks){
            try{check.Value(Path.Combine(root,check.Key));cases.Add(Wire.Map("name",check.Key,"ok",true));}
            catch(Exception e){passed=false;cases.Add(Wire.Map("name",check.Key,"ok",false,"error",e.GetBaseException().Message));}
        }
        var report=Wire.Map("ok",passed,"game_input",false,"ssh_started",false,"tests",cases);
        Wire.Save(Path.Combine(args[0],"preview-link-checks.json"),report);
        Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report));
        return passed?0:1;
    }
}
}
