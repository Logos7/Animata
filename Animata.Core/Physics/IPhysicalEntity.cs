namespace Animata.Core.Physics;

/// <summary>
/// Encja, która ma postać w <see cref="PhysicsWorld"/> (Bepu). Świat podpina ją, gdy fizyka istnieje,
/// a fizykę tworzy przy pierwszej encji dynamicznej. Kolejność w ticku: Think → Act → <see cref="BeforePhysicsStep"/>
/// → krok fizyki → <see cref="AfterPhysicsStep"/>.
/// </summary>
public interface IPhysicalEntity
{
    /// <summary>Czy encja porusza się w fizyce (stwór z części). Statyczne (klocek, cylinder) same fizyki nie tworzą.</summary>
    bool IsDynamic { get; }

    void AttachPhysics(PhysicsWorld aPhysics);

    void DetachPhysics(PhysicsWorld aPhysics);

    /// <summary>Przed krokiem: przeniesienie ręcznych zmian (przesunięcie, rozmiar), cele silników, siły własne.</summary>
    void BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta);

    /// <summary>Po kroku: odczyt pozy z fizyki do <see cref="Entities.Entity.Body"/>.</summary>
    void AfterPhysicsStep(PhysicsWorld aPhysics);
}
