using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp.PixelFormats;

internal sealed class CartographicStyle
{
    public string Format {get;set;}="sp-cartographic-style/1";
    public double FadeMetres {get;set;}=120;
    public double Exaggeration {get;set;}=3;
    public double LightAzimuth {get;set;}=315;
    public double LightAltitude {get;set;}=45;
    public double Ambient {get;set;}=.48;
    public double Diffuse {get;set;}=.74;
    public double ShelfDepth {get;set;}=35;
    public double DeepRange {get;set;}=180;
    public double ShoreHeight {get;set;}=5;
    public double ShoreTint {get;set;}=.28;
    public bool Transparent {get;set;}=false;
    public bool Circular {get;set;}=true;
    public string Shallow {get;set;}="#748182";
    public string Shelf {get;set;}="#485B68";
    public string Deep {get;set;}="#303D50";
    public string Sand {get;set;}="#ABA388";
    public string Snow {get;set;}="#E0E2DD";
    public string Background {get;set;}="#101923";
    public SortedDictionary<int,string> Palette {get;set;}=new(){{1,"#788551"},{2,"#575340"},{4,"#BCBFC5"},{8,"#354B38"},{16,"#B09A65"},{32,"#795043"},{64,"#C3CDD2"},{256,"#3D505E"},{512,"#716A7D"}};
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    internal static CartographicStyle Read(string? path)
    {
        CartographicStyle style;
        if(path==null)style=new();else{
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Style too large.");
            string text=File.ReadAllText(path);using var document=JsonDocument.Parse(text);
            CheckKeys(document.RootElement);
            if(!document.RootElement.TryGetProperty("format",out var format)||format.GetString()!="sp-cartographic-style/1")throw new InvalidDataException("Style format required.");
            style=JsonSerializer.Deserialize<CartographicStyle>(text,Json)??throw new InvalidDataException("Empty style.");
        }
        style.Validate();return style;
    }
    internal static void CheckKeys(JsonElement node)
    {
        if(node.ValueKind!=JsonValueKind.Object)return;
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in node.EnumerateObject()){
            if(!keys.Add(property.Name))throw new InvalidDataException("Duplicate style property.");
            CheckKeys(property.Value);
        }
    }
    internal void Validate()
    {
        static void Range(double n,double min,double max){if(!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException("Invalid style numeric range.");}
        if(Format!="sp-cartographic-style/1"||Palette==null||!Palette.Keys.SequenceEqual(CartographicMapData.KnownBiomes.OrderBy(i=>i)))throw new InvalidDataException("Style palette/format mismatch.");
        Range(FadeMetres,0,500);Range(Exaggeration,0,8);Range(LightAzimuth,0,360);Range(LightAltitude,1,90);Range(Ambient,0,1);Range(Diffuse,0,1);Range(ShelfDepth,.1,1000);Range(DeepRange,.1,2000);Range(ShoreHeight,.1,100);Range(ShoreTint,0,1);
        foreach(string? color in Palette.Values.Concat(new[]{Shallow,Shelf,Deep,Sand,Snow,Background})){if(color==null||!Regex.IsMatch(color,"^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException("Expected opaque #RRGGBB style colors.");}
    }
    internal double Shade(double dx,double dz)
    {
        double az=LightAzimuth*Math.PI/180,alt=LightAltitude*Math.PI/180,nx=-dx*Exaggeration,nz=-dz*Exaggeration;
        double dot=(nx*Math.Sin(az)*Math.Cos(alt)+Math.Sin(alt)+nz*Math.Cos(az)*Math.Cos(alt))/Math.Sqrt(nx*nx+1+nz*nz);
        return Ambient+Diffuse*Math.Max(0,dot);
    }
}

internal static class CartographicFade
{
    // No-flux diffusion on the land adjacency graph. No water edge is crossed.
    // FadeMetres is the nominal 10%-90% width of a straight boundary on a plane.
    internal static float[] Compute(CartographicMapData d,CartographicStyle style)
    {
        int n=d.Size;var a=new float[n*n*4];
        for(int i=0;i<n*n;i++){
            var color=Rgba32.ParseHex(style.Palette[d.Biome[i]]);int k=i*4;
            a[k]=color.R;a[k+1]=color.G;a[k+2]=color.B;a[k+3]=d.Biome[i] is 4 or 64?1:0;
        }
        double sigma=style.FadeMetres/(2.563103*d.Span/n),time=sigma*sigma/2;
        if(time==0)return a;
        double work=Math.Ceiling(time/.2);if(!double.IsFinite(work)||work>128)throw new InvalidDataException("Fade exceeds diffusion work limit at this resolution; reduce fadeMetres.");
        int steps=(int)work;
        float dt=(float)(time/steps);var b=new float[a.Length];
        for(int iteration=0;iteration<steps;iteration++){
            for(int row=0;row<n;row++)for(int col=0;col<n;col++){
                int i=row*n+col,k=i*4;
                for(int channel=0;channel<4;channel++){
                    float v=a[k+channel],delta=0;
                    if(d.Water[i]==0){
                        if(col>0&&d.Water[i-1]==0)delta+=a[k-4+channel]-v;
                        if(col+1<n&&d.Water[i+1]==0)delta+=a[k+4+channel]-v;
                        if(row>0&&d.Water[i-n]==0)delta+=a[k-n*4+channel]-v;
                        if(row+1<n&&d.Water[i+n]==0)delta+=a[k+n*4+channel]-v;
                    }
                    b[k+channel]=v+dt*delta;
                }
            }
            (a,b)=(b,a);
        }
        return a;
    }
}
