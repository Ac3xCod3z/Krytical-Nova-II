"""Runs one heavy program (a batch Unity) so that it can only ever stop itself, and stops it when the PC runs out of memory.

On this 31 GB PC a stuck batch Unity once took the memory the owner's Editor needed. run() starts the program in a
Windows job object, so the program and everything it starts belong to that job and nothing else does; stopping the job
stops exactly that tree, never another Unity found by name. While it runs, the free memory is read every second: below
1 GB the job is stopped at once, below 2 GB for 15 seconds in a row it is stopped too.

    code, why = own_tree.run(cmd)     why is None, or the reason the job was stopped
"""
import ctypes
import os
import subprocess
import time
from ctypes import wintypes

STOP_AT_ONCE = 1 << 30          # below 1 GB free
STOP_AFTER = 2 << 30            # below 2 GB free ...
STOP_AFTER_SECONDS = 15         # ... for this long
STOPPED = 3                     # the exit code run() gives a stopped job


class _MemoryStatus(ctypes.Structure):
    _fields_ = [("dwLength", wintypes.DWORD), ("dwMemoryLoad", wintypes.DWORD),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


def free_memory():
    status = _MemoryStatus(dwLength=ctypes.sizeof(_MemoryStatus))
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
    return status.ullAvailPhys


class _ExtendedLimits(ctypes.Structure):   # JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD), ("IoCounters", ctypes.c_ulonglong * 6),
                ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]


def _job_for(process):
    """A job object holding the process; its children join it as they start. If this watcher itself dies, the job
    closes and takes the tree with it, so the program never runs unwatched."""
    k32 = ctypes.windll.kernel32
    k32.CreateJobObjectW.restype = wintypes.HANDLE
    k32.OpenProcess.restype = wintypes.HANDLE
    job = k32.CreateJobObjectW(None, None)
    limits = _ExtendedLimits(LimitFlags=0x2000)   # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if job:
        k32.SetInformationJobObject(wintypes.HANDLE(job), 9, ctypes.byref(limits), ctypes.sizeof(limits))
    handle = k32.OpenProcess(0x0001 | 0x0100, False, process.pid)   # PROCESS_TERMINATE | PROCESS_SET_QUOTA
    if not job or not handle or not k32.AssignProcessToJobObject(wintypes.HANDLE(job), wintypes.HANDLE(handle)):
        raise OSError(f"could not put process {process.pid} in a job object (error {k32.GetLastError()})")
    k32.CloseHandle(wintypes.HANDLE(handle))
    return job


def run(cmd, below_normal=True):
    """Runs cmd and waits. Returns (exit code, None) or (STOPPED, why) when the memory watch stopped it."""
    flags = 0x00004000 if below_normal else 0   # BELOW_NORMAL_PRIORITY_CLASS
    process = subprocess.Popen(cmd, creationflags=flags)
    if os.name != "nt":
        return process.wait(), None
    k32 = ctypes.windll.kernel32
    try:
        job = _job_for(process)
    except OSError:
        process.kill()   # never run unwatched
        process.wait()
        raise
    low_since = None
    try:
        while process.poll() is None:
            free = free_memory()
            gb = free / (1 << 30)
            if free < STOP_AT_ONCE:
                why = f"free memory fell to {gb:.1f} GB (below 1 GB)"
            elif free < STOP_AFTER and low_since and time.time() - low_since >= STOP_AFTER_SECONDS:
                why = f"free memory stayed below 2 GB for {STOP_AFTER_SECONDS} s ({gb:.1f} GB)"
            else:
                low_since = (low_since or time.time()) if free < STOP_AFTER else None
                time.sleep(1)
                continue
            k32.TerminateJobObject(wintypes.HANDLE(job), STOPPED)
            process.wait()
            return STOPPED, why + ": stopped this run and everything it started, nothing else"
        return process.returncode, None
    finally:
        k32.CloseHandle(wintypes.HANDLE(job))


def shut_build_servers():
    """dotnet keeps MSBuild and compiler servers alive after a build; stop them so the memory comes back."""
    subprocess.run(["dotnet", "build-server", "shutdown"], capture_output=True, text=True)
