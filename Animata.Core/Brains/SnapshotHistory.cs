using Animata.Core.Brains.Modules;

namespace Animata.Core.Brains;

/// <summary>
/// Przeglądanie snapshotów mózgów wstecz (jak kolejne „cofnij”) i zapisywanie bez duplikatów.
/// Kursor jest osobny dla każdego mózgu; nowy snapshot ustawia go z powrotem na najnowszy.
/// Cofanie pomija snapshoty, których przywrócenie niczego by nie zmieniło — każde cofnięcie coś zmienia.
/// </summary>
public sealed class SnapshotHistory
{
    private readonly Dictionary<Brain, int> _cursor = [];

    /// <summary>Snapshot podanych modułów (domyślnie wszystkich) albo null, gdy najnowszy ma już ten stan.</summary>
    public BrainSnapshot? Capture(Brain aBrain, string aLabel, params BrainModule[] aModules)
    {
        var snapshot = aBrain.CaptureIfChanged(aLabel, aModules.Length > 0 ? aModules : null);
        if (snapshot is not null)
            _cursor.Remove(aBrain);
        return snapshot;
    }

    /// <summary>
    /// Przywraca poprzedni (względem kursora) snapshot, który coś zmieni; po najstarszym wraca do najnowszego.
    /// Zwraca przywrócony snapshot i jego indeks albo null, jeśli żaden snapshot nie różni się od stanu mózgu.
    /// </summary>
    public (BrainSnapshot Snapshot, int Index)? StepBack(Brain aBrain)
    {
        var count = aBrain.Snapshots.Count;
        var cursor = Math.Min(_cursor.GetValueOrDefault(aBrain, count), count);
        for (var step = 0; step < count; step++)
        {
            cursor = cursor <= 0 ? count - 1 : cursor - 1;
            var snapshot = aBrain.Snapshots[cursor];
            if (aBrain.Matches(snapshot))
                continue;
            aBrain.Restore(snapshot);
            _cursor[aBrain] = cursor;
            return (snapshot, cursor);
        }
        return null;
    }

    /// <summary>Zapomina kursor (np. usuniętego stwora).</summary>
    public void Forget(Brain aBrain) => _cursor.Remove(aBrain);
}
