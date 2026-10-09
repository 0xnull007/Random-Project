namespace Quill.Core.Editing;

/// <summary>
/// Snapshot-based undo. Consecutive typing (or deleting) records coalesce into one step unless the caller
/// marks a new group (caret moved, new word) or more than <see cref="CoalesceWindow"/> elapsed.
/// </summary>
public sealed class UndoStack
{
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);

    private readonly List<EditRecord> _undo = [];
    private readonly List<EditRecord> _redo = [];
    private readonly int _capacity;
    private readonly TimeProvider _time;
    private bool _breakNext;

    public UndoStack(int capacity = 1000, TimeProvider? time = null)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _time = time ?? TimeProvider.System;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public long Now() => _time.GetTimestamp();

    /// <summary>Prevents the next pushed record from coalescing with the previous one.</summary>
    public void BreakCoalescing() => _breakNext = true;

    public void Push(EditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _redo.Clear();

        if (!_breakNext && _undo.Count > 0 && CanCoalesce(_undo[^1], record))
        {
            EditRecord previous = _undo[^1];
            _undo[^1] = previous with
            {
                After = record.After,
                SelectionAfter = record.SelectionAfter,
                Change = previous.Change.Union(record.Change),
                Timestamp = record.Timestamp,
            };
        }
        else
        {
            _undo.Add(record);
            if (_undo.Count > _capacity)
            {
                _undo.RemoveAt(0);
            }
        }

        _breakNext = false;
    }

    public EditRecord? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        EditRecord record = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(record);
        _breakNext = true;
        return record;
    }

    public EditRecord? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        EditRecord record = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(record);
        _breakNext = true;
        return record;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _breakNext = false;
    }

    private bool CanCoalesce(EditRecord previous, EditRecord next)
    {
        if (!previous.IsCoalescable || previous.Kind != next.Kind || next.StartsNewGroup)
        {
            return false;
        }

        if (!ReferenceEquals(previous.After, next.Before))
        {
            return false;
        }

        if (previous.Change.StructureChanged || next.Change.StructureChanged || previous.Change.Story != next.Change.Story)
        {
            return false;
        }

        return _time.GetElapsedTime(previous.Timestamp, next.Timestamp) <= CoalesceWindow;
    }
}
