using System.Collections.Concurrent;
using UmbraSync.API.Data;
using UmbraSync.Utils;

namespace UmbraSync.PlayerData.Pairs;

public sealed class PairStateCache
{
    private readonly ConcurrentDictionary<string, PairState> _cache = new(StringComparer.Ordinal);
    public void Store(string ident, CharacterData data)
    {
        if (string.IsNullOrEmpty(ident) || data is null)
            return;

        var state = _cache.GetOrAdd(ident, _ => new PairState());
        state.CharacterData = data.DeepClone();
    }
    
    public CharacterData? TryLoad(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return null;

        if (_cache.TryGetValue(ident, out var state) && state.CharacterData is not null)
            return state.CharacterData.DeepClone();

        return null;
    }

    public Guid? TryGetTemporaryCollection(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return null;

        if (_cache.TryGetValue(ident, out var state))
            return state.TemporaryCollectionId;

        return null;
    }


    public Guid? StoreTemporaryCollection(string ident, Guid collection)
    {
        if (string.IsNullOrEmpty(ident) || collection == Guid.Empty)
            return null;

        var state = _cache.GetOrAdd(ident, _ => new PairState());
        state.TemporaryCollectionId = collection;
        return collection;
    }
    
    public Guid? ClearTemporaryCollection(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return null;

        if (_cache.TryGetValue(ident, out var state))
        {
            var existing = state.TemporaryCollectionId;
            state.TemporaryCollectionId = null;
            TryRemoveIfEmpty(ident, state);
            return existing;
        }

        return null;
    }
    
    public IReadOnlyList<Guid> ClearAllTemporaryCollections()
    {
        var removed = new List<Guid>();
        foreach (var (ident, state) in _cache)
        {
            if (state.TemporaryCollectionId is { } guid && guid != Guid.Empty)
            {
                removed.Add(guid);
                state.TemporaryCollectionId = null;
            }
            TryRemoveIfEmpty(ident, state);
        }
        return removed;
    }
    
    public void Clear(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return;

        _cache.TryRemove(ident, out _);
    }
    
    public void ClearAll()
    {
        _cache.Clear();
    }
    
    public bool HasData(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return false;

        return _cache.TryGetValue(ident, out var state) && !state.IsEmpty;
    }

    private void TryRemoveIfEmpty(string ident, PairState state)
    {
        if (state.IsEmpty)
            _cache.TryRemove(ident, out _);
    }
}

public sealed class PairState
{
    public CharacterData? CharacterData { get; set; }
    public Guid? TemporaryCollectionId { get; set; }

    public bool IsEmpty => CharacterData is null && TemporaryCollectionId is null;
}
