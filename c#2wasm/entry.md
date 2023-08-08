# C# -> WAS\[M,I\] and run it from a C# host

Have you ever wanted to compile C# to WASM, only so that you can load that WASM module and execute your code from another C# application? Me neither, but I was bored and tried it anyways, and turns out, it not only works but can also be useful.

## What to expect

I'm not a writer, neither am I an expert on all things C# or WASM, I just like to code and learn things, so don't expect this ramble of words I refuse to call a blog post to be entertaining or offer any deep technical insight. It is much more a short how-to, a starting point for your own adventures in the C# <-> WAS\[M,I\] world.

## A short rundown

### What is WAS\[M/I\]?

Straight from the source: [webassembly.org](https://webassembly.org/)

>WebAssembly (abbreviated Wasm) is a binary instruction format for a stack-based virtual machine. Wasm is designed as a portable compilation target for programming languages, enabling deployment on the web for client and server applications.

In short: It's a binary format, not unlike C#'s Intermediate Language or Java's bytecode, that serves as a common target for multiple languages, allowing different languages to compile to the same format.

While (as the name suggests) it was originally intended to only be ran inside a web-context, i.e inside a browser, people quickly realised the potential of a shared compilation target for multiple languages and started developing ways to get WASM bytecode to run outside of a browser, independent of any web APIs or JavaScript. But any non-trivial application will eventually need interact with the world, and the world is a lot larger than a browser sandbox and thus the WebAssembly System Interface or WASI was born, it is a modular interface to interact with system resources unavailable inside the context of a browser runtime (things like directly reading and writing files).

## Let's get to it

Before we start writing any code, you should know that the entire source code is available in the [MyWasmSolution directory](MyWasmSolution/), so if you get stuck or just want to see the final product you can check it out.

### Compiling C\# to WASM

Let's actually start creating our WASM application by creating our C# scaffold.

First let's create a solution and add our project that we eventually want to compile to WASM.

```shell
dotnet new sln --output MyWasmSolution
cd MyWasmSolution
dotnet new console --output MyWasmSolution.Code
dotnet sln add MyWasmSolution.Code
```

I highly suggest editing the generated `MyWasmSolution.Code.csproj` and removing the `<ImplicitUsings>enable</ImplicitUsings>` to cut down on the amount of bundling done at build, this means you'll have to either add `using System;` to `Program.cs` or change it to `System.Console.WriteLine("Hello, World!");`

Now we need to add the magic sauce that will compile our C# to WASM: [The dotnet-wasi-sdk package](https://github.com/SteveSandersonMS/dotnet-wasi-sdk). Let's also build it for good measure.

```shell
dotnet add MyWasmSolution.Code package Wasi.Sdk --prerelease
dotnet build
```

If you open the `MyWasmSolution.Code/bin/Debug/net7.0` directory you should see a `MyWasmSolution.Code.wasm` file, which means we successfully compiled our C# to WASM, hurray!

### Running it

So, now that we've successfully compiled C# to WASM, it's time to run it, I'll be doing it with [Wasmtime](https://wasmtime.dev/) but you can use [Wasmer](https://wasmer.io/) or any other WASM runtime you prefer. Or, you can skip this step and go directly to [running it with C#](/c#2wasm/entry.md#running-it-with-c).

```shell
wasmtime .\MyWasmSolution.Code\bin\Debug\net7.0\MyWasmSolution.Code.wasm
```

You should see your `Hello, World!` in your console. Congrats, you just ran a WASI application, it interacted with the world around it and all!

### Running it with C\#

Let's actually get to what we're here for: Running it from C#. To do this, we'll create another project in our solution and add the [wasmtime-dotnet package](https://github.com/bytecodealliance/wasmtime-dotnet)

```shell
dotnet new console --output MyWasmSolution.Runner
dotnet sln add MyWasmSolution.Runner
dotnet add MyWasmSolution.Runner package wasmtime
```

Now it's time to do some coding, open the solution in your favourite IDE and open the `Program.cs` of the "Runner" project, I don't like implicit usings so I'll remove those from the `.csproj` again.

Now for the actual code, first we'll want to initialise our Wasmtime prerequisites

```csharp
using Wasmtime;

using var engine = new Engine();
using var linker = new Linker(engine);
using var store = new Store(engine);
```

Now, this is important, since our C# application is a WASI application we need to set a configuration in our `Store` and define the WASI interface on our `Linker` to support that, without this our runner will crash and burn

```csharp
store.SetWasiConfiguration(
    new WasiConfiguration()
        .WithInheritedStandardOutput()
);
linker.DefineWasi();
```

By adding `WithInheritedStandardOutput()` we're telling it that the WASI code inherits our process's standard out, that way we can actually see the `Hello, World!` we are printing.

Now let's load and instatiate our WASM, it's as easy as

```csharp
// The path will be ../../../../MyWasmSolution.Code/bin/Debug/net7.0/MyWasmSolution.Code.wasm
// if you did what I did
using var module = Module.FromFile(engine, "my/path/to/MyWasmSolution.Code.wasm");
var instance = linker.Instantiate(store, module);
```

We can now inspect `module` and see that it has some exports, notably `memory` and `_start`, it also has 34 imports, these are the methods defined in the WASI specification.

So, how de actually run our WASM now? Well, I've technically been lying this whole time, we didn't actually compile our C# to WASM directly, what we did is bundle the .NET runtime which has been compiled to WASM and our C# .dll into one WASM file, and the `_start` export is the method we use to start the .NET runtime, so let's call it

```csharp
var start = instance.GetAction("_start");
start();
```

If you run this now, you should see `Hello, World!` in your console, we just successfully ran some C# code, which has been compiled to WASM, from a C# host, congrats!

### Interop with the WASM code

But just starting the runtime and running the main method is boring, we want to actually interop with our code, so how do we do this? Sadly, since the `Wasi.Sdk` package is still in prerelease, the developer experience is less than optimal, and also scarcely documented, but through some trial and error I managed to figure it out.

First, we'll start treating our `MyWasmSolution.Code` project as a class library instead of a console application, but we need to leave `<OutputType>Exe</OutputType>` as well as a `public static void Main` method, since otherwise we calling `_start` to start the .NET runtime will fail with an error message similar to

>[wasm_trace_logger] * Assertion at /home/runner/work/dotnet-wasi-sdk/dotnet-wasi-sdk/modules/runtime/src/mono/mono/metadata/loader.c:1817, condition `mono_metadata_token_table (m->token) == MONO_TABLE_METHOD' not met

And because calling any method without having called `_start` fails, we need to have this. I have not yet found out why exactly though, so if you know something I don't please contact me.

Now with a `Program.cs` with a class `Program` and an empty `public static void Main` method, we add a method to export to it.

```csharp
using System;

namespace MyWasmSolution.Code;

public class Program
{
    public static void Main() { }

    public static void SayHello()
    {
        Console.WriteLine("Hello from the Client!");
    }
}
```

Also add a `build` and `native` directory to the root of the `MyWasmSolution.Code` directory and add a `MyWasmSolution.Code.props` and a `MyWasmSolution.Code.targets` file to the `build` directory, the `MyWasmSolution.Code.props` file should consist of only this

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
</Project>
```

and the `MyWasmSolution.Code.targets` should be

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

  <ItemGroup>
    <WasiNativeFileReference Include="$(MSBuildThisFileDirectory)..\native\exports.c" />
  </ItemGroup>

</Project>
```

It's important to `Import` the `MyWasmSolution.Code.targets` in your `MyWasmSolution.Code.csproj` file, add the following XML element as a root element

```xml
<Import Project="build\MyWasmSolution.Code.targets"/>
```

Your entire `.csproj` should now look something like

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <Import Project="build\MyWasmSolution.Code.targets" />

  <ItemGroup>
    <PackageReference Include="Wasi.Sdk" Version="0.1.4-preview.10020" />
  </ItemGroup>

</Project>

```

Now, inside the `native` directory, add a `exports.c` file, to this file we'll add additional exports to our WASM application needs as well as hook these exports up. Here's some code, I'll explain it after

```c
#include <mono-wasi/driver.h>
#include <assert.h>
#include <string.h>

MonoMethod* method_SayHelloCall;

__attribute__((export_name("__say_hello")))
void __say_helo() {
    if (!method_SayHelloCall) {
        method_SayHelloCall = lookup_dotnet_method("MyWasmSolution.Code.dll", "MyWasmSolution.Code", "Program", "SayHello",-1);
        assert(method_SayHelloCall);
    }

    MonoObject* exception;
    mono_wasm_invoke_method(method_SayHelloCall, NULL, NULL, &exception);
    assert(!exception);
}
```

First we need to include some headers to get methods we need, after that we declare a pointer to a `MonoMethod` (Mono is the runtime that was compiled to WASM which we are using), after which we need to declare our exported `__say_hello` method. This is where the developer experience could be improved, currently we need to manually do this for every method we want to export (and import), but for a first stable release a way to do that with C# attributes should be added.

Basically we "cache" a pointer to our `MonoMethod` so we don't have to go looking for it every time, we do that by checking if we already set it, and if not we lookup our desired method. The first argument is which dll the method is in, the second is the namespace, the third is the class and the fourth is the method, and the last is the amount of parameters the method has, set this to -1. After looking for the method, we assert that we found it just to make sure.

The next few lines are for actually calling into .NET, first we declare a pointer to a `MonoObject` to hold any potential exception, then we call the method, the first argument is the method to call, the second is the "this argument", i.e on which object to call the method, since our `SayHello` is static we set this to null, the third is the arguments to pass, since `SayHello` has no arguments we set this to null as well, and the last argument is the where to put the exception is any occurs.

Now make sure to rebuild the solution, everytime we change something in the `MyWasmSolution.Code` project we need to manually do this to rebuild the WASM, otherwise our changes won't be carried over.

Let's edit our runner to call `SayHello`, this is pretty simple

```csharp
var sayHello = instance.GetAction("__say_hello");
sayHello();
```

If you run this and inspect `module` you should see an additional export, our `__say_hello`, if we call it, your console should say `Hello from the Client!`, great, we successfully exported a method to be called from the runtime!

### Supplying a method to our client WASM

But calling into our WASM code is only one way, sometimes our WASM needs to call a method declared outside of it. So, how do we do this? It's actually pretty simple now that we have the `.props` and `.targets` as well as the `exports.c` file, but instead of cluttering our exports file, let's create a `imports.c` file in the same directory as exports and add `<WasiNativeFileReference Include="$(MSBuildThisFileDirectory)..\native\exports.c" />` to our `.targets` file (You can also use wildcards like `*.c`). We'll also add `<WasiAfterRuntimeLoaded Include="attach_internal_calls" />` to our `.targets` file, it will come apparent why soon. Your `MyWasmSolution.Code.targets` file should now look like this

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

  <ItemGroup>
    <WasiNativeFileReference Include="$(MSBuildThisFileDirectory)..\native\exports.c" />
    <WasiNativeFileReference Include="$(MSBuildThisFileDirectory)..\native\imports.c" />
    <WasiAfterRuntimeLoaded Include="attach_internal_calls" />
  </ItemGroup>

</Project>
```

So let's add our import, for that we need to write some more C code, in your `imports.c` add

```c
__attribute__((import_name("call_host")))
extern void call_host();

void attach_internal_calls()
{
    mono_add_internal_call("MyWasmSolution.Code.Native::CallHost", call_host);
}
```

It looks familiar yet different, first we again declare our `call_host` method and add the `import_name` attribute to it, in this case we omitted the `import_module` attribute since by default the methods get imported into the `env` module, which we want. If you want, you can add `__attribute__((import_module("whatever")))` to the first line if you want to import it to another module. After this is our `attach_internal_calls`, this method will be called after the WASI runtime was loaded to hook up our methods, here we simply add a internal call. But to actually call this we need to stub out the `CallHost` method, add a `Native.cs` file to the root of the `MyWasmSolution.Code` project

```csharp
using System.Runtime.CompilerServices;

namespace MyWasmSolution.Code;

internal static class Native
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void CallHost();
}
```

Now we need to define this `call_host` method on our `Linker`, in the `MyWasmSolution.Runner` `Program` add the following before instantiating the module:

```csharp
linker.Define(
    "env",
    "call_host",
    Function.FromCallback(store, () =>
    {
        Console.WriteLine("Hello from the Host!");
    })
);
```

Now if we edit our `MyWasmSolution.Code` project to add a call to `Native.CallHost` somewhere (for example, in the `Main` method), our console should say `Hello from the Host!`.

### Passing arguments back and forth

Ok, so calling WASM from C# and calling C# from WASM is pretty cool, but we are only calling void methods with no arguments, what if I want to pass data back and forth? Well, that too is possible, but it requires more C.

In `Program.cs` of our `MyWasmSolution.Code` project add a method `Increment`

```csharp
public static int Increment(int i)
{
    return i + 1;
}
```

And inside `exports.c` add `#include <stdlib.h>` to the top and this to the body

