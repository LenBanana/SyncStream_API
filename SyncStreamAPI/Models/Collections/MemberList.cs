using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SyncStreamAPI.Models.Collections;

/// <summary>
///     An ordered list of room members that is safe to read while it is being changed.
/// </summary>
/// <remarks>
///     This was a plain <see cref="List{T}" /> mutated from concurrent hub invocations and from background
///     timers, while simultaneously being iterated to build broadcast payloads. That is the textbook way to
///     get "collection was modified during enumeration" under load, and to lose a join that raced a leave.
///     <para>
///         Order matters and cannot be traded away for a dictionary: blackjack advances turns by index, so
///         the sequence of members <em>is</em> the turn order.
///     </para>
///     <para>
///         Copy-on-write suits the access pattern exactly. Rooms hold a handful of members, joins and leaves
///         are rare, and reads are constant. Readers take the current immutable snapshot and are never
///         affected by a later write; writers swap the snapshot under a lock. No reader ever needs one.
///     </para>
/// </remarks>
public class MemberList : IEnumerable<Member>
{
    private readonly object _writeLock = new();
    private ImmutableList<Member> _members = ImmutableList<Member>.Empty;

    public MemberList()
    {
    }

    public MemberList(IEnumerable<Member> members)
    {
        _members = ImmutableList.CreateRange(members);
    }

    /// <summary>The current snapshot. Safe to hold and enumerate: it never changes.</summary>
    public ImmutableList<Member> Snapshot => _members;

    public int Count => _members.Count;

    /// <summary>
    ///     Member at a position, or null when the position no longer exists.
    /// </summary>
    /// <remarks>
    ///     Returns null rather than throwing because callers index positions they found earlier, and a member
    ///     can leave in between. The previous list threw <see cref="ArgumentOutOfRangeException" /> there.
    /// </remarks>
    public Member? this[int index]
    {
        get
        {
            var snapshot = _members;
            return index >= 0 && index < snapshot.Count ? snapshot[index] : null;
        }
    }

    public void Add(Member member)
    {
        lock (_writeLock) _members = _members.Add(member);
    }

    public bool Remove(Member member)
    {
        lock (_writeLock)
        {
            var updated = _members.Remove(member);
            if (updated == _members) return false;
            _members = updated;
            return true;
        }
    }

    /// <summary>Removes the member at a position. A no-op when the position is gone.</summary>
    public void RemoveAt(int index)
    {
        lock (_writeLock)
        {
            if (index < 0 || index >= _members.Count) return;
            _members = _members.RemoveAt(index);
        }
    }

    public void Insert(int index, Member member)
    {
        lock (_writeLock)
        {
            var clamped = Math.Clamp(index, 0, _members.Count);
            _members = _members.Insert(clamped, member);
        }
    }

    public void Clear()
    {
        lock (_writeLock) _members = ImmutableList<Member>.Empty;
    }

    public int FindIndex(Predicate<Member> match)
    {
        return _members.FindIndex(match);
    }

    public Member? Find(Predicate<Member> match)
    {
        return _members.Find(match);
    }

    public bool Contains(Member member)
    {
        return _members.Contains(member);
    }

    public void ForEach(Action<Member> action)
    {
        foreach (var member in _members) action(member);
    }

    public List<TResult> ConvertAll<TResult>(Func<Member, TResult> selector)
    {
        return _members.Select(selector).ToList();
    }

    public IEnumerator<Member> GetEnumerator()
    {
        return _members.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
