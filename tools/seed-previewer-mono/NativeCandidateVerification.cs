using System;
using System.IO;
using System.Globalization;

// Offline native-primitive candidate check. Does not patch the game or run terrain.
static class NativeCandidateVerification
{
    static float F(string bits)=>BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(bits,16)),0);
    static string B(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
    public static int Run(string fixture,string output)
    {
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("Output must be new.");
        int perlin=0,pdiff=0,steps=0,rdiff=0,states=0,sdiff=0,seeds=0;
        foreach(var line in File.ReadAllLines(Path.Combine(fixture,"perlin.tsv"))){
            var c=line.Split('\t');float actual=UnityPerlinCandidate.Normalized(F(c[0]),F(c[1]),true,true);
            if(B(actual)!=c[2])pdiff++;perlin++;
        }
        var initial=new System.Collections.Generic.Dictionary<int,uint[]>();
        foreach(var line in File.ReadAllLines(Path.Combine(fixture,"initial.tsv"))){var c=line.Split('\t');initial.Add(int.Parse(c[0],CultureInfo.InvariantCulture),new[]{Convert.ToUInt32(c[1],16),Convert.ToUInt32(c[2],16),Convert.ToUInt32(c[3],16),Convert.ToUInt32(c[4],16)});}
        UnityIntegerRandomCandidate rng=null;int current=0;
        foreach(var line in File.ReadAllLines(Path.Combine(fixture,"random.tsv"))){
            var c=line.Split('\t');int seed=int.Parse(c[0],CultureInfo.InvariantCulture),op=int.Parse(c[1],CultureInfo.InvariantCulture);
            if(rng==null||seed!=current){current=seed;rng=new UnityIntegerRandomCandidate(seed);seeds++;var s=rng.SaveState();for(int i=0;i<4;i++){if(s[i]!=initial[seed][i])sdiff++;states++;}}
            int integer=0;float number=0;
            switch(op){case 0:integer=rng.Range(-10000,10000);break;case 1:integer=rng.Range(int.MinValue,int.MaxValue);break;case 2:number=rng.Range(-1f,1f);break;case 3:number=rng.Value();break;case 4:integer=rng.Range(0,1);break;case 5:integer=rng.Range(-7,3);break;case 6:integer=rng.Range(5,5);break;case 7:integer=rng.Range(9,-4);break;default:throw new InvalidDataException("Unknown RNG operation.");}
            if(op==2||op==3){if(B(number)!=c[4])rdiff++;}else if(integer!=int.Parse(c[3],CultureInfo.InvariantCulture))rdiff++;
            var state=rng.SaveState();for(int i=0;i<4;i++){if(state[i]!=Convert.ToUInt32(c[5+i],16))sdiff++;states++;}steps++;
        }
        if(perlin!=625||steps!=1024||seeds!=8)throw new InvalidDataException("Incomplete native fixture.");
        var saved=rng.SaveState();uint next=rng.NextUInt();rng.RestoreState(saved);if(next!=rng.NextUInt())throw new Exception("State roundtrip failed.");
        File.WriteAllText(output,"{\"PerlinSamples\":"+perlin+",\"PerlinDifferences\":"+pdiff+",\"RandomSteps\":"+steps+",\"RandomDifferences\":"+rdiff+",\"StateWords\":"+states+",\"StateDifferences\":"+sdiff+",\"Limitation\":\"Captured primitive domain only; no terrain parity acceptance.\"}");
        System.Console.WriteLine("Native candidates: Perlin differences="+pdiff+", RNG differences="+rdiff+", state differences="+sdiff);
        return pdiff+rdiff+sdiff==0?0:2;
    }
}
