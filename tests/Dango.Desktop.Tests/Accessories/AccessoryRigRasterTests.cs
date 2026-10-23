using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>No Window, Show, compositor loop or global input. Explicit DPI exercises the production CPU raster and WritePixels only.</summary>
[TestClass]
public sealed class AccessoryRigRasterTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void MaximumRigRestMeshSamplesOriginalTextureIncludingAlphaEdgesAndPivot(int dpi)
    {
        var texture=Texture(out var original);var rig=Definition();var skin=new AccessoryRigSkin(rig);
        var solver=new AccessoryRigSolver(rig,new(Softness:0));
        solver.Advance(0,1d/60,new(new(.213*256,.04*256),Scale:256));
        var frame=skin.Deform(solver.Refresh());
        Assert.AreEqual(1089,frame.Vertices.Count);Assert.AreEqual(2048,frame.TriangleIndices.Count/3);
        Assert.AreEqual(frame.ImagePivot,frame.Vertices[skin.PivotVertexIndex]);
        Assert.AreEqual(new Point(.213,.04),frame.TextureCoordinates[skin.PivotVertexIndex]);
        var visual=new AccessoryMeshVisual();visual.SetTexture(texture);
        var quad=new AccessoryMeshVisual();quad.SetTexture(texture);
        var reference=new AccessoryMeshFrame(0,Array.AsReadOnly(new Point[]{new(0,0),new(256,0),new(0,256),new(256,256)}),
            Array.AsReadOnly(new Point[]{new(0,0),new(1,0),new(0,1),new(1,1)}),Array.AsReadOnly(new[]{0,1,2,1,3,2}),
            new(0,0,256,256),frame.ImagePivot,new(),0);
        var batch=AccessoryMeshVisual.ApplyBatch([(visual,frame),(quad,reference)],new(dpi,dpi));
        Assert.HasCount(0,batch.Errors);
        var actual=visual.CaptureBitmap()!;var expected=quad.CaptureBitmap()!;
        Assert.AreEqual(expected.PixelWidth,actual.PixelWidth);Assert.AreEqual(expected.PixelHeight,actual.PixelHeight);
        var rendered=Pixels(actual);var baseline=Pixels(expected);
        var maxQuad=0;var maxSource=0;long difference=0;var transparent=0;var semitransparent=0;var opaque=0;
        var size=256*dpi;
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {
            // The production bitmap has a one-pixel safety margin independent of explicit DPI.
            var at=(y+1)*actual.PixelWidth+x+1;
            var source=SourceSample(original,x,y,size);
            var a=(uint)source>>24;if(a==0)transparent++;else if(a==255)opaque++;else semitransparent++;
            for(var shift=0;shift<32;shift+=8)
            {
                var channel=((uint)rendered[at]>>shift)&255;
                var quadDelta=Math.Abs((int)channel-(int)(((uint)baseline[at]>>shift)&255));
                var sourceDelta=Math.Abs((int)channel-(int)(((uint)source>>shift)&255));
                maxQuad=Math.Max(maxQuad,quadDelta);maxSource=Math.Max(maxSource,sourceDelta);difference+=sourceDelta;
            }
        }
        Assert.IsTrue(transparent>100 && semitransparent>100 && opaque>100,"Fixture must cover original transparency and color, not an opaque proxy.");
        Assert.IsTrue(maxQuad<=2,$"New rig topology differs from independent quad sampling by {maxQuad} channel levels.");
        Assert.IsTrue(maxSource<=2,$"Original source reconstruction differs by {maxSource} channel levels.");
        Assert.IsTrue((double)difference/(size*size*4)<.1,"Dense topology cannot hide alpha seams in aggregate error.");
        Assert.IsTrue(rendered.Any(p=>p!=0));
        TestContext.WriteLine($"No-window rig/rest original-texture check DPI={dpi}x: max-channel delta quad={maxQuad}, independent source={maxSource}; mean source delta={(double)difference/(size*size*4):F6}; transparent/partial/opaque samples={transparent}/{semitransparent}/{opaque}; capture={actual.PixelWidth}x{actual.PixelHeight}; exact pivot={frame.ImagePivot}; AllowFolding={frame.AllowFolding}.");
    }

    [STATestMethod]
    [TestCategory("E0Performance")]
    [DataRow(1,1)]
    [DataRow(8,1)]
    [DataRow(1,2)]
    [DataRow(8,2)]
    public void MaximumRigDynamicApplyBatchRecordsProductionRasterAndUploadWithoutWindow(int count,int dpi)
        => MeasureDynamicBatch(count,dpi,false);

    [STATestMethod]
    [TestCategory("E0Performance")]
    public void ExplicitDecimatedEngineeringCandidateEightItemsTwoDpiRecordsCostsWithoutQualityClaim()
        => MeasureDynamicBatch(8,2,true);

    private void MeasureDynamicBatch(int count,int dpi,bool decimate)
    {
        const int warmup=120,samples=120;
        var texture=Texture(out _);var rig=Definition();var skin=new AccessoryRigSkin(rig);
        var candidate=decimate?new DecimatedCandidate(skin.TextureCoordinates,rig.RestNodes[0]):null;
        var solvers=Enumerable.Range(0,count).Select(_=>new AccessoryRigSolver(rig,new(.65,6,5,1))).ToArray();
        var visuals=Enumerable.Range(0,count).Select(_=>new AccessoryMeshVisual()).ToArray();
        foreach(var visual in visuals)visual.SetTexture(texture);
        var requests=new(AccessoryMeshVisual Visual,AccessoryMeshFrame Frame)[count];
        var total=new double[samples];var solve=new double[samples];var preparation=new double[samples];var raster=new double[samples];var upload=new double[samples];
        var rerasterized=0;var changed=false;AccessoryMeshFrame? first=null,last=null;
        var allocationStart=0L;
        for(var tick=0;tick<warmup+samples;tick++)
        {
            if(tick==warmup)allocationStart=GC.GetAllocatedBytesForCurrentThread();
            var start=Stopwatch.GetTimestamp();
            for(var i=0;i<count;i++)
            {
                solvers[i].Advance(tick,1d/60,new(new(256*(.213+.025*Math.Sin(tick*.1+i)),256*(.04+.015*Math.Cos(tick*.07+i))),.04*Math.Sin(tick*.09+i),256));
                var complete=skin.Deform(solvers[i].Refresh());
                requests[i]=(visuals[i],candidate is null?complete:candidate.Select(complete));
            }
            var solveMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var batch=AccessoryMeshVisual.ApplyBatch(requests,new(dpi,dpi));
            var totalMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.HasCount(0,batch.Errors,"Production frame validation, fold coverage or upload rejected the new rig frame.");
            if(tick<warmup)continue;
            var index=tick-warmup;total[index]=totalMs;solve[index]=solveMs;preparation[index]=batch.PreparationMilliseconds;raster[index]=batch.RasterMilliseconds;upload[index]=batch.UploadMilliseconds;
            if(batch.RasterMilliseconds>0 && visuals.All(v=>v.LastRasterMilliseconds>0))rerasterized++;
            first??=requests[0].Frame;last=requests[0].Frame;
            changed|=first.Vertices.Where((p,j)=>((p-first.ImagePivot)-(last.Vertices[j]-last.ImagePivot)).Length>0.01).Any();
        }
        var allocated=GC.GetAllocatedBytesForCurrentThread()-allocationStart;
        Assert.AreEqual(samples,rerasterized,"Pure-translation caching must not masquerade as dynamic mesh rasterization.");
        Assert.IsTrue(changed,"Fixture must change relative shape throughout the measurement.");
        foreach(var visual in visuals)
        {
            var capture=visual.CaptureBitmap();Assert.IsNotNull(capture);
            Assert.IsTrue(Pixels(capture).Any(p=>p!=0),"Raster/capture must contain actual synthetic texture pixels.");
        }
        var process=Process.GetCurrentProcess();
        TestContext.WriteLine($"No-window production ApplyBatch {count} items, explicit {dpi}x DPI, 1024x1024 Pbgra32 original texture, 256x256 DIP base mesh, 17 nodes/{last!.Vertices.Count} vertices/{last.TriangleIndices.Count/3} triangles/item, AllowFolding=true; {warmup} warmup + {samples} measured frames; CPU logical={Environment.ProcessorCount}, runtime={Environment.Version}; explicit test-only decimation={decimate}.");
        if(candidate is not null)TestContext.WriteLine($"Engineering candidate grid={candidate.Columns}x{candidate.Rows}, every second original grid axis plus exact pivot/endpoints; vertices sampled from full 1089-vertex solved snapshot. Full solve/mesh and decimation costs are included. No runtime adaptation or dynamic image quality acceptance is implied.");
        TestContext.WriteLine($"milliseconds median/p95/max: total={Stats(total)}; solver+mesh={Stats(solve)}; preparation={Stats(preparation)}; parallel raster wall={Stats(raster)}; WritePixels upload={Stats(upload)}; UI-thread allocated={allocated/samples:N0} bytes/frame; testhost peak working set={process.PeakWorkingSet64/1048576d:F1} MiB. This excludes compositor/presentation and manual acceptance.");
    }

    private static AccessoryRigDefinition Definition()=>new(Enumerable.Range(0,17).Select(i=>new Point(.213,.04+i*.056)));
    private static string Stats(double[] data){var sorted=data.Order().ToArray();return $"{sorted[sorted.Length/2]:F3}/{sorted[(int)(sorted.Length*.95)]:F3}/{sorted[^1]:F3}";}
    private static int[] Pixels(BitmapSource bitmap){var pixels=new int[bitmap.PixelWidth*bitmap.PixelHeight];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;}

    private sealed class DecimatedCandidate
    {
        private readonly int[] selected;
        private readonly IReadOnlyList<Point> uvs;
        private readonly IReadOnlyList<int> triangles;
        public int Columns { get; }
        public int Rows { get; }
        public DecimatedCandidate(IReadOnlyList<Point> original,Point pivot)
        {
            var xs=original.Select(p=>p.X).Distinct().Order().ToArray();
            var ys=original.Select(p=>p.Y).Distinct().Order().ToArray();
            var xIndices=Enumerable.Range(0,xs.Length).Where(i=>i%2==0 || i==xs.Length-1 || xs[i]==pivot.X).ToArray();
            var yIndices=Enumerable.Range(0,ys.Length).Where(i=>i%2==0 || i==ys.Length-1 || ys[i]==pivot.Y).ToArray();
            Columns=xIndices.Length;Rows=yIndices.Length;
            selected=yIndices.SelectMany(y=>xIndices.Select(x=>y*xs.Length+x)).ToArray();
            uvs=Array.AsReadOnly(selected.Select(i=>original[i]).ToArray());
            var indices=new List<int>();
            for(var y=0;y<Rows-1;y++)for(var x=0;x<Columns-1;x++)
            {
                var a=y*Columns+x;indices.AddRange([a,a+1,a+Columns,a+1,a+Columns+1,a+Columns]);
            }
            triangles=indices.AsReadOnly();
            Assert.Contains(pivot,uvs,"Decimation must retain the exact original pivot UV.");
        }
        public AccessoryMeshFrame Select(AccessoryMeshFrame complete)
        {
            var vertices=selected.Select(i=>complete.Vertices[i]).ToArray();var bounds=Rect.Empty;
            foreach(var p in vertices)bounds.Union(p);
            return complete with {Vertices=Array.AsReadOnly(vertices),TextureCoordinates=uvs,TriangleIndices=triangles,Bounds=bounds};
        }
    }

    private static BitmapSource Texture(out int[] pixels)
    {
        const int edge=1024;pixels=new int[edge*edge];
        for(var y=0;y<edge;y++)for(var x=0;x<edge;x++)
        {
            var dx=(x-edge*.5)/(edge*.45);var dy=(y-edge*.5)/(edge*.45);var radius=Math.Sqrt(dx*dx+dy*dy);
            var alpha=radius>.98?0:radius>.9?(int)((.98-radius)/.08*128):radius>.7?128:255;
            var red=(x*255/(edge-1))*alpha/255;var green=(y*255/(edge-1))*alpha/255;
            var blue=(((x/17+y/23)%2==0)?224:64)*alpha/255;
            pixels[y*edge+x]=alpha<<24|red<<16|green<<8|blue;
        }
        var texture=BitmapSource.Create(edge,edge,96,96,PixelFormats.Pbgra32,null,pixels,edge*4);texture.Freeze();return texture;
    }

    // At these 1024->256/512 scales every sample lands halfway between source texels.
    // Scalar rounded channel averages provide an oracle independent of renderer geometry and packed interpolation.
    private static int SourceSample(int[] original,int x,int y,int outputSize)
    {
        var sx=(int)((x+.5)*1024/outputSize-.5);var sy=(int)((y+.5)*1024/outputSize-.5);var result=0;
        for(var shift=0;shift<32;shift+=8)
        {
            int Channel(int at)=>(int)(((uint)original[at]>>shift)&255);
            var top=(Channel(sy*1024+sx)+Channel(sy*1024+sx+1)+1)/2;
            var bottom=(Channel((sy+1)*1024+sx)+Channel((sy+1)*1024+sx+1)+1)/2;
            result|=((top+bottom+1)/2)<<shift;
        }
        return result;
    }
}
