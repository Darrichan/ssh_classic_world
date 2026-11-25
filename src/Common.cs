using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SshScreen {
public sealed class Packet {
    public Dictionary<string,object> Header;
    public byte[] Data;
}
public static class Wire {
    public const int Version=2, MaxPayload=64*1024*1024;
    public static Dictionary<string,object> Map(params object[] values) {
        var map=new Dictionary<string,object>();
        for(int i=0;i<values.Length;i+=2)map[(string)values[i]]=values[i+1];
        return map;
    }
    public static string Str(Dictionary<string,object> m,string key,string fallback="") {object v;return m.TryGetValue(key,out v)?Convert.ToString(v):fallback;}
    public static int Int(Dictionary<string,object> m,string key,int fallback=0) {object v;return m.TryGetValue(key,out v)?Convert.ToInt32(v):fallback;}
    public static long Long(Dictionary<string,object> m,string key,long fallback=0) {object v;return m.TryGetValue(key,out v)?Convert.ToInt64(v):fallback;}
    public static bool Bool(Dictionary<string,object> m,string key) {object v;return m.TryGetValue(key,out v)&&Convert.ToBoolean(v);}
    public static Dictionary<string,object> Load(string path) {return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}
    public static void Save(string path,object value) {
        string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(value),new UTF8Encoding(false));
        if(File.Exists(path))File.Delete(path);File.Move(tmp,path);
    }
    static byte[] Read(Stream stream,int length) {
        byte[] data=new byte[length];int offset=0;
        while(offset<length){int n=stream.Read(data,offset,length-offset);if(n==0)throw new EndOfStreamException("SSH连接已关闭");offset+=n;}
        return data;
    }
    public static Packet Receive(Stream stream) {
        int size=BitConverter.ToInt32(Read(stream,4),0);
        if(size<=0||size>65536)throw new InvalidDataException("协议头长度无效");
        var header=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Read(stream,size)));
        int payload=Int(header,"payload_length");if(payload<0||payload>MaxPayload)throw new InvalidDataException("画面数据长度无效");
        return new Packet{Header=header,Data=Read(stream,payload)};
    }
    public static void Send(Stream stream,Dictionary<string,object> header,byte[] data=null) {
        if(data==null)data=new byte[0];header["payload_length"]=data.Length;
        byte[] json=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(header));
        stream.Write(BitConverter.GetBytes(json.Length),0,4);stream.Write(json,0,json.Length);
        if(data.Length>0)stream.Write(data,0,data.Length);stream.Flush();
    }
    public static byte[] Deflate(byte[] data) {
        using(var output=new MemoryStream()){
            using(var zip=new DeflateStream(output,CompressionLevel.Fastest,true))zip.Write(data,0,data.Length);
            return output.ToArray();
        }
    }
    public static byte[] Inflate(byte[] data,int expected) {
        using(var input=new MemoryStream(data))using(var zip=new DeflateStream(input,CompressionMode.Decompress)){
            byte[] result=new byte[expected];int offset=0,n;
            while(offset<expected&&(n=zip.Read(result,offset,expected-offset))>0)offset+=n;
            if(offset!=expected||zip.ReadByte()!=-1)throw new InvalidDataException("像素长度不符");return result;
        }
    }
    public static string Hash(byte[] data) {using(var sha=new SHA256Cng())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    public static void InflateInto(byte[] data,byte[] output) {
        using(var stream=new MemoryStream(data))using(var zip=new DeflateStream(stream,CompressionMode.Decompress)){
            int at=0,n;while(at<output.Length&&(n=zip.Read(output,at,output.Length-at))>0)at+=n;
            if(at!=output.Length||zip.ReadByte()!=-1)throw new InvalidDataException("像素长度不符");
        }
    }
    public static unsafe void Xor(byte[] a,byte[] b,byte[] result) {
        if(a.Length!=b.Length||a.Length!=result.Length)throw new InvalidDataException("差分长度不符");
        int length=a.Length,blocks=length/8;
        fixed(byte* pa=a,pb=b,pc=result){ulong* x=(ulong*)pa,y=(ulong*)pb,z=(ulong*)pc;for(int i=0;i<blocks;i++)z[i]=x[i]^y[i];for(int i=blocks*8;i<length;i++)pc[i]=(byte)(pa[i]^pb[i]);}
    }
    public static unsafe bool Equal(byte[] a,byte[] b) {
        if(a==null||b==null||a.Length!=b.Length)return false;
        int blocks=a.Length/8;fixed(byte* pa=a,pb=b){ulong* x=(ulong*)pa,y=(ulong*)pb;for(int i=0;i<blocks;i++)if(x[i]!=y[i])return false;for(int i=blocks*8;i<a.Length;i++)if(pa[i]!=pb[i])return false;}return true;
    }
    public static unsafe int[] ChangedTiles(byte[] raw,byte[] prior,int width,int height,int tile) {
        var changed=new List<int>();int columns=(width+tile-1)/tile;
        fixed(byte* a=raw,b=prior){for(int top=0;top<height;top+=tile)for(int left=0;left<width;left+=tile){int rows=Math.Min(tile,height-top),rowBytes=Math.Min(tile,width-left)*3;bool different=false;
            for(int y=0;y<rows&&!different;y++){int offset=((top+y)*width+left)*3;ulong* p=(ulong*)(a+offset),q=(ulong*)(b+offset);int n=rowBytes/8;for(int i=0;i<n;i++)if(p[i]!=q[i]){different=true;break;}if(!different)for(int i=n*8;i<rowBytes;i++)if(a[offset+i]!=b[offset+i]){different=true;break;}}
            if(different)changed.Add((top/tile)*columns+left/tile);
        }}return changed.ToArray();
    }
    public static int TileBytes(int[] tiles,int width,int height,int tile) {
        int columns=(width+tile-1)/tile,rows=(height+tile-1)/tile,total=0;var seen=new HashSet<int>();
        foreach(int id in tiles){if(id<0||id>=columns*rows||!seen.Add(id))throw new InvalidDataException("差分块编号无效");int x=id%columns*tile,y=id/columns*tile;total=checked(total+Math.Min(tile,width-x)*Math.Min(tile,height-y)*3);}return total;
    }
    public static unsafe byte[] PackTiles(byte[] raw,byte[] prior,int[] tiles,int width,int height,int tile) {
        byte[] packed=new byte[TileBytes(tiles,width,height,tile)];int columns=(width+tile-1)/tile,at=0;
        fixed(byte* a=raw,b=prior,c=packed){foreach(int id in tiles){int left=id%columns*tile,top=id/columns*tile,rowBytes=Math.Min(tile,width-left)*3,rows=Math.Min(tile,height-top);
            for(int y=0;y<rows;y++){int offset=((top+y)*width+left)*3;ulong* p=(ulong*)(a+offset),q=(ulong*)(b+offset),r=(ulong*)(c+at);int n=rowBytes/8;for(int i=0;i<n;i++)r[i]=p[i]^q[i];for(int i=n*8;i<rowBytes;i++)c[at+i]=(byte)(a[offset+i]^b[offset+i]);at+=rowBytes;}
        }}return packed;
    }
    public static unsafe void ApplyTiles(byte[] current,byte[] packed,int[] tiles,int width,int height,int tile) {
        if(packed.Length!=TileBytes(tiles,width,height,tile))throw new InvalidDataException("差分块长度不符");int columns=(width+tile-1)/tile,at=0;
        fixed(byte* a=current,b=packed){foreach(int id in tiles){int left=id%columns*tile,top=id/columns*tile,rowBytes=Math.Min(tile,width-left)*3,rows=Math.Min(tile,height-top);
            for(int y=0;y<rows;y++){int offset=((top+y)*width+left)*3;ulong* p=(ulong*)(a+offset),q=(ulong*)(b+at);int n=rowBytes/8;for(int i=0;i<n;i++)p[i]^=q[i];for(int i=n*8;i<rowBytes;i++)a[offset+i]^=b[at+i];at+=rowBytes;}
        }}
    }
    public static int[] IntArray(object values){var list=new List<int>();foreach(object v in (System.Collections.IEnumerable)values)list.Add(Convert.ToInt32(v));return list.ToArray();}
    public static int PixelLength(int width,int height) {
        if(width<1||height<1||width>8192||height>8192||(long)width*height>16000000)throw new InvalidDataException("画面尺寸超出限制");return checked(width*height*3);
    }
    public static byte[] Pixels(Bitmap bitmap) {
        byte[] raw=new byte[PixelLength(bitmap.Width,bitmap.Height)];int row=bitmap.Width*3;
        var locked=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
        try{for(int y=0;y<bitmap.Height;y++)Marshal.Copy(IntPtr.Add(locked.Scan0,y*locked.Stride),raw,y*row,row);}finally{bitmap.UnlockBits(locked);}return raw;
    }
    public static Bitmap BitmapFrom(byte[] raw,int width,int height) {
        if(raw.Length!=PixelLength(width,height))throw new InvalidDataException("像素数量错误");
        var bitmap=new Bitmap(width,height,PixelFormat.Format24bppRgb);int row=width*3;
        var locked=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
        try{for(int y=0;y<height;y++)Marshal.Copy(raw,y*row,IntPtr.Add(locked.Scan0,y*locked.Stride),row);}finally{bitmap.UnlockBits(locked);}return bitmap;
    }
}
}
