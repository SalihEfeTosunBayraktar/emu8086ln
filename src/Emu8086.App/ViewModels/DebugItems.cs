using System.Globalization;
using Emu8086.App.Infrastructure;
using Emu8086.Core.Assembler;

namespace Emu8086.App.ViewModels;

/// <summary>A CPU register shown (and editable) in the registers panel.</summary>
public sealed class RegisterItem : ObservableObject
{
    private readonly Action<ushort> _write;
    private ushort _value;
    private bool _changed;

    public RegisterItem(string name, bool splittable, Action<ushort> write)
    {
        Name = name;
        Splittable = splittable;
        _write = write;
    }

    public string Name { get; }
    /// <summary>AX..DX are shown as separate high and low bytes.</summary>
    public bool Splittable { get; }

    public ushort Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value)) return;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(High));
            OnPropertyChanged(nameof(Low));
            OnPropertyChanged(nameof(Details));
        }
    }

    public bool Changed
    {
        get => _changed;
        set => Set(ref _changed, value);
    }

    /// <summary>The value in every base, shown as the register's tooltip.</summary>
    public string Details
    {
        get
        {
            var loc = Emu8086.App.Services.Loc.Instance;
            string bits = Convert.ToString(_value, 2).PadLeft(16, '0');
            return $"{Name}\n{loc["tools.hex"]}: {_value:X4}h\n{loc["tools.unsigned"]}: {_value}\n"
                   + $"{loc["tools.signed"]}: {(short)_value}\n{loc["tools.binary"]}: {bits[..8]} {bits[8..]}b";
        }
    }

    public string Text
    {
        get => _value.ToString("X4");
        set => Write(value, 0xFFFF, 0);
    }

    public string High
    {
        get => (_value >> 8).ToString("X2");
        set => Write(value, 0xFF, 8);
    }

    public string Low
    {
        get => (_value & 0xFF).ToString("X2");
        set => Write(value, 0xFF, 0);
    }

    private void Write(string text, int mask, int shift)
    {
        if (!int.TryParse(text.Trim().TrimEnd('h', 'H'), NumberStyles.HexNumber, null, out int v)) return;
        v &= mask;
        int full = (_value & ~(mask << shift)) | (v << shift);
        _write((ushort)full);
        Value = (ushort)full;
    }
}

public sealed class FlagItem : ObservableObject
{
    private readonly Action<bool> _write;
    private bool _value;
    private bool _changed;

    public FlagItem(string name, string descriptionKey, Action<bool> write)
    {
        Name = name;
        DescriptionKey = descriptionKey;
        _write = write;
    }

    public string Name { get; }
    public string DescriptionKey { get; }

    public bool Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value)) return;
            _write(value);
        }
    }

    /// <summary>Updates the value from the CPU without writing back.</summary>
    public void Load(bool value)
    {
        Changed = value != _value;
        _value = value;
        OnPropertyChanged(nameof(Value));
    }

    public bool Changed
    {
        get => _changed;
        private set => Set(ref _changed, value);
    }
}

public sealed record StackRow(string Address, string Value, bool IsTop);

public sealed record DisassemblyRow(int Address, string AddressText, string Bytes, string Text, bool IsCurrent, bool HasBreakpoint);

public sealed record VariableRow(string Name, string Address, string Type, string Value, int PhysicalAddress, int ElementSize, int Length);

public enum ValueFormat { Hex, Decimal, Ascii }

public sealed record DiagnosticItem(DiagnosticSeverity Severity, string File, int Line, string Message)
{
    public bool IsError => Severity == DiagnosticSeverity.Error;
}

public enum OutputKind { Info, Success, Warning, Error }

public sealed record OutputLine(string Time, string Text, OutputKind Kind);
