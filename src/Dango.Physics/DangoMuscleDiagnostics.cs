namespace Dango.Physics;

/// <summary>Read-only telemetry from the last accepted native muscle step.</summary>
public sealed record DangoMuscleDiagnostics(double Time, double Strength, int ActiveSubsteps,
    int PairCount, double NetForceX, double NetForceY, double MaximumAbsTorque, double Work,
    double MaximumTension, double MinimumContactWeight, double Envelope);
