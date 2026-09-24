namespace Emu8086.Core.Editing;

public enum AutoSaveMode { Off, Interval, Smart }

/// <summary>Why a document is being considered for saving.</summary>
public enum AutoSaveTrigger
{
    /// <summary>Periodic check (timer tick).</summary>
    Tick,
    /// <summary>The application lost focus or the user switched to another document.</summary>
    FocusLost,
}

/// <summary>Unsaved-change statistics of one document since its last save.</summary>
public sealed class EditStats
{
    /// <summary>Characters inserted plus removed.</summary>
    public int ChangedCharacters { get; private set; }
    /// <summary>Line breaks inserted or removed (the shape of the code changed).</summary>
    public int StructuralEdits { get; private set; }
    public DateTime? FirstChange { get; private set; }
    public DateTime? LastChange { get; private set; }

    public bool HasChanges => FirstChange != null;

    public void Record(int inserted, int removed, int lineBreaks, DateTime now)
    {
        ChangedCharacters += inserted + removed;
        StructuralEdits += lineBreaks;
        FirstChange ??= now;
        LastChange = now;
    }

    public void Reset()
    {
        ChangedCharacters = 0;
        StructuralEdits = 0;
        FirstChange = null;
        LastChange = null;
    }
}

/// <summary>
/// Decides when to auto-save.
/// Interval: every <c>interval</c> once there are changes.
/// Smart: never while the user is typing; after a short pause it saves meaningful changes
/// (enough characters or a changed line structure), small changes once they are older than
/// the interval, and everything when focus leaves the document.
/// </summary>
public sealed class AutoSavePolicy
{
    public static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(2);
    public const int CharacterThreshold = 40;

    public AutoSaveMode Mode { get; init; }
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    public bool ShouldSave(EditStats stats, DateTime now, AutoSaveTrigger trigger)
    {
        if (Mode == AutoSaveMode.Off || !stats.HasChanges) return false;

        if (Mode == AutoSaveMode.Interval)
            return now - stats.FirstChange!.Value >= Interval;

        if (trigger == AutoSaveTrigger.FocusLost) return true;
        bool idle = now - stats.LastChange!.Value >= IdleTime;
        if (!idle) return false;
        return stats.ChangedCharacters >= CharacterThreshold
               || stats.StructuralEdits > 0
               || now - stats.FirstChange!.Value >= Interval;
    }
}
