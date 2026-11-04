using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// F01 visual sample. Only reads validated map data; never initializes a world.
internal static class CartographicSample
{
    static readonly Dictionary<int,string> Palette=new(){{1,"#788551"},{2,"#575340"},{4,"#BCBFC5"},{8,"#354B38"},{16,"#B09A65"},{32,"#795043"},{64,"#C3CDD2"},{256,"#3D505E"},{512,"#716A7D"}};
    static Rgba32 Mix(Rgba32 a,Rgba32 b,double t){t=Math.Clamp(t,0,1);return new((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));}
    static Rgba32 Scale(Rgba32 c,double f)=>new((byte)Math.Clamp(Math.Round(c.R*f),0,255),(byte)Math.Clamp(Math.Round(c.G*f),0,255),(byte)Math.Clamp(Math.Round(c.B*f),0,255));
    // Azimuth 315 degrees in world coordinates (-X west, +Z north), altitude 45.
    internal static double Shade(double dx,double dz,double exaggeration)
    {
        double nx=-dx*exaggeration,nz=-dz*exaggeration,norm=Math.Sqrt(nx*nx+1+nz*nz);
        double light=Math.Max(0,(-.5*nx+Math.Sqrt(.5)+.5*nz)/norm);
        return .48+.74*light;
    }
    internal static double Gradient(float lo,float hi,double distance)=>(hi-lo)/distance;
    internal static int CheckGeometry()
    {
        if(Math.Abs(Gradient(10,50,40)-1)>1e-12||Gradient(5,5,20)!=0||Shade(1,0,2)<=Shade(-1,0,2)||Shade(0,-1,2)<=Shade(0,1,2))throw new InvalidOperationException("Cartographic world-axis lighting check failed.");
        // +Z is north, so northern heights are subtracted from southern rows in reverse order.
        if(Gradient(10,30,20)!=Gradient(20,60,40))throw new InvalidOperationException("Metric gradient changes with sample spacing.");
        System.Console.WriteLine("Cartographic geometry: metric spacing, flat plane, east/west and north/south light passed.");return 0;
    }
    static Image<Rgba32> Draw(CartographicMapData d,double exaggeration,bool transparent)
    {
        int n=d.Size;double step=d.Span/n;var image=new Image<Rgba32>(n,n);
        var shallow=Rgba32.ParseHex("#748182");var shelf=Rgba32.ParseHex("#485B68");var deep=Rgba32.ParseHex("#303D50");var sand=Rgba32.ParseHex("#ABA388");var snow=Rgba32.ParseHex("#E0E2DD");var background=Rgba32.ParseHex("#101923");
        for(int row=0;row<n;row++)for(int col=0;col<n;col++){
            int i=row*n+col,x0=Math.Max(0,col-1),x1=Math.Min(n-1,col+1),z0=Math.Max(0,row-1),z1=Math.Min(n-1,row+1);
            double dx=Gradient(d.Height[row*n+x0],d.Height[row*n+x1],(x1-x0)*step);
            double dz=Gradient(d.Height[z1*n+col],d.Height[z0*n+col],(z1-z0)*step);
            double light=Shade(dx,dz,exaggeration);float h=d.Height[i];Rgba32 color;
            if(d.Water[i]==1){
                double depth=30d-h;color=depth<35?Mix(shallow,shelf,depth/35):Mix(shelf,deep,(depth-35)/180);
                color=Scale(color,.94+.06*light);
                bool edge=d.Water[row*n+x0]==0||d.Water[row*n+x1]==0||d.Water[z0*n+col]==0||d.Water[z1*n+col]==0;
                if(edge)color=Mix(color,shallow,.28);
            }else{
                if(!Palette.TryGetValue(d.Biome[i],out string? hex)){image.Dispose();throw new InvalidDataException("Unknown biome in sample: "+d.Biome[i]);}
                color=Rgba32.ParseHex(hex);
                if(d.Biome[i] is 4 or 64)color=Mix(color,snow,Math.Clamp((h-100)/230d,0,.8));
                else color=Mix(color,snow,Math.Clamp((h-60)/900d,0,.13));
                color=Mix(color,sand,.28*Math.Exp(-Math.Max(0,h-30)/5));color=Scale(color,light);
            }
            double wx=(col+.5)*step-d.Span/2,wz=d.Span/2-(row+.5)*step;
            double coverage=Math.Clamp((d.Span/2-Math.Sqrt(wx*wx+wz*wz))/step+.5,0,1);
            if(transparent)color.A=(byte)Math.Round(255*coverage);else color=Mix(background,color,coverage);
            image[col,row]=color;
        }
        return image;
    }
    static (int x,int y) Pick(CartographicMapData d,string kind)
    {
        int n=d.Size;double best=double.NegativeInfinity;var chosen=(x:n/2,y:n/2);
        for(int y=128;y<n-128;y++)for(int x=128;x<n-128;x++){
            int i=y*n+x;double score;
            if(kind=="mountain"){if(d.Water[i]!=0||d.Biome[i]!=4)continue;score=d.Height[i];}
            else if(kind=="water"){if(d.Water[i]!=1||d.Height[i]>-20)continue;score=-Math.Pow(x-n*.5,2)-Math.Pow(y-n*.25,2);}
            else if(kind=="coast"){if(d.Water[i]!=1||d.Water[i+1]!=0)continue;score=-Math.Pow(x-n*.35,2)-Math.Pow(y-n*.5,2);}
            else{if(d.Water[i]!=0||d.Water[i+1]!=0||d.Biome[i]==d.Biome[i+1])continue;score=-Math.Pow(x-n*.5,2)-Math.Pow(y-n*.5,2);}
            if(score>best){best=score;chosen=(x,y);}
        }
        if(double.IsNegativeInfinity(best))throw new InvalidDataException("No representative sample region: "+kind);return chosen;
    }
    internal static int Run(string bundle,string output)
    {
        bundle=Path.GetFullPath(bundle);output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("Sample output must be new.");
        var timer=Stopwatch.StartNew();var d=CartographicMapData.Read(bundle);if(d.Size<512)throw new InvalidDataException("Sample crops require 512 or larger.");CheckGeometry();Directory.CreateDirectory(output);
        using var balanced=Draw(d,3,true);balanced.SaveAsPng(Path.Combine(output,"map-transparent.png"));
        using(var matte=Draw(d,3,false))matte.SaveAsPng(Path.Combine(output,"map.png"));
        using(var soft=Draw(d,1.5,false))soft.SaveAsPng(Path.Combine(output,"map-soft.png"));
        var crops=new List<object>();
        using var diagnostic=Image.Load<Rgba32>(Path.Combine(bundle,"map/biomes.png"));
        foreach(string kind in new[]{"coast","mountain","water","transition"}){
            var p=Pick(d,kind);var rectangle=new Rectangle(p.x-128,p.y-128,256,256);
            using(var crop=balanced.Clone(ctx=>ctx.Crop(rectangle)))crop.SaveAsPng(Path.Combine(output,kind+"-1to1.png"));
            using(var crop=diagnostic.Clone(ctx=>ctx.Crop(rectangle)))crop.SaveAsPng(Path.Combine(output,kind+"-biomes-1to1.png"));
            crops.Add(new{kind,left=rectangle.X,top=rectangle.Y,width=256,height=256,centerX=((p.x+.5)/d.Size-.5)*d.Span,centerZ=(.5-(p.y+.5)/d.Size)*d.Span});
        }
        foreach(var entry in d.Hashes)if(CartographicMapData.Hash(Path.Combine(bundle,"map",entry.Key))!=entry.Value)throw new IOException("Source changed during sample rendering.");
        File.WriteAllText(Path.Combine(output,"sample.json"),JsonSerializer.Serialize(new{status="F01-visual-direction-awaiting-user-review",d.Preset,d.Seed,d.Size,totalSizeMetres=d.Span,metresPerPixel=d.Span/d.Size,sourceHashes=d.Hashes,palette=Palette,lightAzimuthDegrees=315,lightAltitudeDegrees=45,balancedExaggeration=3,softExaggeration=1.5,oceanMetres=30,crops,elapsedMs=timer.ElapsedMilliseconds,limitation="Base data only. No forest-density, lava/moss material masks or alt-biome texture overrides. Categorical biome colors do not represent individual trees. Display shading and circular alpha never change source layers."},new JsonSerializerOptions{WriteIndented=true}));
        System.Console.WriteLine("Cartographic samples exported; source hashes unchanged.");return 0;
    }
}
