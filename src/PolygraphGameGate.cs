using System;
using System.IO;

namespace SshScreen {
// Lightweight template check for the active polygraph game's fixed instruction strip.
// The reference is a 2x downsampled grayscale crop from a confirmed in-game frame.
sealed class PolygraphGameGate {
    public const int SampleWidth=133,SampleHeight=23;
    readonly byte[] reference;
    public readonly string Error;
    public PolygraphGameGate(string runtime){
        try{
            string path=Path.Combine(runtime,"polygraph-active-prompt.gray");
            reference=File.ReadAllBytes(path);
            if(reference.Length!=SampleWidth*SampleHeight)throw new InvalidDataException("小游戏提示模板尺寸不匹配");
        }catch(Exception e){Error=e.Message;}
    }
    public bool Available{get{return reference!=null;}}
    public bool IsActive(byte[] bgr,int width,int height,out double correlation,out double meanDifference){
        correlation=0;meanDifference=255;
        if(reference==null||bgr==null||width<SampleWidth||height<SampleHeight||bgr.Length!=width*height*3)return false;
        int count=reference.Length;double sumA=0,sumB=0;
        double scaleX=width/266.0,scaleY=height/45.0;
        int k=0;
        for(int y=0;y<SampleHeight;y++){
            int py=Math.Min(height-1,(int)(2*y*scaleY));
            for(int x=0;x<SampleWidth;x++){
                int px=Math.Min(width-1,(int)(2*x*scaleX));int i=(py*width+px)*3;
                int gray=(bgr[i]*29+bgr[i+1]*150+bgr[i+2]*77)>>8;
                sumA+=reference[k++];sumB+=gray;
            }
        }
        double meanA=sumA/count,meanB=sumB/count,ab=0,aa=0,bb=0,difference=0;k=0;
        for(int y=0;y<SampleHeight;y++){
            int py=Math.Min(height-1,(int)(2*y*scaleY));
            for(int x=0;x<SampleWidth;x++){
                int px=Math.Min(width-1,(int)(2*x*scaleX));int i=(py*width+px)*3;
                double a=reference[k],b=(bgr[i]*29+bgr[i+1]*150+bgr[i+2]*77)>>8;k++;
                double da=a-meanA,db=b-meanB;ab+=da*db;aa+=da*da;bb+=db*db;difference+=Math.Abs(a-b);
            }
        }
        correlation=aa<=0||bb<=0?0:ab/Math.Sqrt(aa*bb);meanDifference=difference/count;
        return correlation>=0.88&&meanDifference<=30.0;
    }
}
}
