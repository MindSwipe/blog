using System.Runtime.CompilerServices;

namespace MyWasmSolution.Code;

internal static class Native
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void CallHost();

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void Print(string input);
}