```c
MonoMethod* method_IncrementCall;

__attribute__((export_name("__increment")))
size_t __increment(size_t value)
{
    if (!method_IncrementCall) {
        method_IncrementCall = lookup_dotnet_method("MyWasmSolution.Code.dll", "MyWasmSolution.Code", "Program", "Increment", -1);
        assert(method_IncrementCall);
    }

    void *method_params[] = { &value };
    MonoObject *exception;
    MonoObject *result = mono_wasm_invoke_method(method_IncrementCall, NULL, method_params, &exception);
    assert(!exception);

    size_t int_result = *(size_t*)mono_object_unbox(result);
    return int_result;
}
```

This is pretty similar, except now we have arguments, since `size_t`/ `int` is a value type it's pretty trivial to pass it around, as we can see we simply create a `void *method_params` and pass that to `mono_wasm_invoke_method`. To get the result from the method call is also pretty easy, simply cast the result of `mono_object_unbox(result)` and voilà.

To call this from our host is also pretty easy

```csharp
var increment = instance.GetFunction<int, int>("__increment");
Console.WriteLine($"Got {increment(1)} back from the client");
```

Note how we're using `instance.GetFunction<int, int>` to get a function that takes and `int` parameter and returns an `int`, running this prints `Got 2 back from the client` in the console, neat!

