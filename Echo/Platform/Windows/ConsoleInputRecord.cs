using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

/// <summary>A Win32 INPUT_RECORD, with its keyboard/focus union beginning at byte four.</summary>
[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 20)]
internal struct ConsoleInputRecord
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(4)] public int IsDownOrFocused;
    [FieldOffset(8)] public ushort Repeat;
    [FieldOffset(10)] public ushort Key;
    [FieldOffset(12)] public ushort Scan;
    [FieldOffset(14)] public char Character;
    [FieldOffset(16)] public uint Modifiers;
}