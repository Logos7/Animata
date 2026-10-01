using System.Numerics;

namespace Animata.Core.Training;

public sealed record SeekTargetOptions
{
    public int EpisodesPerGeneration { get; init; } = 8;
    public float EpisodeSeconds { get; init; } = 8;
    public float Delta { get; init; } = 1f / 30f;
    public float MinDistance { get; init; } = 1.5f;
    public float MaxDistance { get; init; } = 6;
    public int Seed { get; init; } = 1;

    /// <summary>Koszt energii ruchu: waga · średni wysiłek (patrz <see cref="SeekRig.Effort"/>). Bez niego sieć „taranuje” cel.</summary>
    public float EnergyWeight { get; init; } = 0.1f;

    /// <summary>Liczba cylindrów na drodze do celu w próbie (losowana z zakresu).</summary>
    public int MinObstacles { get; init; }
    public int MaxObstacles { get; init; }
    public float MinObstacleRadius { get; init; } = 0.3f;
    public float MaxObstacleRadius { get; init; } = 0.7f;

    /// <summary>
    /// Kara za kontakt z przeszkodą: waga · ułamek czasu próby w kontakcie. Bez niej sieć uczy się
    /// „przepychać” po cylindrach, bo kolizje i tak ją przesuwają.
    /// </summary>
    public float ContactWeight { get; init; }

    /// <summary>Najmniejsza odległość środka startu od brzegu cylindra — żeby dało się ruszyć i skręcić.</summary>
    public float ObstacleClearanceFromStart { get; init; } = 2;
    public float ObstacleClearanceFromTarget { get; init; } = 1.5f;

    /// <summary>
    /// Stały zestaw prób do wyboru mistrza — niezależny od <see cref="Seed"/>, żeby wyniki mistrzów
    /// z różnych sesji nauki (i etykiety snapshotów) dało się porównywać.
    /// </summary>
    public int ValidationSeed { get; init; } = 1;
    public int ValidationEpisodes { get; init; } = 16;

    /// <summary>Szczelina (powierzchnia–powierzchnia) na końcu próby, przy której cel uznaje się za osiągnięty.</summary>
    public float ReachGap { get; init; } = 0.3f;

    /// <summary>Liczba płaskich klocków w próbie (losowana z zakresu) — stwór uczy się chodzić po nierównym.</summary>
    public int MinSlabs { get; init; }
    public int MaxSlabs { get; init; }
    public float MinSlabSide { get; init; } = 0.8f;
    public float MaxSlabSide { get; init; } = 2.2f;
    public float MinSlabHeight { get; init; } = 0.03f;
    public float MaxSlabHeight { get; init; } = 0.1f;

    /// <summary>
    /// Kara za złą postawę: waga · całka z <see cref="SeekRig.Posture"/> / T (np. pająk leżący brzuchem na ziemi albo
    /// przewrócony). Bez niej ewolucja chętnie „pełza” tułowiem po ziemi, bo i tak dojeżdża do celu.
    /// </summary>
    public float PostureWeight { get; init; }

    /// <summary>Długość ciała za głową w chwili startu (wąż leży wzdłuż −X) — klocki nie mogą leżeć pod nim.</summary>
    public float BodyLength { get; init; }

    /// <summary>
    /// Waga członu dojścia do celu (1 — zwykle; 0 — próba nie jest o dojściu, np. stanie w miejscu, gdzie liczy się tylko
    /// postawa i wysiłek).
    /// </summary>
    public float DistanceWeight { get; init; } = 1;

    /// <summary>
    /// Kara za odejście z miejsca startu: waga · całka z poziomej odległości od startu (m) / T. Dla stania — bez niej
    /// regulator „stoi”, kołysząc się i drobiąc w tył (kilka metrów w 40 s).
    /// </summary>
    public float DriftWeight { get; init; }

    /// <summary>
    /// Pchnięcie w próbie: w chwili <see cref="PushAtSeconds"/> stwór dostaje prędkość o losowym kierunku w poziomie
    /// i wielkości z zakresu (m/s). <see cref="MaxPushSpeed"/> 0 — bez pchnięć.
    /// </summary>
    public float PushAtSeconds { get; init; } = 1.5f;
    public float MinPushSpeed { get; init; }
    public float MaxPushSpeed { get; init; }
}

public readonly record struct ObstacleSpec(Vector2 Position, float Radius);

/// <summary>Klocek terenu: środek spodu, wymiary (X, Y), wysokość, obrót wokół pionu.</summary>
public readonly record struct SlabSpec(Vector2 Position, Vector2 Size, float Height, float Yaw);

/// <summary>
/// Jedna próba: stwór w (0,0) obrócony o Yaw, cel w TargetOffset (na terenie), cylindry i klocki na drodze, opcjonalne
/// pchnięcie (prędkość w poziomie, m/s — patrz <see cref="SeekTargetOptions.MaxPushSpeed"/>).
/// </summary>
public readonly record struct SeekEpisode(float Yaw, Vector2 TargetOffset, ObstacleSpec[] Obstacles, SlabSpec[]? Slabs = null, Vector2? Push = null);

/// <summary>
/// Wynik jednej próby: Cost — składnik fitness (mniejszy = lepszy), FinalGap — szczelina do celu na końcu,
/// ContactTime — sekundy w kontakcie z przeszkodą, Reached — FinalGap ≤ <see cref="SeekTargetOptions.ReachGap"/>.
/// </summary>
public readonly record struct EpisodeResult(float Cost, float FinalGap, float ContactTime, bool Reached, float PostureTime = 0);
