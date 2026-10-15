namespace Dango.PhysicsLab;

public static class LabDisplayGeometry
{
    // A sampling diagnostic for visible loops and area; exact extrema are measured separately.
    public static (double Area, bool SelfIntersects) Inspect(IReadOnlyList<float> controls)
    {
        var count=controls.Count/2;
        var points=new (double X,double Y)[count*8];
        for (var i=0;i<count;i++)
        for (var j=0;j<8;j++)
        {
            var t=j/8d;
            var w0=.5*(1-t)*(1-t); var w1=.5+t-t*t; var w2=.5*t*t;
            var a=(i+count-1)%count; var c=(i+1)%count;
            points[8*i+j]=(w0*controls[2*a]+w1*controls[2*i]+w2*controls[2*c],
                w0*controls[2*a+1]+w1*controls[2*i+1]+w2*controls[2*c+1]);
        }
        double area=0;
        for (var i=0;i<points.Length;i++)
        {
            var a=points[i]; var b=points[(i+1)%points.Length];
            area+=a.X*b.Y-a.Y*b.X;
            for (var j=i+2;j<points.Length;j++)
            {
                if (i==0 && j==points.Length-1) continue;
                var c=points[j]; var d=points[(j+1)%points.Length];
                if (Cross(a,b,c)*Cross(a,b,d)<-1e-14 && Cross(c,d,a)*Cross(c,d,b)<-1e-14)
                    return (area/2,true);
            }
        }
        return (area/2,false);
    }

    private static double Cross((double X,double Y) a,(double X,double Y) b,(double X,double Y) c) =>
        (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
}