But those are only ints, what if we want to pass reference types? Something like a string? That's possible as well, but a little harder. Remember the `memory` export? Yeah, we can use that to interact with the memory of our client application, and our client application can tell the host where in it's memory the string is we want. Let's start with a new imported method, inside the `Native` class add

```csharp
[MethodImpl(MethodImplOptions.InternalCall)]
public static extern void Print(string input);
```

Now let's edit the `imports.c` file

```c
__attribute__((import_name("print")))
extern void print(char* value, int value_len);

void mono_print(MonoString* value) {
    char* value_utf8 = mono_wasm_string_get_utf8(value);
    print(value_utf8, strlen(value_utf8));
}
```

And inside the `attach_internal_calls` add

```c
mono_add_internal_call("MyWasmSolution.Code.Native::Print", mono_print);
```

Note how we're attaching `mono_print` to our `Native::Print`, this is allows use to declare our C# method with a `string` argument instead of the equivalent of `char* value, int value_len`.

Now we need to define the `print` method in our WASM runtime, so inside the `Program` of `MyWasmSolution.Runner` add

```csharp
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
    })
);
```

Before instantiating our module. Now, change the `SayHello` method to this

```csharp
public static void SayHello()
{
    Native.Print("Hello from the Client!");
}
```

If you run now the console should still say `Hello from the Client!`, same as before, but we can now remove `WithInheritedStandardOutput()` from our `WasiConfiguration` and our client app can still print to standard out!

