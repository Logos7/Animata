namespace Animata.Tests;

/// <summary>
/// Znane porażki, trzymane osobno, żeby CI mówiło o nowych. Progi się nie zmieniają — testy dalej sprawdzają to samo.
/// <see cref="BepuBeta29"/>: ruch stworów strojono na masterze Bepu, a paczka 2.5.0-beta.29 nie ma poprawek tarcia
/// (3bd72d2) i rozszerzenia kątowego pudeł (c230dd1). Te testy przejdą, gdy wyjdzie nowsza paczka (wtedy usunąć znacznik).
/// CI: główny krok pomija te testy (<c>--filter "Znane!=Bepu-beta29"</c>), osobny krok puszcza tylko je, bez blokowania.
/// </summary>
public static class KnownFailures
{
    public const string Trait = "Znane";
    public const string BepuBeta29 = "Bepu-beta29";
}
