namespace Animata.Core.Brains;

public sealed record BrainConnection(Guid SourceId, string SourcePort, Guid TargetId, string TargetPort);
