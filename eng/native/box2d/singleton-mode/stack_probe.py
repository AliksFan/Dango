"""Execute a bounded existing probe on an explicit Windows 1 MiB reserve."""
import argparse, ctypes as C, json, pathlib, runpy, sys, traceback

parser = argparse.ArgumentParser()
parser.add_argument('--receipt', required=True)
parser.add_argument('script')
parser.add_argument('arguments', nargs=argparse.REMAINDER)
args = parser.parse_args()
kernel = C.WinDLL('kernel32', use_last_error=True)
SIZE, V, DWORD = C.c_size_t, C.c_void_p, C.c_uint32

class MBI(C.Structure):
    _fields_ = [('BaseAddress', V), ('AllocationBase', V), ('AllocationProtect', DWORD),
                ('PartitionId', C.c_uint16), ('RegionSize', SIZE), ('State', DWORD),
                ('Protect', DWORD), ('Type', DWORD)]

kernel.GetCurrentThreadStackLimits.argtypes = [C.POINTER(SIZE), C.POINTER(SIZE)]
kernel.VirtualQuery.argtypes = [V, C.POINTER(MBI), SIZE]
kernel.VirtualQuery.restype = SIZE
CALLBACK = C.WINFUNCTYPE(DWORD, V)
kernel.CreateThread.argtypes = [V, SIZE, CALLBACK, V, DWORD, C.POINTER(DWORD)]
kernel.CreateThread.restype = V
kernel.WaitForSingleObject.argtypes = [V, DWORD]
kernel.GetExitCodeThread.argtypes = [V, C.POINTER(DWORD)]
kernel.CloseHandle.argtypes = [V]
receipt = {'requestedReserveBytes': 1048576, 'createThreadFlag': 'STACK_SIZE_PARAM_IS_A_RESERVATION',
           'script': args.script, 'arguments': args.arguments, 'passed': False}

@CALLBACK
def run(_):
    low, high, memory = SIZE(), SIZE(), MBI()
    try:
        kernel.GetCurrentThreadStackLimits(C.byref(low), C.byref(high))
        assert kernel.VirtualQuery(V(high.value-1), C.byref(memory), C.sizeof(memory)) == C.sizeof(memory)
        actual = high.value-memory.AllocationBase
        receipt.update({'reportedLow': low.value, 'reportedHigh': high.value,
                        'allocationBase': memory.AllocationBase, 'actualReserveBytes': actual})
        pathlib.Path(args.receipt).write_text(json.dumps(receipt, indent=2)+'\n')
        assert actual == 1048576, receipt
        sys.argv = [args.script] + args.arguments
        try:
            runpy.run_path(args.script, run_name='__main__')
        except SystemExit as error:
            if error.code not in (None, 0): raise
        receipt['passed'] = True
        pathlib.Path(args.receipt).write_text(json.dumps(receipt, indent=2)+'\n')
        return 0
    except BaseException:
        receipt['error'] = traceback.format_exc()
        pathlib.Path(args.receipt).write_text(json.dumps(receipt, indent=2)+'\n')
        return 1

tid = DWORD()
thread = kernel.CreateThread(None, 1048576, run, None, 0x10000, C.byref(tid))
assert thread, C.get_last_error()
assert kernel.WaitForSingleObject(thread, 0xffffffff) == 0
code = DWORD()
assert kernel.GetExitCodeThread(thread, C.byref(code))
kernel.CloseHandle(thread)
print(json.dumps(receipt, indent=2))
raise SystemExit(code.value)
