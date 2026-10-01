namespace Animata.Core.Bodies;

/// <summary>
/// Nazwy portów stawów — wspólne dla napędu stawów (<see cref="Actuators.SpineActuator"/>), czucia stawów
/// (<see cref="Sensors.JointSensor"/>) i sterowników (CPG, chody, sieci): <c>Yaw{i}</c> — skręt stawu i,
/// <c>Pitch{i}</c> — pochylenie, oba jako ułamek zakresu stawu [-1, 1].
/// </summary>
public static class JointPorts
{
    public const string YawPrefix = "Yaw";
    public const string PitchPrefix = "Pitch";

    public static string Yaw(int aJoint) => YawPrefix + aJoint.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Pitch(int aJoint) => PitchPrefix + aJoint.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Porty <paramref name="aJoints"/> stawów z obiema osiami: najpierw wszystkie Yaw, potem wszystkie Pitch.</summary>
    public static string[] For(int aJoints) =>
        [.. Enumerable.Range(0, aJoints).Select(Yaw), .. Enumerable.Range(0, aJoints).Select(Pitch)];

    /// <summary>Porty na przemian (Yaw0, Pitch0, Yaw1, …) — wyjścia sieci, do których nowy staw dopisuje się na końcu.</summary>
    public static IEnumerable<string> Interleaved(int aJoints) =>
        Enumerable.Range(0, aJoints).SelectMany(aJoint => new[] { Yaw(aJoint), Pitch(aJoint) });

    /// <summary>
    /// Porty osi z serwem (napęd stawów): Yaw{i} stawów z ruchomym skrętem, potem Pitch{i} stawów z ruchomym pochyleniem
    /// (zawias nie ma portu skrętu, staw bierny — żadnego). Dla planu z samymi stawami kulowymi — jak <see cref="For"/>.
    /// </summary>
    public static string[] Driven(BodyPlan aPlan) => Select(aPlan, aJoint => aJoint.DrivesYaw, aJoint => aJoint.DrivesPitch);

    /// <summary>Porty czucia stawów: ruchome osie wszystkich zginanych stawów (z serwem i biernych), Yaw, potem Pitch.</summary>
    public static string[] Sensed(BodyPlan aPlan) => Select(aPlan, aJoint => aJoint.HasYaw, aJoint => aJoint.HasPitch);

    private static string[] Select(BodyPlan aPlan, Func<JointPlan, bool> aYaw, Func<JointPlan, bool> aPitch) =>
    [
        .. Enumerable.Range(0, aPlan.Joints.Count).Where(aJoint => aYaw(aPlan.Joints[aJoint])).Select(Yaw),
        .. Enumerable.Range(0, aPlan.Joints.Count).Where(aJoint => aPitch(aPlan.Joints[aJoint])).Select(Pitch)
    ];

    /// <summary>Czy port to port stawu (Yaw{i} albo Pitch{i}); <paramref name="aJoint"/> — numer stawu, <paramref name="aYaw"/> — która oś.</summary>
    public static bool TryParse(string aPort, out int aJoint, out bool aYaw)
    {
        aYaw = aPort.StartsWith(YawPrefix, StringComparison.Ordinal);
        var prefix = aYaw ? YawPrefix : aPort.StartsWith(PitchPrefix, StringComparison.Ordinal) ? PitchPrefix : null;
        aJoint = -1;
        return prefix is not null && aPort.Length > prefix.Length && aPort.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0 &&
               int.TryParse(aPort.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out aJoint);
    }

    /// <summary>Czy port to port stawu (Yaw{i} albo Pitch{i}).</summary>
    public static bool IsJointPort(string aPort) => TryParse(aPort, out _, out _);
}