But that's sending a string to the host, what about sending a string to the client? Well, that's a little more involved since we need to do the low level `string` to `char*` in C#, like so

```csharp
var memory = instance.GetMemory("memory");
var malloc = instance.GetFunction<int, int>("malloc");

var left_len = Encoding.UTF8.GetBytes("Hello ").Length;
var left_ptr = malloc(left_len + 1);
var right_len = Encoding.UTF8.GetBytes("World").Length;
var right_ptr = malloc(right_len + 1);

memory.WriteString(left_ptr, "Hello ", Encoding.UTF8);
memory.WriteByte(left_ptr + left_len, 0);
memory.WriteString(right_ptr, "World", Encoding.UTF8);
memory.WriteByte(right_ptr + right_len, 0);

var concat = instance.GetFunction<int, int, int>("__concat")!;
var resultPtr = concat(left_ptr, right_ptr);
var resultString = memory.ReadNullTerminatedString(resultPtr);
Console.WriteLine($"Concat got {resultString}");
```

First we need to get the memory of our WASM app, then also get the `malloc` function, after this we need to get the amount of memory we need to allocate, this the amount of bytes of our string. Now we allocate that much + 1 for null-termination (this is because strings in C are an array of bytes terminated by a NULL byte). After doing this for both parts, we then write the strings to memory and a NULL byte after it.[<sup>1</sup>](#sidenote-null-terminate)

Now we can get the `__concat` function, as we we'll see shortly it takes two ints (pointers in `memory`) and returns an int (a pointer in `memory` where our result is store), we call it, and use the returned pointer to read a NULL terminated string out of our WASM apps memory.

<sup id="sidenote-null-terminate">1: Sidenote, instead of doing this manually like so, we could also make sure to manually null-terminate our strings, like `memory.WriteString(left_ptr, "Hello \0", Encoding.UTF8)` (also make sure to get the length of the null terminated string)</sup>

Now for the glue C code, in `exports.c`

```c
MonoMethod* method_ConcatCall;

__attribute__((export_name("__concat")))
char* __concat(char* left, char* right) {
    if (!method_ConcatCall) {
        method_ConcatCall = lookup_dotnet_method("MyWasmSolution.Code.dll", "MyWasmSolution.Code", "Program", "Concat", -1);
        assert(method_ConcatCall);
    }

    MonoString* left_string = mono_wasm_string_from_js(left);
    MonoString* right_string = mono_wasm_string_from_js(right);

    void* method_params[] = { left_string, right_string };
    MonoObject* exception;
    MonoObject* result = mono_wasm_invoke_method(method_ConcatCall, NULL, method_params, &exception);
    assert(!exception);
    free(left);
    free(right);

    MonoString* string_result = (MonoString*)result;
    char* utf8_result = mono_wasm_string_get_utf8(string_result);
    return utf8_result;
}
```

The general structure is very similar to our other exported methods, but it's also somewhat different. I think the code is pretty self explanatory, but I do want to note that we're directly casting our `result` to a `MonoString*` instead of using `mono_object_unbox`, this is because `string` is a reference type, and calling `mono_object_unbox` on a non-value type will result in an assertion failure, so if you ever get an error like

>Assertion at [...] object-internals.h, condition `m_class_is_valuetype (mono_object_class (obj))' not met

Then you know you called `mono_object_unbox` on a reference type.

Now for the C# client side, in `Program.cs` add

```csharp
public static string Concat(string left, string right)
{
    return left + right;
}
```

And run the project, you should see `Concat got Hello World` in your console, which means you successfully passed strings into the client app, hooray!

## That's all folks

And that's it. Remember WASI is very much still in it's infancy, and C# WASI doubly so, I fully expect (and hope) this to be useless information in the not so distance future.
