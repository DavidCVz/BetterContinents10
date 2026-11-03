using System;

// F03 candidate only: not installed as a replacement for UnityEngine.Random.
// Xorshift128 with a 1812433253 seed recurrence and unsigned modulo range mapping.
// Algorithm/initialization experiment: https://gist.github.com/macklinb/a00be6b616cbf20fa95e4227575fe50b
// Independently expressed here; acceptance remains limited to captured primitive sequences.
public sealed class UnityIntegerRandomCandidate
{
    uint a,b,c,d;
    public UnityIntegerRandomCandidate(int seed)
    {
        unchecked { a=(uint)seed; b=a*1812433253u+1; c=b*1812433253u+1; d=c*1812433253u+1; }
    }
    public uint NextUInt()
    {
        unchecked { uint shifted=a^(a<<11); a=b; b=c; c=d; d=d^(d>>19)^shifted^(shifted>>8); return d; }
    }
    public int Range(int minimum,int maximum)
    {
        if(minimum==maximum)return minimum;
        uint next=NextUInt();
        if(minimum>maximum)return unchecked((int)((uint)minimum-next%(uint)((long)minimum-maximum)));
        return unchecked((int)((uint)minimum+next%(uint)((long)maximum-minimum)));
    }
    public float Value()=>NativeFloat.Div(NextUInt()&0x7FFFFFu,8388607f);
    public float Range(float minimum,float maximum){float r=Value();return NativeFloat.Add(NativeFloat.Mul(minimum,r),NativeFloat.Mul(maximum,NativeFloat.Sub(1f,r)));}
    public uint[] SaveState()=>new[]{a,b,c,d};
    public void RestoreState(uint[] state){if(state==null||state.Length!=4)throw new ArgumentException("Four state words required.");a=state[0];b=state[1];c=state[2];d=state[3];}
}
