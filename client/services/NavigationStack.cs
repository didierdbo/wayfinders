using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# stack of <see cref="IScreen"/> identifiers + their contexts.
/// Lives separately from <see cref="SceneManager"/> so it can be unit-tested
/// without a Godot runtime. The Godot autoload is a thin orchestration
/// shell over this — see pre-brief §4.2.
///
/// <para>
/// <b>What lives here:</b> ordered list of screens, push/pop/replace,
/// peek, and the "is X currently in the stack" query. No async, no signal
/// emission, no scene tree mutation — those happen in
/// <see cref="SceneManager"/>.
/// </para>
///
/// <para>
/// We track <see cref="ScreenStackEntry"/> records (id + context) rather
/// than raw <see cref="IScreen"/> instances so this stays unit-testable.
/// The actual <see cref="IScreen"/> node lookup belongs to the manager.
/// </para>
/// </summary>
public sealed class NavigationStack
{
    private readonly List<ScreenStackEntry> _entries = new();

    /// <summary>Number of screens currently in the stack.</summary>
    public int Count => _entries.Count;

    /// <summary>Top of stack (the active screen), or null if empty.</summary>
    public ScreenStackEntry? Current => _entries.Count > 0 ? _entries[^1] : null;

    /// <summary>Snapshot copy of the stack from bottom to top, useful for tests and inspection.</summary>
    public IReadOnlyList<ScreenStackEntry> Snapshot() => _entries.ToList();

    /// <summary>True if a screen with that id is anywhere in the stack.</summary>
    public bool Contains(string screenId) => _entries.Any(e => e.ScreenId == screenId);

    /// <summary>Push a new screen onto the top of the stack.</summary>
    public void Push(ScreenStackEntry entry) => _entries.Add(entry);

    /// <summary>
    /// Pop the top entry. Returns the popped entry, or null if the stack
    /// was empty. Caller decides whether empty-pop is an error
    /// (SceneManager treats it as a no-op so Esc-on-root is safe).
    /// </summary>
    public ScreenStackEntry? Pop()
    {
        if (_entries.Count == 0)
            return null;
        var top = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        return top;
    }

    /// <summary>
    /// Pop all entries above (and including) the named screen, then push
    /// the new entry. If the named screen is not in the stack, behaves
    /// like a Push. Used for the "go back to E1 then jump to E3" case.
    /// </summary>
    public IReadOnlyList<ScreenStackEntry> PopUntilThenPush(string popUntilScreenId, ScreenStackEntry newEntry)
    {
        var popped = new List<ScreenStackEntry>();
        var index = _entries.FindLastIndex(e => e.ScreenId == popUntilScreenId);
        if (index >= 0)
        {
            for (int i = _entries.Count - 1; i > index; i--)
            {
                popped.Add(_entries[i]);
                _entries.RemoveAt(i);
            }
            // Replace the target itself
            popped.Add(_entries[index]);
            _entries.RemoveAt(index);
        }
        _entries.Add(newEntry);
        return popped;
    }

    /// <summary>Drop everything. Used on hard reset (e.g. for tests).</summary>
    public void Clear() => _entries.Clear();
}

/// <summary>
/// Pair of screen id and its entering context. Records held by
/// <see cref="NavigationStack"/>.
/// </summary>
public sealed record ScreenStackEntry(string ScreenId, ScreenContext Context);
