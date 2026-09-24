using Emu8086.Core.Cpu;

namespace Emu8086.Core.Machine;

/// <summary>What one executed instruction changed; enough to undo it and to visualise it.</summary>
public sealed class StepRecord
{
    public required CpuState Before { get; init; }
    public CpuState After { get; set; }
    public List<(int Address, byte OldValue, byte NewValue)> MemoryWrites { get; } = new();
    /// <summary>Every byte read, including the instruction fetch (in order, may repeat).</summary>
    public List<(int Address, byte Value)> MemoryReads { get; } = new();
    public List<(int Port, int Value, bool IsWrite, bool Word)> PortAccesses { get; } = new();
}

/// <summary>Bounded undo log used by "step back".</summary>
public sealed class ExecutionHistory : IMemoryJournal
{
    public const int DefaultCapacity = 20000;

    private readonly LinkedList<StepRecord> _records = new();
    private readonly Memory _memory;
    private StepRecord? _current;

    public ExecutionHistory(Memory memory)
    {
        _memory = memory;
    }

    public int Capacity { get; set; } = DefaultCapacity;
    public int Count => _records.Count;
    public StepRecord? Last => _records.Last?.Value;

    public void Begin(CpuState before) => _current = new StepRecord { Before = before };

    public void Record(int address, byte oldValue)
    {
        // The new value is filled in by Commit (write happens right after this call).
        _current?.MemoryWrites.Add((address, oldValue, 0));
    }

    public void RecordRead(int address, byte value) => _current?.MemoryReads.Add((address, value));

    public void RecordPort(int port, int value, bool isWrite, bool word) =>
        _current?.PortAccesses.Add((port, value, isWrite, word));

    public void Cancel() => _current = null;

    public StepRecord? Commit(CpuState after)
    {
        if (_current == null) return null;
        var record = _current;
        _current = null;
        record.After = after;
        for (int i = 0; i < record.MemoryWrites.Count; i++)
        {
            var w = record.MemoryWrites[i];
            record.MemoryWrites[i] = (w.Address, w.OldValue, _memory.Peek(w.Address));
        }
        _records.AddLast(record);
        while (_records.Count > Capacity) _records.RemoveFirst();
        return record;
    }

    /// <summary>Undoes the most recent instruction. Returns false when history is empty.</summary>
    public bool Undo(Cpu8086 cpu)
    {
        var node = _records.Last;
        if (node == null) return false;
        _records.RemoveLast();
        var record = node.Value;
        for (int i = record.MemoryWrites.Count - 1; i >= 0; i--)
            _memory.Restore(record.MemoryWrites[i].Address, record.MemoryWrites[i].OldValue);
        cpu.SetState(record.Before);
        return true;
    }

    public void Clear()
    {
        _records.Clear();
        _current = null;
    }
}
