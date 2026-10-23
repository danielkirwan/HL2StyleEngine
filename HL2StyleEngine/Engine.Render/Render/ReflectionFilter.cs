using System.Numerics;
using System.Runtime.InteropServices;

namespace Engine.Render;

public static class ReflectionFilter
{
    public const int FaceSize=128, LutSize=128, FilterSamples=256, LutSamples=1024;

    public static Vector3 Studio(Vector3 d)
    {
        // A fixed distant lighting rig, deliberately unrelated to the level's switchable lamps.
        var value=new Vector3(.025f,.03f,.045f)+new Vector3(.12f,.15f,.2f)*MathF.Max(0,d.Y);
        value+=new Vector3(10,7,4)*MathF.Pow(MathF.Max(0,Vector3.Dot(d,Vector3.Normalize(new(-.65f,.55f,.5f)))),80);
        value+=new Vector3(2,5,9)*MathF.Pow(MathF.Max(0,Vector3.Dot(d,Vector3.Normalize(new(.8f,.25f,-.45f)))),40);
        value+=new Vector3(2.5f)*MathF.Pow(MathF.Max(0,d.Y),24);
        return value;
    }

    public static Vector3 Direction(int face,float u,float v) => Vector3.Normalize(face switch
    {
        0=>new Vector3(1,-v,-u), 1=>new Vector3(-1,-v,u),
        2=>new Vector3(u,1,v), 3=>new Vector3(u,-1,-v),
        4=>new Vector3(u,-v,1), 5=>new Vector3(-u,-v,-1),
        _=>throw new ArgumentOutOfRangeException(nameof(face))
    });

    public static ReflectionEnvironmentData Build(Func<Vector3,Vector3> environment,int size=FaceSize,int lutSize=LutSize,int samples=FilterSamples,int lutSamples=LutSamples, CancellationToken cancel=default)
    {
        int mips=1+(int)Math.Log2(size);
        var faces=new byte[mips*6][];
        for(int mip=0;mip<mips;mip++)
        {
            cancel.ThrowIfCancellationRequested();
            int width=Math.Max(1,size>>mip); float rough=mips>1 ? (float)mip/(mips-1) : 0;
            var halfVectors=Enumerable.Range(0,samples).Select(i=>SampleGgx(i,samples,rough)).ToArray();
            for(int face=0;face<6;face++)
            {
                var texels=new Half[width*width*4];
                for(int y=0;y<width;y++) for(int x=0;x<width;x++)
                {
                    if(x==0) cancel.ThrowIfCancellationRequested();
                    var n=Direction(face,2*(x+.5f)/width-1,2*(y+.5f)/width-1);
                    Vector3 value;
                    if(mip==0) value=environment(n);
                    else
                    {
                        var tangent=Vector3.Normalize(Vector3.Cross(MathF.Abs(n.Z)<.999f?Vector3.UnitZ:Vector3.UnitX,n));
                        var bitangent=Vector3.Cross(n,tangent); var sum=Vector3.Zero; float weight=0;
                        foreach(var local in halfVectors)
                        {
                            var h=tangent*local.X+bitangent*local.Y+n*local.Z;
                            var l=Vector3.Normalize(2*Vector3.Dot(n,h)*h-n); float nl=MathF.Max(0,Vector3.Dot(n,l));
                            if(nl>0) {sum+=environment(l)*nl; weight+=nl;}
                        }
                        value=sum/MathF.Max(weight,1e-8f);
                    }
                    int index=(y*width+x)*4;
                    texels[index]=(Half)value.X; texels[index+1]=(Half)value.Y; texels[index+2]=(Half)value.Z; texels[index+3]=(Half)1;
                }
                faces[mip*6+face]=MemoryMarshal.AsBytes(texels.AsSpan()).ToArray();
            }
        }
        var lut=new Half[lutSize*lutSize*2];
        for(int y=0;y<lutSize;y++) for(int x=0;x<lutSize;x++)
        {
            if(x==0) cancel.ThrowIfCancellationRequested();
            var integral=Integrate((x+.5f)/lutSize,(y+.5f)/lutSize,lutSamples);
            lut[(y*lutSize+x)*2]=(Half)integral.X; lut[(y*lutSize+x)*2+1]=(Half)integral.Y;
        }
        var result=new ReflectionEnvironmentData(size,mips,lutSize,faces,MemoryMarshal.AsBytes(lut.AsSpan()).ToArray());
        result.Validate(); return result;
    }

    public static Vector2 Integrate(float nv,float rough,int samples)
    {
        nv=Math.Clamp(nv,.00001f,1); rough=Math.Clamp(rough,.04f,1);
        var v=new Vector3(MathF.Sqrt(1-nv*nv),0,nv); float a2=MathF.Pow(rough,4); var sum=Vector2.Zero;
        for(int i=0;i<samples;i++)
        {
            var h=SampleGgx(i,samples,rough); float vh=MathF.Max(0,Vector3.Dot(v,h));
            var l=2*vh*h-v; float nl=l.Z, nh=h.Z;
            if(nl<=0 || nh<=0) continue;
            float denominator=nl*MathF.Sqrt(nv*nv*(1-a2)+a2)+nv*MathF.Sqrt(nl*nl*(1-a2)+a2);
            float weight=2*nl*vh/MathF.Max(1e-8f,nh*denominator);
            float fresnel=MathF.Pow(1-vh,5);
            sum+=new Vector2(1-fresnel,fresnel)*weight;
        }
        return sum/samples;
    }

    private static Vector3 SampleGgx(int i,int count,float rough)
    {
        uint bits=(uint)i; bits=(bits<<16)|(bits>>16);
        bits=((bits & 0x55555555)<<1)|((bits & 0xAAAAAAAA)>>1);
        bits=((bits & 0x33333333)<<2)|((bits & 0xCCCCCCCC)>>2);
        bits=((bits & 0x0F0F0F0F)<<4)|((bits & 0xF0F0F0F0)>>4);
        bits=((bits & 0x00FF00FF)<<8)|((bits & 0xFF00FF00)>>8);
        float xi=bits*2.3283064365386963e-10f, phi=2*MathF.PI*i/count;
        float a2=MathF.Pow(Math.Clamp(rough,.04f,1),4);
        float cos=MathF.Sqrt((1-xi)/(1+(a2-1)*xi)), sin=MathF.Sqrt(MathF.Max(0,1-cos*cos));
        return new(MathF.Cos(phi)*sin,MathF.Sin(phi)*sin,cos);
    }
}
