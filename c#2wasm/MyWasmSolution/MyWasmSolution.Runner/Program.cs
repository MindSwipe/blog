using System;
using System.Text;
using Wasmtime;

using var engine = new Engine();
using var linker = new Linker(engine);
using var store = new Store(engine);

store.SetWasiConfiguration(
    new WasiConfiguration()
        .WithInheritedStandardOutput()
        .WithInheritedStandardError()
);
linker.DefineWasi();

using var module = Module.FromFile(engine, "../../../../MyWasmSolution.Code/bin/Debug/net7.0/MyWasmSolution.Code.wasm");

linker.Define(
    "env",
    "call_host",
    Function.FromCallback(store, () =>
    {
        Console.WriteLine("Hello from the Host!");
    })
);

linker.Define(
    "env",
    "print",
    Function.FromCallback(store, (Caller caller, int value_ptr, int value_len) =>
    {
        // Read the string from the WASM memory
        var memory = caller.GetMemory("memory");
        if (memory is null)
            throw new Exception("Missing export 'memory'");

        var value = memory.ReadString(value_ptr, value_len, Encoding.UTF8);

        // Actually print
        Console.WriteLine(value);

        var free = caller.GetFunction("free")!;
        free.Invoke(value_ptr);
    })
);

var instance = linker.Instantiate(store, module);
var start = instance.GetAction("_start")!;
start();

var sayHello = instance.GetAction("__say_hello")!;
sayHello();

var increment = instance.GetFunction<int, int>("__increment")!;
Console.WriteLine($"Got {increment(1)} back from the client");

var memory = instance.GetMemory("memory")!;
var malloc = instance.GetFunction<int, int>("malloc")!;

var leftLen = Encoding.UTF8.GetBytes("Hello ").Length;
var leftPtr = malloc(leftLen /*+ 1*/);
var rightLen = Encoding.UTF8.GetBytes("World").Length;
var rightPtr = malloc(rightLen + 1);

memory.WriteString(leftPtr, "Hello ", Encoding.UTF8);
memory.WriteByte(leftPtr + leftLen, 0);
memory.WriteString(rightPtr, "World", Encoding.UTF8);
memory.WriteByte(rightPtr + rightLen, 0);

var concat = instance.GetFunction<int, int, int>("__concat")!;
var resultPtr = concat(leftPtr, rightPtr);
var resultString = memory.ReadNullTerminatedString(resultPtr);
Console.WriteLine($"Concat got {resultString}");

;