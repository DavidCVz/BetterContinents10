using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Explicit observation/storage boundaries for the inferred primitive candidates.
// Not used by F02 terrain. Reinterpretation forces storage without allocating byte arrays.
static class NativeFloat
{
    [StructLayout(LayoutKind.Explicit)] struct Word { [FieldOffset(0)] public float Single; [FieldOffset(0)] public int Integer; }
    [MethodImpl(MethodImplOptions.NoInlining|MethodImplOptions.NoOptimization)] static float R(float v){var a=new Word{Single=v};var b=new Word{Integer=a.Integer};return b.Single;}
    public static float Add(float a,float b)=>R(a+b);
    public static float Sub(float a,float b)=>R(a-b);
    public static float Mul(float a,float b)=>R(a*b);
    public static float Div(float a,float b)=>R(a/b);
}
