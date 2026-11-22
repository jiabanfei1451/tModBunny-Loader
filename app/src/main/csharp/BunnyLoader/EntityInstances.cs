using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using BunnyLoader.Interop;

namespace BunnyLoader;

internal sealed unsafe class EntityInstances<T> where T : class
{
    const int SweepEvery = 512;

    readonly Dictionary<IntPtr, Entry> entries = new();
    int added;
    long version;

    struct Entry
    {
        public uint Handle;
        public long Validated;
        public T Value;
    }

    struct Hit
    {
        public EntityInstances<T>? Owner;
        public IntPtr Entity;
        public long Epoch, Version;
        public T? Value;
    }

    [ThreadStatic] static Hit last;

    public int Count
    {
        get { lock (entries) return entries.Count; }
    }

    public T? Get(IntPtr entity)
    {
        if (entity == IntPtr.Zero) return null;
        long collections = GameGc.Collections();
        if (collections >= 0)
        {
            ref Hit hit = ref last;
            if (hit.Entity == entity && hit.Epoch == collections && ReferenceEquals(hit.Owner, this) && hit.Version == Volatile.Read(ref version)) return hit.Value;
        }
        lock (entries)
        {
            ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(entries, entity);
            if (Unsafe.IsNullRef(ref entry)) return null;
            if (collections >= 0 && entry.Validated == collections) return Remember(entity, collections, entry.Value);
            NativeApi* api = Host.Native();
            if (api->GcHandleTarget(entry.Handle) == entity)
            {
                entry.Validated = collections;
                return collections >= 0 ? Remember(entity, collections, entry.Value) : entry.Value;
            }
            Remove(api, entity, entry);
            return null;
        }
    }

    T Remember(IntPtr entity, long collections, T value)
    {
        last = new Hit { Owner = this, Entity = entity, Epoch = collections, Version = version, Value = value };
        return value;
    }

    public void Set(IntPtr entity, T? value)
    {
        if (entity == IntPtr.Zero) return;
        NativeApi* api = Host.Native();
        lock (entries)
        {
            if (entries.TryGetValue(entity, out Entry old))
            {
                if (value != null && api->GcHandleTarget(old.Handle) == entity)
                {
                    entries[entity] = new Entry { Handle = old.Handle, Validated = collections, Value = value };
                    Interlocked.Increment(ref version);
                    return;
                }
                Remove(api, entity, old);
            }
            if (value == null) return;
            uint handle = api->GcHandleNewWeak(entity);
            if (handle == 0) return;
            entries[entity] = new Entry { Handle = handle, Validated = collections, Value = value };
            Interlocked.Increment(ref version);
            if (++added % SweepEvery == 0) Sweep(api);
        }
    }

    void Remove(NativeApi* api, IntPtr entity, Entry entry)
    {
        entries.Remove(entity);
        Interlocked.Increment(ref version);
        api->GcHandleFree(entry.Handle);
    }

    void Sweep(NativeApi* api)
    {
        List<IntPtr>? dead = null;
        foreach ((IntPtr entity, Entry entry) in entries)
        {
            if (api->GcHandleTarget(entry.Handle) != entity) (dead ??= new List<IntPtr>()).Add(entity);
        }
        if (dead == null) return;
        foreach (IntPtr entity in dead) Remove(api, entity, entries[entity]);
    }
}
