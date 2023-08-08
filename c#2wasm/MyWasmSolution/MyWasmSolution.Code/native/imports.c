#include <mono-wasi/driver.h>
#include <assert.h>
#include <string.h>

__attribute__((import_name("call_host")))
extern void call_host();

__attribute__((import_name("print")))
extern void print(char* value, int value_len);

void mono_print(MonoString* value) {
    char* value_utf8 = mono_wasm_string_get_utf8(value);
    print(value_utf8, strlen(value_utf8));
}

void attach_internal_calls()
{
    mono_add_internal_call("MyWasmSolution.Code.Native::CallHost", call_host);
    mono_add_internal_call("MyWasmSolution.Code.Native::Print", mono_print);
}