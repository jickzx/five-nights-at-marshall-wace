#!/usr/bin/env python3
"""Compile-check the Fnmw assemblies of a checkout without opening it in Unity.

    python3 Unity/Tools/cc.py                 # check the checkout this script is in
    python3 Unity/Tools/cc.py Fnmw.Core Fnmw.Game

It reuses the compiler arguments Unity wrote for another checkout that has been opened
(default: the main checkout, override with FNMW_LIBRARY=<path to a Unity/Library>), swaps in
this checkout's sources, and runs Unity's own csc. Output goes to a temp folder, so an open
Editor is not disturbed. It only compiles: it does not run tests. An assembly whose asmdef
is new since that Library was built cannot be checked and is reported as skipped.
"""
import glob
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)  # .../Unity
DEFAULT_LIBRARY = "/Volumes/DeveloperStorage/GitHub/five-nights-at-marshall-wace/Unity/Library"
DROP = ("-refout:", "-analyzer:", "/additionalfile:", "-additionalfile:", "/generatedfilesout:", "-generatedfilesout:")


def find_tools():
    for root in sorted(glob.glob("/Applications/Unity/Hub/Editor/*/Unity.app/Contents"), reverse=True):
        dotnet = os.path.join(root, "Resources/Scripting/DotNetSdk/dotnet")
        csc = glob.glob(os.path.join(root, "Resources/Scripting/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll"))
        if os.path.exists(dotnet) and csc:
            return dotnet, csc[0]
    sys.exit("cc.py: no Unity editor with a bundled csc found")


def asmdefs():
    """assembly name -> folder, for every asmdef under Assets."""
    found = {}
    for path in glob.glob(os.path.join(PROJECT, "Assets/**/*.asmdef"), recursive=True):
        m = re.search(r'"name"\s*:\s*"([^"]+)"', open(path, encoding="utf-8").read())
        if m:
            found[m.group(1)] = os.path.dirname(path)
    return found


def sources(folder, all_folders):
    nested = [f for f in all_folders if f != folder and f.startswith(folder + os.sep)]
    out = []
    for root, dirs, files in os.walk(folder):
        dirs[:] = [d for d in dirs if not d.endswith("~") and not d.startswith(".")
                   and os.path.join(root, d) not in nested]
        out += [os.path.join(root, f) for f in files if f.endswith(".cs")]
    return sorted(out)


def main():
    library = os.environ.get("FNMW_LIBRARY", DEFAULT_LIBRARY)
    dags = glob.glob(os.path.join(library, "Bee/artifacts/*.dag"))
    if not dags:
        sys.exit("cc.py: no Bee artifacts in " + library + " (open that checkout in Unity once)")
    dag = max(dags, key=os.path.getmtime)
    lib_project = os.path.dirname(library)
    dotnet, csc = find_tools()
    defs = asmdefs()
    wanted = sys.argv[1:] or sorted(defs)
    out_dir = tempfile.mkdtemp(prefix="fnmw-cc-")

    # Our own assemblies each one references, read from Unity's response files.
    plans = {}
    for name in defs:
        rsp = os.path.join(dag, name + ".rsp")
        if not os.path.exists(rsp):
            continue
        lines = [l.rstrip("\n") for l in open(rsp, encoding="utf-8")]
        deps = [d for d in re.findall(r'/(Fnmw\.[\w.]+?)(?:\.ref)?\.dll"', "\n".join(l for l in lines if l.startswith("-r:")))
                if d != name and d in defs]
        plans[name] = (lines, deps)

    order, seen = [], set()

    def visit(n):
        if n in seen or n not in plans:
            return
        seen.add(n)
        for d in plans[n][1]:
            visit(d)
        order.append(n)

    for n in wanted:
        if n not in defs:
            sys.exit("cc.py: unknown assembly " + n)
        if n not in plans:
            print("SKIP  " + n + " (asmdef is newer than the reference Library; compile it in Unity)")
        visit(n)

    failed = False
    built = set()
    for name in order:
        lines, deps = plans[name]
        if any(d not in built for d in deps):
            print("SKIP  " + name + " (a dependency failed)")
            failed = True
            continue
        args = []
        for l in lines:
            if not l or l.startswith(DROP):
                continue
            if l.startswith("-out:"):
                l = '-out:"' + os.path.join(out_dir, name + ".dll") + '"'
            elif l.startswith("-r:"):
                m = re.search(r'/(Fnmw\.[\w.]+?)(?:\.ref)?\.dll"', l)
                if m and m.group(1) in built:
                    l = '-r:"' + os.path.join(out_dir, m.group(1) + ".dll") + '"'
                else:
                    path = l[3:].strip('"')
                    if not os.path.isabs(path):
                        l = '-r:"' + os.path.join(lib_project, path) + '"'
            elif l.startswith('"') and l.endswith('.cs"'):
                path = l.strip('"')
                if path.startswith("Assets/") or path.startswith("Assets\\"):
                    continue  # replaced below with this checkout's files
                if not os.path.isabs(path):
                    l = '"' + os.path.join(lib_project, path) + '"'
            args.append(l)
        args += ['"' + s + '"' for s in sources(defs[name], list(defs.values()))]
        rsp_out = os.path.join(out_dir, name + ".rsp")
        with open(rsp_out, "w", encoding="utf-8") as f:
            f.write("\n".join(args))
        r = subprocess.run([dotnet, csc, "/noconfig", "@" + rsp_out], capture_output=True, text=True, cwd=PROJECT)
        text = (r.stdout + r.stderr).strip()
        errors = [l for l in text.splitlines() if ": error " in l]
        warnings = [l for l in text.splitlines() if ": warning " in l]
        if r.returncode == 0:
            built.add(name)
            print("OK    " + name + (" (" + str(len(warnings)) + " warnings)" if warnings else ""))
            for w in warnings:
                print("      " + w.replace(PROJECT + os.sep, ""))
        else:
            failed = True
            print("FAIL  " + name)
            for e in (errors or text.splitlines()):
                print("      " + e.replace(PROJECT + os.sep, ""))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
