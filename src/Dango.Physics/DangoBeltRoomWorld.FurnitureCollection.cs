using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Dango.Physics;

/// <summary>One detached publication; every geometry and ball channel is in room meters.</summary>
public sealed record DangoBeltRoomFurnitureFrame(DangoBeltRoomFrame BeltFrame,
    ulong LastAppliedToken, ReadOnlyCollection<DangoFurnitureCollectionItem> Items)
{
    public ulong WorldGeneration => BeltFrame.OwnerEpoch;
    public ulong SceneRevision => BeltFrame.SceneRevision;
    public ulong PhysicsStep => BeltFrame.Tick;
    public DangoFurnitureCoordinateSpace CoordinateSpace => DangoFurnitureCoordinateSpace.BeltWorldMeters;
}

public sealed unsafe partial class DangoBeltRoomWorld
{
    public const string FurnitureCollectionModel = "belt-room-furniture-r3-v1";
    public const int FurnitureCollectionMaximumItems = 30, FurnitureCollectionFrameSize = 16968;
    public bool SupportsFurnitureCollection { get; private set; }
    private CollectionEnableDelegate? collectionEnable;
    private CollectionReadDelegate? collectionRead;
    private CollectionProbeDelegate? collectionProbe;
    private CollectionPrepareDelegate? collectionPrepare;
    private CollectionTokenDelegate? collectionApply, collectionCancel;
    private NativeCollectionToken? preparedCollectionToken;

