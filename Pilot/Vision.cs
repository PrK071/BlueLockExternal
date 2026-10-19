// C# | Vision.cs | .NET Framework / Windows x64 | Local, user-calibrated image matching.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal sealed class Match
{
    internal Rectangle Box;
    internal double Confidence;
    internal Size FrameSize;
    internal Bitmap Preview;
    internal int CandidateCount;
}

internal static class Vision
{
    internal static string SampleProblem(Bitmap sample)
    {
        int total=sample.Width*sample.Height,grass=0;double sum=0,sum2=0;
        for(int y=0;y<sample.Height;y++)for(int x=0;x<sample.Width;x++){
            Color color=sample.GetPixel(x,y);
            if(color.G>color.R+8 && color.G>color.B+8)grass++;
            double gray=(color.R+color.G+color.B)/3.0;sum+=gray;sum2+=gray*gray;
        }
        if(total==0)return "amostra vazia";
        if(grass/(double)total>.85)return "mais de 85% da amostra é verde de grama; a bola não ocupa uma área suficiente";
        if(Math.Sqrt(Math.Max(0,sum2/total-Math.Pow(sum/total,2)))<9)return "amostra sem contraste suficiente";
        return null;
    }
    internal static Bitmap Capture(Rectangle bounds)
    {
        Bitmap bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format24bppRgb);
        try { using(Graphics g=Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy); }
        catch { bitmap.Dispose(); throw; }
        return bitmap;
    }
    internal static Bitmap Scale(Bitmap source,int width,int height)
    {
        Bitmap result=new Bitmap(width,height,PixelFormat.Format24bppRgb);
        using(Graphics g=Graphics.FromImage(result)) {
            g.InterpolationMode=InterpolationMode.HighQualityBilinear;
            g.DrawImage(source,new Rectangle(0,0,width,height));
        }
        return result;
    }
    private static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
        try {
            byte[] packed=new byte[bitmap.Width*bitmap.Height*3];
            for(int y=0;y<bitmap.Height;y++) Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),packed,y*bitmap.Width*3,bitmap.Width*3);
            return packed;
        } finally { bitmap.UnlockBits(data); }
    }
    private sealed class Candidate {internal Rectangle Box;internal double Shape;}
    private static bool Green(byte[] p,int i){return p[i+1]>p[i+2]+8 && p[i+1]>p[i]+8;}
    private static bool Light(byte[] p,int i)
    {
        int b=p[i],g=p[i+1],r=p[i+2];
        return r>125 && g>125 && b>125 && r+g+b>465 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<100;
    }
    private static bool Field(int x,int y,int w,int h)
    {
        double nx=x/(double)w,ny=y/(double)h;
        if(nx<.01||nx>.99||ny<.14||ny>.90)return false;
        if(nx>.85 && ny>.54)return false;
        if(nx>.30 && nx<.67 && ny>.70)return false;
        return true;
    }
    private static List<Candidate> Candidates(byte[] pixels,int w,int h)
    {
        byte[] white=new byte[w*h],expanded=new byte[w*h];
        for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
            int pos=y*w+x;
            if(Field(x,y,w,h) && Light(pixels,pos*3) && !Green(pixels,pos*3)){
                white[pos]=1;
                // Join white panels across narrow dark seams.
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)expanded[(y+dy)*w+x+dx]=1;
            }
        }
        var result=new List<Candidate>();int[] queue=new int[w*h];
        for(int start=0;start<expanded.Length;start++){
            if(expanded[start]!=1)continue;
            int head=0,tail=0,minX=w,maxX=0,minY=h,maxY=0,whiteCount=0;
            queue[tail++]=start;expanded[start]=2;
            while(head<tail){
                int pos=queue[head++],x=pos%w,y=pos/w;
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);whiteCount+=white[pos];
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                    int xx=x+dx,yy=y+dy;if(xx<0||xx>=w||yy<0||yy>=h)continue;
                    int next=yy*w+xx;if(expanded[next]==1){expanded[next]=2;queue[tail++]=next;}
                }
            }
            int bw=maxX-minX+1,bh=maxY-minY+1,diameter=Math.Max(bw,bh);
            if(whiteCount<3||diameter<4||diameter>80||Math.Min(bw,bh)/(double)diameter<.58)continue;
            Rectangle box=new Rectangle(minX,minY,bw,bh);double shape=Shape(pixels,w,h,box);
            if(shape>.55)result.Add(new Candidate{Box=box,Shape=shape});
        }
        result.Sort(delegate(Candidate a,Candidate b){return b.Shape.CompareTo(a.Shape);});
        if(result.Count>70)result.RemoveRange(70,result.Count-70);
        return result;
    }
    private static double Shape(byte[] p,int w,int h,Rectangle box)
    {
        double d=Math.Max(box.Width,box.Height),cx=box.X+(box.Width-1)/2.0,cy=box.Y+(box.Height-1)/2.0;
        double inner2=Math.Pow(d*.39,2),outerMin2=Math.Pow(d*.62,2),outerMax2=Math.Pow(d*.90,2);
        int inside=0,nonGreen=0,light=0,dark=0,ring=0,grass=0,reach=(int)Math.Ceiling(d*.90);
        for(int y=Math.Max(0,(int)cy-reach);y<=Math.Min(h-1,(int)cy+reach);y++)
        for(int x=Math.Max(0,(int)cx-reach);x<=Math.Min(w-1,(int)cx+reach);x++){
            double radius=(x-cx)*(x-cx)+(y-cy)*(y-cy);int i=(y*w+x)*3;bool green=Green(p,i);
            if(radius<=inner2){
                inside++;if(!green)nonGreen++;if(Light(p,i)&&!green)light++;
                if(!green && Math.Max(p[i],Math.Max(p[i+1],p[i+2]))<150)dark++;
            }else if(radius>=outerMin2&&radius<=outerMax2){ring++;if(green)grass++;}
        }
        if(inside==0||ring==0)return 0;
        double ng=nonGreen/(double)inside,lf=light/(double)inside,df=dark/(double)inside,gr=grass/(double)ring;
        if(ng<.52||lf<.14||gr<.48)return 0;
        double roundness=Math.Min(box.Width,box.Height)/d,texture=Math.Min(1,lf/.30)*Math.Min(1,df/.12);
        return .30*gr+.25*roundness+.25*ng+.20*texture;
    }
    private static double TemplateScore(byte[] pixels,int w,int h,Bitmap template,Rectangle box)
    {
        double best=0,cx=box.X+box.Width/2.0,cy=box.Y+box.Height/2.0;int diameter=Math.Max(box.Width,box.Height);
        foreach(double scale in new double[]{.78,.94,1.08,1.25,1.50}){
            int size=Math.Max(4,Math.Min(100,(int)Math.Round(diameter*scale)));
            byte[] model;using(Bitmap scaled=Scale(template,size,size))model=Pixels(scaled);
            for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){
                int left=(int)Math.Round(cx-size/2.0)+dx,top=(int)Math.Round(cy-size/2.0)+dy;
                if(left<0||top<0||left+size>w||top+size>h)continue;
                double error=0;
                for(int sy=0;sy<8;sy++)for(int sx=0;sx<8;sx++){
                    int yy=Math.Min(size-1,(sy*size+size/2)/8),xx=Math.Min(size-1,(sx*size+size/2)/8);
                    int a=((top+yy)*w+left+xx)*3,b=(yy*size+xx)*3;
                    error+=Math.Abs(pixels[a]-model[b])+Math.Abs(pixels[a+1]-model[b+1])+Math.Abs(pixels[a+2]-model[b+2]);
                }
                // Calculate the complete score instead of converting early rejects to 0%.
                best=Math.Max(best,1.0-error/(64*3*255.0));
            }
        }
        return best;
    }
    internal static Match AnalyzeBitmap(Bitmap capture,Bitmap template,Rectangle lastBox,double minimum)
    {
        using(Bitmap frame=Scale(capture,640,Math.Max(100,(int)Math.Round(640.0*capture.Height/capture.Width)))){
            byte[] pixels=Pixels(frame);int w=frame.Width,h=frame.Height,luminance=0;
            for(int i=0;i<pixels.Length;i+=Math.Max(3,(pixels.Length/600/3)*3))luminance+=pixels[i]+pixels[i+1]+pixels[i+2];
            if(luminance<200)throw new InvalidOperationException("Captura preta. Use modo janela/sem bordas e mantenha Roblox visível.");
            List<Candidate> candidates=Candidates(pixels,w,h);double best=0;Rectangle bestBox=Rectangle.Empty;
            foreach(Candidate candidate in candidates){
                double raw=TemplateScore(pixels,w,h,template,candidate.Box);
                // Shape/contrast tolerates ball rotation. This score is not a probability.
                double score=.80*candidate.Shape+.20*raw;
                if(raw<.66)continue;
                if(score>best){best=score;bestBox=candidate.Box;}
            }
            Bitmap preview=(Bitmap)frame.Clone();
            if(best>=minimum)using(Graphics g=Graphics.FromImage(preview))using(Pen pen=new Pen(Color.Lime,2))g.DrawRectangle(pen,bestBox);
            return new Match{Box=best>=minimum?bestBox:Rectangle.Empty,Confidence=best,FrameSize=frame.Size,Preview=preview,CandidateCount=candidates.Count};
        }
    }
    internal static Match Find(IntPtr window,Rectangle bounds,Bitmap template,Rectangle lastBox,double minimum)
    {
        if(!Platform.GameFocused(window))throw new OperationCanceledException("Roblox perdeu foco.");
        using(Bitmap capture=Capture(bounds)){
            if(!Platform.GameFocused(window))throw new OperationCanceledException("Roblox perdeu foco.");
            return AnalyzeBitmap(capture,template,lastBox,minimum);
        }
    }
}
