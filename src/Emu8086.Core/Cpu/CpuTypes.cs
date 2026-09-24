namespace Emu8086.Core.Cpu;

[Flags]
public enum CpuFlags : ushort
{
    None = 0,
    CF = 0x0001,
    PF = 0x0004,
    AF = 0x0010,
    ZF = 0x0040,
    SF = 0x0080,
    TF = 0x0100,
    IF = 0x0200,
    DF = 0x0400,
    OF = 0x0800,
}

public enum StepResult
{
    Ok,
    /// <summary>The instruction needs input that is not available yet; it will be retried.</summary>
    Waiting,
    Halted,
}

public enum InterruptResult
{
    /// <summary>Service finished; execution continues after the INT.</summary>
    Handled,
    /// <summary>Service must block (e.g. waiting for a key); the INT is retried later.</summary>
    Wait,
    /// <summary>Program terminated.</summary>
    Halt,
    /// <summary>No native service; dispatch through the vector table.</summary>
    NotHandled,
}

public interface IPortBus
{
    int In(int port, bool word);
    void Out(int port, int value, bool word);
}

/// <summary>Native implementation of BIOS/DOS services.</summary>
public interface IInterruptHandler
{
    InterruptResult Handle(int vector, Cpu8086 cpu);
}

/// <summary>Complete register file; used for snapshots, step-back and the UI.</summary>
public readonly record struct CpuState(
    ushort AX, ushort BX, ushort CX, ushort DX,
    ushort SI, ushort DI, ushort BP, ushort SP,
    ushort CS, ushort DS, ushort ES, ushort SS,
    ushort IP, ushort Flags, bool Halted);
