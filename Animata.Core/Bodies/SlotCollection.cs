using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Animata.Core.Bodies;

public sealed class SlotCollection<T> : Collection<T> where T : class
{
    public long Revision { get; private set; }

    [SuppressMessage("Naming", "CA1725")]
    protected override void InsertItem(int aIndex, T aItem)
    {
        ArgumentNullException.ThrowIfNull(aItem);
        base.InsertItem(aIndex, aItem);
        Revision++;
    }

    [SuppressMessage("Naming", "CA1725")]
    protected override void SetItem(int aIndex, T aItem)
    {
        ArgumentNullException.ThrowIfNull(aItem);
        base.SetItem(aIndex, aItem);
        Revision++;
    }

    [SuppressMessage("Naming", "CA1725")]
    protected override void RemoveItem(int aIndex)
    {
        base.RemoveItem(aIndex);
        Revision++;
    }

    [SuppressMessage("Naming", "CA1725")]
    protected override void ClearItems()
    {
        base.ClearItems();
        Revision++;
    }
}
