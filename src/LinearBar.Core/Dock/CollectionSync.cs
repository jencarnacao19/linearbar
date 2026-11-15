using System.Collections.ObjectModel;

namespace LinearBar.Core.Dock;

/// <summary>
/// Brings an <see cref="ObservableCollection{T}"/> in line with a desired, keyed order using
/// the fewest structural changes, so bound views keep the visuals of items that survive.
/// </summary>
public static class CollectionSync
{
    /// <param name="target">Collection to update in place.</param>
    /// <param name="desiredKeys">Keys in the order the collection should end up in. Must be unique.</param>
    /// <param name="keyOf">Returns the key of an existing item.</param>
    /// <param name="create">Creates the item for a key that is not in the collection yet.</param>
    /// <param name="removed">Called for every item that was dropped, e.g. to dispose it.</param>
    public static void Apply<T>(
        ObservableCollection<T> target,
        IReadOnlyList<string> desiredKeys,
        Func<T, string> keyOf,
        Func<string, T> create,
        Action<T>? removed = null)
    {
        var desired = new HashSet<string>(desiredKeys, StringComparer.Ordinal);

        for (int i = target.Count - 1; i >= 0; i--)
        {
            T item = target[i];
            if (!desired.Contains(keyOf(item)))
            {
                target.RemoveAt(i);
                removed?.Invoke(item);
            }
        }

        for (int i = 0; i < desiredKeys.Count; i++)
        {
            string key = desiredKeys[i];

            if (i < target.Count && keyOf(target[i]) == key)
            {
                continue;
            }

            int existing = -1;
            for (int j = i + 1; j < target.Count; j++)
            {
                if (keyOf(target[j]) == key)
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, create(key));
            }
        }
    }
}
