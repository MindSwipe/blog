using System;

namespace MyWasmSolution.Code;

public class Program
{
    public static void Main()
    {
        Native.CallHost();
    }

    public static void SayHello()
    {
        Native.Print("Hello from the Client!");
    }

    public static string Concat(string left, string right)
    {
        return left + right;
    }

    public static int Increment(int i)
    {
        return i + 1;
    }
}