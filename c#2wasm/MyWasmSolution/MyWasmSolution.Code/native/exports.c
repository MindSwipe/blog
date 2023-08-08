#include <mono-wasi/driver.h>
#include <assert.h>
#include <string.h>
#include <stdlib.h>

//__wasm_call_ctors();

MonoMethod* method_SayHelloCall;

__attribute__((export_name("__say_hello")))
void __say_helo() {
    if (!method_SayHelloCall) {
        method_SayHelloCall = lookup_dotnet_method("MyWasmSolution.Code.dll", "MyWasmSolution.Code", "Program", "SayHello", -1);
        assert(method_SayHelloCall);
    }

    MonoObject* exception;
    mono_wasm_invoke_method(method_SayHelloCall, NULL, NULL, &exception);
    assert(!exception);
}

MonoMethod* method_IncrementCall;

__attribute__((export_name("__increment")))
size_t __increment(size_t value)
{
    if (!method_IncrementCall) {
        method_IncrementCall = lookup_dotnet_method("MyWasmSolution.Code.dll", "MyWasmSolution.Code", "Program", "Increment", -1);
        assert(method_IncrementCall);
    }

    void* method_params[] = { &value };
    MonoObject* exception;
    MonoObject* result = mono_wasm_invoke_method(method_IncrementCall, NULL, method_params, &exception);
    assert(!exception);

    size_t int_result = *(size_t*)mono_object_unbox(result);
    return int_result;
}

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