    private void LoadFurnitureCollection()
    {
        var capabilities = Export<VersionDelegate>("dango_belt_room_capabilities")();
        if ((capabilities & 64) == 0) return;
        if ((capabilities & 128) == 0 || sizeof(NativeCollectionDefinition) != 112 ||
            sizeof(NativeBallState) != 88 || sizeof(NativeBallContact) != 104 || sizeof(NativeCollectionItem) != 416 ||
            sizeof(NativeCollectionFrame) != FurnitureCollectionFrameSize || sizeof(NativeCollectionProbe) != 48 ||
            sizeof(NativeCollectionToken) != 40 || Export<VersionDelegate>("dango_belt_room_collection_version")() != 1 ||
            Export<VersionDelegate>("dango_belt_room_collection_frame_size")() != FurnitureCollectionFrameSize ||
            Identity("dango_belt_room_collection_model") != FurnitureCollectionModel)
            throw new NotSupportedException("Unsupported belt room furniture collection identity, capabilities or layout.");
        collectionEnable = Export<CollectionEnableDelegate>("dango_belt_room_enable_collection");
        collectionRead = Export<CollectionReadDelegate>("dango_belt_room_read_collection_frame");
        collectionProbe = Export<CollectionProbeDelegate>("dango_belt_room_probe_furniture_change");
        collectionPrepare = Export<CollectionPrepareDelegate>("dango_belt_room_prepare_furniture_change");
        collectionApply = Export<CollectionTokenDelegate>("dango_belt_room_apply_prepared_furniture_change");
        collectionCancel = Export<CollectionTokenDelegate>("dango_belt_room_cancel_prepared_furniture_change");
        SupportsFurnitureCollection = true;
    }
    private void RequireCollection()
    {
        VerifyAccess();
        if (!SupportsFurnitureCollection) throw new NotSupportedException("This belt room module does not support furniture collections.");
    }
    public void EnableFurnitureCollection(ulong expectedSceneRevision)
    {
        RequireCollection();
        lock (NativeGate) Check(collectionEnable!(handle, expectedSceneRevision), "enable furniture collection");
    }
    public DangoBeltRoomFurnitureFrame ReadFurnitureCollection() => ReadFurnitureCollectionFrame();
    public DangoBeltRoomFurnitureFrame ReadFurnitureCollectionFrame()
    {
        RequireCollection(); NativeCollectionFrame native = default;
        lock (NativeGate) Check(collectionRead!(handle, &native, sizeof(NativeCollectionFrame)), "read furniture collection");
        var body = DecodeFrame(native.Base);
        if (native.Enabled != 1 || native.ItemCount is < 0 or > FurnitureCollectionMaximumItems || body.Platform is not null)
            throw InvalidCollectionFrame();
        var result = new DangoFurnitureCollectionItem[native.ItemCount]; var ids = new HashSet<ulong>();
        var items = (NativeCollectionItem*)native.Items;
        for (var i = 0; i < result.Length; i++)
        {
            var item = items[i];
            if (item.Instance == 0 || !ids.Add(item.Instance)) throw InvalidCollectionFrame();
            var definition = item.Definition.ToPublic();
            DangoRoundBallState? ball = null; DangoRoundBallContact? contact = null, surface = null;
            if (definition.Kind == DangoFurnitureKind.RoundBall)
            {
                var b = item.Ball;
                if (b.Id <= 0 || (ulong)b.Id != item.Instance || b.Enabled != 1 || b.Sleeping is < 0 or > 1 ||
                    b.SceneRevision != body.SceneRevision || b.Tick != body.Tick ||
                    !AllFinite(b.X,b.Y,b.Vx,b.Vy,b.Angle,b.Omega)) throw InvalidCollectionFrame();
                ball = new(new(b.Id),b.SceneRevision,b.Tick,b.X,b.Y,b.Vx,b.Vy,b.Angle,b.Omega,b.Sleeping==1,b.Integrations);
                contact = item.BodyContact.ToPublic(body); surface = item.SurfaceContact.ToPublic(body);
            }
            else if (item.Ball.Enabled != 0) throw InvalidCollectionFrame();
            result[i] = new(item.Instance,definition,ball,contact,surface);
        }
        return new(body,native.LastAppliedToken,Array.AsReadOnly(result));
    }
    public DangoFurnitureProbe ProbeFurnitureChange(DangoFurnitureChangeKind kind, ulong targetInstanceId,
        DangoFurnitureDefinition? candidate, ulong expectedSceneRevision)
    {
        RequireCollection(); var definition = CollectionCandidate(kind,candidate);
        lock (NativeGate)
        {
            Check(collectionProbe!(handle,(int)kind,targetInstanceId,expectedSceneRevision,in definition,out var probe,48),"probe furniture change");
            return DecodeProbe(probe);
        }
    }
    public DangoFurniturePreparation PrepareFurnitureChange(DangoFurnitureChangeKind kind, ulong targetInstanceId,
        DangoFurnitureDefinition? candidate, ulong expectedSceneRevision)
    {
        RequireCollection(); var definition = CollectionCandidate(kind,candidate);
        lock (NativeGate)
        {
            Check(collectionPrepare!(handle,(int)kind,targetInstanceId,expectedSceneRevision,in definition,out var raw,48,out var token),"prepare furniture change");
            var probe = DecodeProbe(raw);
            if (!probe.Allowed) return new(probe,null);
            if (token.Generation != worldGeneration || token.TokenId == 0 || token.Revision != raw.Revision ||
                token.Tick != raw.Tick || token.ConfigurationRevision != raw.ConfigurationRevision) throw InvalidCollectionFrame();
            preparedCollectionToken = token;
            return new(probe,new(ownerEpoch,token.TokenId,token.Revision,token.Tick));
        }
    }
    public DangoBeltRoomFurnitureFrame ApplyPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    {
        RequireCollection(); var native = RequireToken(token);
        lock (NativeGate) Check(collectionApply!(handle,in native),"apply furniture change");
        preparedCollectionToken = null;
        var frame = ReadFurnitureCollectionFrame();
        if (frame.LastAppliedToken != token.TokenId || frame.SceneRevision != token.SceneRevision + 1 || frame.PhysicsStep != token.PhysicsStep ||
            frame.BeltFrame.ConfigurationRevision != native.ConfigurationRevision || frame.BeltFrame.Fault != 0)
            throw InvalidCollectionFrame();
        return frame;
    }
    public void CancelPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    {
        RequireCollection(); var native = RequireToken(token);
        lock (NativeGate) Check(collectionCancel!(handle,in native),"cancel furniture change");
        preparedCollectionToken = null;
    }
    private NativeCollectionToken RequireToken(DangoPreparedFurnitureChange token)
    {
        if (token.WorldGeneration != ownerEpoch || preparedCollectionToken is not { } native ||
            native.TokenId != token.TokenId || native.Revision != token.SceneRevision || native.Tick != token.PhysicsStep)
            throw new ArgumentException("Furniture permit is stale or belongs to another owner.",nameof(token));
        return native;
    }
    private DangoFurnitureProbe DecodeProbe(NativeCollectionProbe probe)
    {
        if (probe.Generation != worldGeneration || probe.ConfigurationRevision == 0 || !double.IsFinite(probe.MinimumGap) ||
            probe.Reserved != 0 || probe.Rejection is < 0 or > 9) throw InvalidCollectionFrame();
        return new(ownerEpoch,probe.Revision,probe.Tick,probe.MinimumGap,(DangoFurnitureChangeRejection)probe.Rejection);
    }
    private static NativeCollectionDefinition CollectionCandidate(DangoFurnitureChangeKind kind,DangoFurnitureDefinition? candidate)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == DangoFurnitureChangeKind.Delete)
        {
            if (candidate is not null) throw new ArgumentException("Delete cannot carry a definition.",nameof(candidate));
            return default;
        }
        ArgumentNullException.ThrowIfNull(candidate); candidate.Validate();
        if (candidate.CoordinateSpace != DangoFurnitureCoordinateSpace.BeltWorldMeters)
            throw new ArgumentException("Belt furniture must explicitly use room meters.",nameof(candidate));
        var result = new NativeCollectionDefinition { Kind = (int)candidate.Kind };
        if (candidate.Platform is { } p) { result.X=p.CenterX;result.Y=p.CenterY;result.Width=p.Width;result.Height=p.Height;result.Friction=p.Friction; }
        if (candidate.Ball is { } b) result.Ball = new() { X=b.CenterQX,Y=b.CenterQY,Radius=b.RadiusQ,Mass=b.Mass,FloorFriction=b.FloorFriction,DangoFriction=b.DangoFriction,FloorRestitution=b.FloorRestitution,DangoRestitution=b.DangoRestitution };
        return result;
    }
    private static InvalidOperationException InvalidCollectionFrame() => new("Native belt room returned an invalid furniture collection publication.");
    private static bool AllFinite(params double[] values) => values.All(double.IsFinite);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionEnableDelegate(nint world,ulong revision);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionReadDelegate(nint world,NativeCollectionFrame* frame,int bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionProbeDelegate(nint world,int kind,ulong target,ulong revision,in NativeCollectionDefinition definition,out NativeCollectionProbe probe,int bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionPrepareDelegate(nint world,int kind,ulong target,ulong revision,in NativeCollectionDefinition definition,out NativeCollectionProbe probe,int bytes,out NativeCollectionToken token);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionTokenDelegate(nint world,in NativeCollectionToken token);
    [StructLayout(LayoutKind.Sequential)] private struct NativeCollectionToken { public ulong Generation,TokenId,Revision,Tick,ConfigurationRevision; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeCollectionProbe { public ulong Generation,Revision,Tick,ConfigurationRevision;public double MinimumGap;public int Rejection,Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBallDefinition { public double X,Y,Radius,Mass,FloorFriction,DangoFriction,FloorRestitution,DangoRestitution; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBallState { public int Id,Enabled,Sleeping;public ulong SceneRevision,Tick,Integrations;public double X,Y,Vx,Vy,Angle,Omega; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBallContact
    {
        public int ObjectId,Touching,Sample;public ulong Revision,Tick;
        public double Fraction,Nx,Ny,Gap,NormalImpulse,TangentImpulse,NormalSpeedBefore,TangentSpeedBefore,BallSpeedBefore;
        public readonly DangoRoundBallContact ToPublic(DangoBeltRoomFrame body)
        {
            if (Touching is < 0 or > 1 || Revision != body.SceneRevision || Tick != body.Tick ||
                !AllFinite(Fraction,Nx,Ny,Gap,NormalImpulse,TangentImpulse,NormalSpeedBefore,TangentSpeedBefore,BallSpeedBefore)) throw InvalidCollectionFrame();
            return new(ObjectId,Touching==1,Sample,Revision,Tick,Fraction,Nx,Ny,Gap,NormalImpulse,TangentImpulse,NormalSpeedBefore,TangentSpeedBefore,BallSpeedBefore);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeCollectionDefinition
    {
        public int Kind,Reserved;public double X,Y,Width,Height,Friction;public NativeBallDefinition Ball;
        public readonly DangoFurnitureDefinition ToPublic()
        {
            if (Reserved != 0 || Kind is < 1 or > 2) throw InvalidCollectionFrame();
            var result = new DangoFurnitureDefinition((DangoFurnitureKind)Kind,
                Kind==1 ? new(X,Y,Width,Height,Friction) : null,
                Kind==2 ? new(Ball.X,Ball.Y,Ball.Radius,Ball.Mass,Ball.FloorFriction,Ball.DangoFriction,Ball.FloorRestitution,Ball.DangoRestitution) : null)
                { CoordinateSpace=DangoFurnitureCoordinateSpace.BeltWorldMeters };
            result.Validate();return result;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeCollectionItem { public ulong Instance;public NativeCollectionDefinition Definition;public NativeBallState Ball;public NativeBallContact BodyContact,SurfaceContact; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeCollectionFrame { public NativeFrame Base;public ulong LastAppliedToken;public int ItemCount,Enabled;public fixed byte Items[416*FurnitureCollectionMaximumItems]; }
}
