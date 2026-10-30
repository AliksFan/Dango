using System.Security.Cryptography;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>
/// Opt-in independent-binary native trajectory gate. Run this same test assembly once with the
/// reference Desktop DLL and once with the candidate DLL, in two distinct test processes.
/// No window, wall-clock cadence, handle or private process state enters a frame digest.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AccessoryRuntimeOutputEquivalenceTests
{
    private const string BaselinePathVariable = "DANGO_ACCESSORY_EQUIVALENCE_BASELINE_PATH";
    private const string WriteVariable = "DANGO_ACCESSORY_EQUIVALENCE_WRITE_BASELINE";
    private const int Schema = 2;
    private const int FrameCount = 300 + 180 + 180 + 180 + 30 + 60;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(.02);
    private static readonly LogicalRect Bounds = new(400, 350, 160, 120);
    private static readonly PhysicsPresentationInput Input = new("fictional-runtime-equivalence", Bounds,
        DangoActionState.Idle, 0, 1, "fictional-ground", new(0, 1), 0, 1, false, true, []);
    private static readonly JsonSerializerOptions CanonicalJson = new() { WriteIndented = false };
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("IndependentBinaryEquivalence")]
    public void IndependentDesktopBinariesProduceIdenticalCompleteNativeAndAccessoryTrajectoryDigests()
    {
        var baselinePath = Environment.GetEnvironmentVariable(BaselinePathVariable);
        if (string.IsNullOrWhiteSpace(baselinePath))
        {
            Assert.Inconclusive($"Opt-in independent-binary gate: set {BaselinePathVariable}; set {WriteVariable}=1 only in the reference process, then verify with a different Desktop DLL/process.");
            return;
        }
        baselinePath = Path.GetFullPath(baselinePath);
        var write = Environment.GetEnvironmentVariable(WriteVariable) == "1";
        var desktopSha = FileSha(typeof(StackedDangoSceneRuntime).Assembly.Location);
        var nativePath = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll");
        var nativeSha = FileSha(nativePath);
        var configuration = Configuration();
        var fixtureSha = Digest(new
        {
            schema = Schema,
            trajectory = "settle300-D180-A180-jump180-squash30-recovery60",
            quietIdleHostProtocol = "settle:first270=false,last30=true;D=true;A=true;jump=false;squash=false;recovery=false",
            autonomousOutputAllowed = false,
            wanderScreenWidthEnabled = false,
            fixedStepTicks = Step.Ticks,
            input = Input,
            accessories = configuration.Items.Select(CanonicalItem).ToArray(),
        });
        Baseline? reference = null;
        if (!write)
        {
            Assert.IsTrue(File.Exists(baselinePath), "The independent reference recording must already exist.");
            reference = JsonSerializer.Deserialize<Baseline>(File.ReadAllBytes(baselinePath), CanonicalJson);
            Assert.IsNotNull(reference);
            Assert.AreEqual(Schema, reference.Schema);
            Assert.AreEqual(nativeSha, reference.NativeSha256, "Both processes must use the identical actual native module.");
            Assert.AreEqual(fixtureSha, reference.FixtureSha256, "Both processes must use the identical trajectory and fictional accessory configuration.");
            Assert.AreNotEqual(reference.ReferenceDesktopSha256, desktopSha,
                "A recording of this same Desktop DLL cannot serve as an independent implementation reference.");
            Assert.AreNotEqual(reference.WriterProcessId, Environment.ProcessId,
                "Reference recording and candidate verification must run in separate processes.");
            Assert.HasCount(FrameCount, reference.Frames);
        }

        var settingsRoot = Path.Combine(TestRepository.Root, "artifacts", "accessory-runtime-equivalence", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(settingsRoot);
        session.Stage(configuration);
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(nativePath);
        runtime.BindAccessories(session);
        runtime.SetAccessories(configuration);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start();
        runtime.SetViewport(new(0, 0, 2160, 1280));
        var idleOwner = new object();
        runtime.AttachQuietIdleHost(idleOwner, false);
        runtime.UpdateWanderScreenWidth(idleOwner, null);
        var frames = new List<FrameDigest>(FrameCount);
        var gaitNonIdentity = 0;
        var airborne = 0;
        var rising = 0;
        var falling = 0;
        var landed = false;
        var previousY = 0f;
        var rightStart = 0d;
        var rightEnd = 0d;
        var leftStart = 0d;
        var leftEnd = 0d;
        Run("settle", 270, _ => DirectionalControlIntent.None);
        // The final 0.6 seconds provide actual eligible neutral steps for the gait's
        // 0.5-second calibration. Quiet-idle cannot reach its first blink (>=4 seconds
        // after scheduling); the following held D/A immediately reset that schedule.
        // No fake gait state or additional native frame is introduced.
        runtime.UpdateQuietIdleHost(idleOwner, true);
        Run("settle", 30, _ => DirectionalControlIntent.None);
        rightStart = runtime.CurrentSelectedCenter!.Value.X;
        Run("D", 180, _ => new(false, true, false, false));
        rightEnd = runtime.CurrentSelectedCenter!.Value.X;
        leftStart = rightEnd;
        Run("A", 180, _ => new(true, false, false, false));
        leftEnd = runtime.CurrentSelectedCenter!.Value.X;
        runtime.UpdateQuietIdleHost(idleOwner, false);
        Assert.IsTrue(runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
            "The real native jump must begin from a grounded walking body.");
        previousY = runtime.LastPhysicsFrame.Bodies.Single().CoreY;
        Run("jump", 180, index => new(false, false, index == 0, false));
        Run("squash", 30, _ => new(false, false, false, true));
        Run("recovery", 60, _ => DirectionalControlIntent.None);
        Assert.HasCount(FrameCount, frames);
        Assert.IsTrue(rightEnd > rightStart + 1 && leftEnd < leftStart - 1,
            "Both directions must move the actual native body, not only change its input flag.");
        Assert.IsTrue(airborne >= 5 && rising >= 3 && falling >= 3 && landed,
            $"Actual jump must leave, rise, fall and land: airborne={airborne}, rising={rising}, falling={falling}, landed={landed}.");
        Assert.IsTrue(gaitNonIdentity > 0, "The trajectory must exercise a real nonidentity gait output.");
        var evidence = new TrajectoryEvidence(rightEnd - rightStart, leftEnd - leftStart,
            airborne, rising, falling, landed, gaitNonIdentity);

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            var recording = new Baseline(Schema, desktopSha, nativeSha, fixtureSha,
                Environment.ProcessId, frames.ToArray(), evidence);
            // A reference may only be created. Existing evidence is never overwritten or repaired.
            using var output = new FileStream(baselinePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, recording, CanonicalJson);
            TestContext.WriteLine($"Created independent reference: {baselinePath}; Desktop SHA256={desktopSha}; native SHA256={nativeSha}; frames={frames.Count}.");
        }
        else
        {
            for (var index = 0; index < frames.Count; index++)
            {
                Assert.AreEqual(reference!.Frames[index].Step, frames[index].Step);
                Assert.AreEqual(reference.Frames[index].Phase, frames[index].Phase);
                Assert.AreEqual(reference.Frames[index].Sha256, frames[index].Sha256,
                    $"Complete output differs from the independent Desktop reference at step={frames[index].Step}, phase={frames[index].Phase}.");
            }
            Assert.AreEqual(reference!.Evidence, evidence,
                "Observed physical movement, jump and gait evidence must also match the reference trajectory.");
            TestContext.WriteLine($"Verified {frames.Count} complete output digests against {baselinePath}; reference Desktop SHA256={reference.ReferenceDesktopSha256}; candidate Desktop SHA256={desktopSha}; native SHA256={nativeSha}.");
        }

        void Run(string phase, int count, Func<int, DirectionalControlIntent> intentFor)
        {
            for (var index = 0; index < count; index++)
            {
                var intent = intentFor(index);
                runtime.SetDirectionalControl(intent);
                var frame = runtime.Tick(Input, Step);
                var step = frames.Count + 1;
                Assert.IsNull(runtime.LastError, $"{phase} step={step}");
                Assert.IsNull(runtime.AttachmentError);
                Assert.IsNull(runtime.GaitError);
                Assert.IsNull(runtime.AccessoryError);
                Assert.IsFalse(runtime.HasAutonomousAction,
                    "The explicit host protocol must not produce quiet-idle, wander or doze output.");
                var physical = runtime.LastPhysicsFrame!;
                Assert.IsTrue(physical.Succeeded);
                Assert.HasCount(1, physical.Bodies);
                Assert.HasCount(1, frame.Bodies);
                Assert.AreEqual(step, frame.Bodies[0].Diagnostics.SimulatedStepCount,
                    "Every captured output must correspond to precisely one actual native step.");
                Assert.IsNotNull(frame.Bodies[0].Attachments);
                Assert.IsNotNull(frame.Bodies[0].Attachments!.Hat,
                    "This records the ordinary product path with its legacy hat consumer intact.");
                if (frame.Gait is { IsIdentity: false }) gaitNonIdentity++;
                var accessories = runtime.GetAccessoryFrames(frame, 0);
                Assert.HasCount(3, accessories, "All three real equipped accessories must contribute their mesh output.");
                CollectionAssert.AreEqual(new[] { "fictional-back-bowler", "fictional-front-leaf", "fictional-front-hat" },
                    accessories.Select(accessory => accessory.Item.Id).ToArray(),
                    "The recording must preserve behind/front layers and their deterministic order.");
                foreach (var accessory in accessories)
                {
                    Assert.AreEqual((long)step, accessory.Mesh.StepIndex,
                        "Accessory geometry must advance exactly once with each captured native frame.");
                    Assert.IsTrue(accessory.Mesh.Vertices.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)),
                        "A real equipped accessory must emit a finite mesh.");
                }
                if (phase == "jump")
                {
                    var body = physical.Bodies[0];
                    if (!body.IsGrounded)
                    {
                        airborne++;
                        if (body.CoreY > previousY + 1e-4) rising++;
                        if (body.CoreY < previousY - 1e-4) falling++;
                    }
                    if (airborne > 0 && body.IsGrounded && body.GroundContactCount > 0) landed = true;
                    previousY = body.CoreY;
                }
                frames.Add(new(step, phase, Digest(new
                {
                    step,
                    phase,
                    intent,
                    native = new
                    {
                        physical.ErrorCode,
                        physical.ContactStepCount,
                        physical.Succeeded,
                        bodies = physical.Bodies.Select(body => new
                        {
                            entity = body.EntityId.Value,
                            contourBits = body.Contour.Select(value => Bits(value)).ToArray(),
                            coreXBits = Bits(body.CoreX),
                            coreYBits = Bits(body.CoreY),
                            signedAreaBits = Bits(body.SignedArea),
                            centroidXBits = Bits(body.CentroidX),
                            centroidYBits = Bits(body.CentroidY),
                            maximumSpeedBits = Bits(body.MaximumSpeed),
                            body.GroundContactCount,
                            body.IsGrounded,
                            body.SelectedDragNode,
                            body.PointCount,
                        }).ToArray(),
                    },
                    bodies = frame.Bodies.Select(CanonicalBody).ToArray(),
                    authoritativeBounds = frame.AuthoritativeBounds.Select(RectBits).ToArray(),
                    renderOffsets = frame.RenderOffsets.Select(offset => new[] { Bits(offset.X), Bits(offset.Y) }).ToArray(),
                    frame.ContactCount,
                    adjacentContacts = frame.AdjacentContacts.ToArray(),
                    frame.ConstraintsReleased,
                    maximumNodeSpeedBits = Bits(frame.MaximumNodeSpeed),
                    entityIds = frame.EntityIds.Select(entity => entity.Value).ToArray(),
                    colorSlots = frame.ColorSlots.ToArray(),
                    gait = frame.Gait is null ? null : new
                    {
                        entity = frame.Gait.EntityId.Value,
                        frame.Gait.Generation,
                        frame.Gait.StepIndex,
                        geometryBits = new[] { Bits(frame.Gait.CenterX), Bits(frame.Gait.BottomY), Bits(frame.Gait.ScaleX), Bits(frame.Gait.ScaleY) },
                        frame.Gait.IsIdentity,
                        frame.Gait.IsValid,
                    },
                    gaitStateBits = new[] { Bits(runtime.Gait.Phase), Bits(runtime.Gait.Strain) },
                    accessories = accessories.Select(CanonicalAccessory).ToArray(),
                })));
            }
        }
    }

    private static object CanonicalBody(PhysicsPresentationFrame body)
    {
        var attachment = body.Attachments!;
        var shape = attachment.SourceShape!;
        Assert.IsNotNull(shape);
        return new
        {
            body.IsVisible,
            body.FallbackReason,
            body.Diagnostics,
            dozeBits = body.DozeSeconds is { } doze ? Bits(doze) : (long?)null,
            contourBits = body.Contour.Select(point => new[] { Bits(point.X), Bits(point.Y) }).ToArray(),
            eyes = AttachmentBits(body.Eyes),
            leftEye = AttachmentBits(body.LeftEye),
            rightEye = AttachmentBits(body.RightEye),
            hatReference = AttachmentBits(body.Hat),
            attachment.StepIndex,
            attachment.Generation,
            shape = new
            {
                boundaryBits = shape.Boundary.Select(point => new[] { Bits(point.X), Bits(point.Y) }).ToArray(),
                boundsBits = new[] { Bits(shape.Left), Bits(shape.Right), Bits(shape.Bottom), Bits(shape.Top), Bits(shape.Width), Bits(shape.Height) },
                estimatedTilt = TiltBits(shape.EstimateTilt()),
            },
            tilt = TiltBits(attachment.ShapeTilt),
            face = new
            {
                left = EyeBits(attachment.Face.Left),
                right = EyeBits(attachment.Face.Right),
                attachment.Face.Safety,
                scalesBits = new[] { Bits(attachment.Face.GazeScale), Bits(attachment.Face.SpacingScale), Bits(attachment.Face.SizeScale) },
            },
            hat = attachment.Hat is null ? null : new
            {
                stateBits = new[] { Bits(attachment.Hat.S), Bits(attachment.Hat.Velocity), Bits(attachment.Hat.Minimum), Bits(attachment.Hat.Maximum) },
                attachment.Hat.Safety,
                poseBits = attachment.Hat.Pose is null ? null : new[] { Bits(attachment.Hat.Pose.Center.X), Bits(attachment.Hat.Pose.Center.Y),
                    Bits(attachment.Hat.Pose.Angle), Bits(attachment.Hat.Pose.S) },
                followBits = new[] { Bits(attachment.Hat.Follow.Lift), Bits(attachment.Hat.Follow.Bend),
                    Bits(attachment.Hat.Follow.Stretch), Bits(attachment.Hat.Follow.Slide) },
            },
        };
    }

    private static object CanonicalAccessory(AccessoryVisualFrame accessory) => new
    {
        item = CanonicalItem(accessory.Item),
        accessory.Mesh.StepIndex,
        accessory.Mesh.AllowFolding,
        verticesBits = accessory.Mesh.Vertices.Select(point => new[] { Bits(point.X), Bits(point.Y) }).ToArray(),
        textureCoordinatesBits = accessory.Mesh.TextureCoordinates.Select(point => new[] { Bits(point.X), Bits(point.Y) }).ToArray(),
        triangleIndices = accessory.Mesh.TriangleIndices.ToArray(),
        boundsBits = new[] { Bits(accessory.Mesh.Bounds.X), Bits(accessory.Mesh.Bounds.Y), Bits(accessory.Mesh.Bounds.Width), Bits(accessory.Mesh.Bounds.Height) },
        imagePivotBits = new[] { Bits(accessory.Mesh.ImagePivot.X), Bits(accessory.Mesh.ImagePivot.Y) },
        connectionBits = new[] { Bits(accessory.Mesh.ConnectionOffset.X), Bits(accessory.Mesh.ConnectionOffset.Y) },
        rotationBits = Bits(accessory.Mesh.RotationDegrees),
    };

    private static object CanonicalItem(AccessoryItem item) => new
    {
        item.Id,
        item.Name,
        item.AssetId,
        item.PixelWidth,
        item.PixelHeight,
        item.IsEquipped,
        item.Layer,
        item.Order,
        item.LockRotation,
        item.LimitSwing,
        item.DeformationMode,
        rig = item.Rig is null ? null : new
        {
            item.Rig.BindingVersion,
            nodes = item.Rig.Nodes.Select(node => new[] { Bits(node.X), Bits(node.Y) }).ToArray(),
            parameters = new[] { Bits(item.Rig.Softness), Bits(item.Rig.Damping), Bits(item.Rig.Restoration) },
        },
        parameterBits = new[] { Bits(item.SizePercent), Bits(item.BaseAngleDegrees), Bits(item.ImagePivotX), Bits(item.ImagePivotY),
            Bits(item.BodyAnchorX), Bits(item.BodyAnchorY), Bits(item.ConnectionStrength), Bits(item.MaxConnectionTravelPercent), Bits(item.RotationDamping),
            Bits(item.ReturnStrength), Bits(item.MaxSwingDegrees), Bits(item.GravityInfluence) },
    };
    private static object EyeBits(EyePresentationFrame eye) => new
    {
        eye.Shape,
        eye.Visible,
        eye.IsRight,
        geometryBits = new[] { Bits(eye.Center.X), Bits(eye.Center.Y), Bits(eye.Width), Bits(eye.Height),
            Bits(eye.Angle), Bits(eye.Openness), Bits(eye.Strength) },
    };
    private static object TiltBits(ShapeTilt tilt) => new { angleBits = Bits(tilt.Angle), confidenceBits = Bits(tilt.Confidence), tilt.Reason };
    private static long[] AttachmentBits(PresentationAttachment attachment) =>
        [Bits(attachment.Position.X), Bits(attachment.Position.Y), Bits(attachment.RotationRadians)];
    private static long[] RectBits(LogicalRect rectangle) => [Bits(rectangle.X), Bits(rectangle.Y), Bits(rectangle.Width), Bits(rectangle.Height)];
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static string Digest(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, CanonicalJson)));
    private static string FileSha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static AccessoryConfiguration Configuration() => new([
        new("fictional-front-hat", "Fictional front hat", "builtin-top-hat", 64, 64)
        {
            Layer = AccessoryLayer.Front, Order = 2, BodyAnchorX = .72, BodyAnchorY = .2,
            SizePercent = 84, BaseAngleDegrees = 13, ImagePivotX = .3, ImagePivotY = .81,
            ConnectionStrength = 63, RotationDamping = 71, LockRotation = false,
            ReturnStrength = 46, MaxSwingDegrees = 87, GravityInfluence = 29,
        },
        new("fictional-back-bowler", "Fictional back bowler", "builtin-bowler", 64, 64)
        {
            Layer = AccessoryLayer.Behind, Order = 1, BodyAnchorX = .29, BodyAnchorY = .56,
            SizePercent = 117, BaseAngleDegrees = -21, ImagePivotX = .8, ImagePivotY = .4,
            DeformationMode = AccessoryDeformationMode.SingleChain,
            Rig = new([new(.8, .4), new(.6, .6), new(.4, .85)], .68),
            ConnectionStrength = 25, GravityInfluence = 43,
        },
        new("fictional-front-leaf", "Fictional front leaf", "builtin-sprout", 64, 64)
        {
            Layer = AccessoryLayer.Front, Order = 1, BodyAnchorX = .228, BodyAnchorY = .756,
            DeformationMode = AccessoryDeformationMode.SingleChain,
            Rig = new([new(.5, .5), new(.7, .7), new(.8, .9)], .8446),
            ConnectionStrength = 0, RotationDamping = 0, LockRotation = false,
            ReturnStrength = 60, MaxSwingDegrees = 178, LimitSwing = true, GravityInfluence = 20,
        },
    ]);

    private sealed record FrameDigest(int Step, string Phase, string Sha256);
    private sealed record TrajectoryEvidence(double RightDisplacement, double LeftDisplacement,
        int AirborneFrames, int RisingFrames, int FallingFrames, bool Landed, int NonIdentityGaitFrames);
    private sealed record Baseline(int Schema, string ReferenceDesktopSha256, string NativeSha256,
        string FixtureSha256, int WriterProcessId, FrameDigest[] Frames, TrajectoryEvidence Evidence);
}
