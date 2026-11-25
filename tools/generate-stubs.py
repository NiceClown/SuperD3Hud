#!/usr/bin/env python3
"""Generates safe-default implementations for the plugin API interfaces.

The public plugin API declares around a hundred interfaces. Writing a class for
each by hand is not practical, and leaving a property returning null crashes
plugins. This generator emits, for every interface that the host does not
implement itself, a class whose members return a safe value:

  * numbers      -> 0
  * bool         -> false
  * string       -> ""
  * enums        -> default
  * collections  -> an empty list of the right type
  * other ifaces -> a shared stub instance of that interface

Controllers and core game objects with real data are written by hand in the
normal source tree and are listed in HANDWRITTEN, so the generator leaves them
alone.

The output is one file, regenerated on demand. Nothing in it talks to the game.
"""
import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IFACE = os.path.join(ROOT, "compat", "interfaces")
OUT = os.path.join(ROOT, "src", "turbohost", "HostStubs.g.cs")

# Interfaces implemented by hand; the generator must not touch them.
HANDWRITTEN = {
    # Controllers
    "IController", "IGameController", "IRenderController", "IWindow",
    "ITimeController", "ISnoController", "IInputController",
    "IInventoryController", "IQueueController", "ITextureController",
    "IStatController", "ITextLogController", "ISoundController",
    "ITrackerController", "IAvoidanceController", "ISceneRevealController",
    "IActorQueryController", "ITargetController", "IInteractionController",
    "IInventoryManagementController",
    # Core game objects
    "IActor", "IPlayer", "IMonster", "IItem", "IScene", "IBuff", "IHeadStone",
    # SNO data with real lookups
    "ISnoItem", "ISnoPower", "ISnoActor", "ISnoMonster", "ISnoArea",
    "ISnoQuest", "IAttribute",
    # Huge catalogues, generated separately by generate-catalogues.py
    "ISnoItemList", "ISnoPowerList", "ISnoQuestList", "IAttributeList",
    # Infrastructure
    "IWatch", "IPerfCounter", "IBrush", "IFont", "ITexture", "IUiElement",
    "IWorldCoordinate", "IScreenCoordinate", "IQueueItem",
}

# Interfaces with generic methods are skipped: their hand-written
# implementations provide the exact constraints.
GENERIC_METHODS = {"IController", "IQueueController"}

PRIMITIVES = {
    "int": "0", "uint": "0u", "long": "0L", "ulong": "0UL", "short": "(short)0",
    "ushort": "(ushort)0", "byte": "(byte)0", "sbyte": "(sbyte)0",
    "float": "0f", "double": "0d", "decimal": "0m", "bool": "false",
    "string": '""', "char": "'\\0'", "void": "",
}

# Structs that must be constructed rather than left null.
STRUCTS = {
    "Rectangle", "RectangleF", "Point", "PointF", "Size", "SizeF", "Color",
}

INTERFACE_RE = re.compile(
    r"public\s+interface\s+([A-Za-z0-9_]+)\s*(?::\s*([A-Za-z0-9_,\s]+?))?\s*\{", re.S)
ENUM_RE = re.compile(r"public\s+enum\s+([A-Za-z0-9_]+)")

PROPERTY_RE = re.compile(
    r"^\s*(.+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get\s*;\s*(set\s*;\s*)?\}\s*;?\s*$",
    re.M)
METHOD_RE = re.compile(
    r"^\s*(.+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(<[^>]+>)?\s*\(([^)]*)\)\s*"
    r"(?:where[^;]*)?\s*;\s*$", re.M)


def strip_comments(text):
    """Removes // comments that are not inside a string literal."""
    out = []
    for line in text.split("\n"):
        in_string = False
        escape = False
        cut = len(line)
        for i, ch in enumerate(line):
            if escape:
                escape = False
                continue
            if ch == "\\":
                escape = True
                continue
            if ch == '"':
                in_string = not in_string
                continue
            if not in_string and ch == "/" and i + 1 < len(line) and line[i + 1] == "/":
                cut = i
                break
        out.append(line[:cut])
    return "\n".join(out)


def find_interfaces():
    """Yields (name, bases, body) for every public interface."""
    for dirpath, _dirs, files in os.walk(IFACE):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(dirpath, name)
            raw = io.open(path, encoding="utf-8-sig").read()
            text = strip_comments(raw)
            for match in INTERFACE_RE.finditer(text):
                iface = match.group(1)
                bases = [b.strip() for b in (match.group(2) or "").split(",") if b.strip()]
                depth = 1
                index = match.end()
                while index < len(text) and depth > 0:
                    if text[index] == "{":
                        depth += 1
                    elif text[index] == "}":
                        depth -= 1
                    index += 1
                yield iface, bases, text[match.end():index - 1]


def find_enums():
    names = set()
    for dirpath, _dirs, files in os.walk(IFACE):
        for name in files:
            if not name.endswith(".cs"):
                continue
            text = io.open(os.path.join(dirpath, name), encoding="utf-8-sig").read()
            for match in ENUM_RE.finditer(text):
                names.add(match.group(1))
    return names


def split_top(text, separator=","):
    """Splits on a separator, ignoring separators inside <>, () and []."""
    parts = []
    depth = 0
    current = []
    for ch in text:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == separator and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)
    parts.append("".join(current))
    return parts


def parse_members(body):
    """Returns (properties, methods) where properties are (type, name, setter)."""
    properties = []
    for match in PROPERTY_RE.finditer(body):
        properties.append((match.group(1).strip(), match.group(2),
                           bool(match.group(3))))

    methods = []
    for match in METHOD_RE.finditer(body):
        type_name = match.group(1).strip()
        name = match.group(2)
        generic = (match.group(3) or "").strip()
        args = match.group(4).strip()
        # object.ToString() already satisfies an interface ToString().
        if name == "ToString" and not args and last_segment(type_name) == "string":
            continue
        # A generic method cannot be stubbed without its constraints.
        if generic:
            methods.append((type_name, name, args, generic, True))
            continue
        methods.append((type_name, name, args, "", False))
    return properties, methods


def last_segment(type_name):
    return type_name.strip().split(".")[-1]


def default_for(type_name, enums, interfaces):
    """C# expression that produces a safe value of the given type."""
    clean = type_name.strip()
    last = last_segment(clean)

    if clean in PRIMITIVES:
        return PRIMITIVES[clean]

    if clean.endswith("?"):
        return "null"

    if clean.endswith("[]"):
        inner = clean[:-2]
        return "new " + inner + "[0]"

    generic = re.match(r"^(IEnumerable|ICollection|IList|List)<(.+)>$", clean)
    if generic:
        return "new List<" + generic.group(2) + ">()"

    generic = re.match(r"^(HashSet|ISet)<(.+)>$", clean)
    if generic:
        return "new HashSet<" + generic.group(2) + ">()"

    if last in ("DateTime",):
        return "DateTime.MinValue"
    if last in ("TimeSpan",):
        return "TimeSpan.Zero"

    if last in STRUCTS:
        return "new " + clean + "()"

    if last in enums:
        return "default(" + clean + ")"

    if last.startswith("Func<") or last.startswith("Action") or last.startswith("Tuple"):
        return "null"
    if last.startswith("Dictionary"):
        return "null"

    if last in interfaces:
        return "HostStubs.Get<" + clean + ">()"

    return "default(" + clean + ")"


def parse_parameters(args):
    """Returns a list of (declaration, name, is_out) for a method."""
    result = []
    if not args.strip():
        return result
    for raw in split_top(args):
        raw = raw.strip()
        if not raw:
            continue
        # Drop a default value at depth zero.
        depth = 0
        cut = len(raw)
        for i, ch in enumerate(raw):
            if ch in "<([":
                depth += 1
            elif ch in ">)]":
                depth -= 1
            elif ch == "=" and depth == 0:
                cut = i
                break
        raw = raw[:cut].strip()
        if not raw:
            continue
        parts = raw.rsplit(" ", 1)
        if len(parts) != 2:
            continue
        decl = parts[0].strip()
        name = parts[1].strip()
        is_out = decl.startswith("out ") or decl.startswith("ref ")
        result.append((decl + " " + name, name, is_out, decl))
    return result


def collect_members(iface, own_props, own_methods, bases, by_name, seen=None):
    """Gathers inherited members so a stub satisfies the whole interface."""
    if seen is None:
        seen = set()
    if iface in seen:
        return [], []
    seen.add(iface)

    props = list(own_props)
    methods = list(own_methods)
    for base in bases:
        if base not in by_name:
            continue
        bprops, bmethods, bbases = by_name[base]
        iprops, imethods = collect_members(base, bprops, bmethods,
                                           bbases, by_name, seen)
        props.extend(iprops)
        methods.extend(imethods)

    # Deduplicate by member name, keeping the first declaration.
    seen_props = set()
    unique_props = []
    for p in props:
        if p[1] in seen_props:
            continue
        seen_props.add(p[1])
        unique_props.append(p)

    seen_methods = set()
    unique_methods = []
    for m in methods:
        key = (m[1], len(m[2]))
        if key in seen_methods:
            continue
        seen_methods.add(key)
        unique_methods.append(m)

    return unique_props, unique_methods


def main():
    interfaces = list(find_interfaces())
    enums = find_enums()

    parsed = {}
    for iface, bases, body in interfaces:
        props, methods = parse_members(body)
        parsed[iface] = (props, methods, bases)

    interface_names = set(parsed.keys())

    out = []
    w = out.append

    w("// <auto-generated>")
    w("//   Generated by tools/generate-stubs.py from the public plugin API.")
    w("//   Do not edit by hand: change the generator instead.")
    w("// </auto-generated>")
    w("using System;")
    w("using System.Collections.Generic;")
    w("using System.Drawing;")
    w("using System.Windows.Forms;")
    w("using System.Media;")
    w("using SharpDX.Direct2D1;")
    w("using SharpDX.DirectInput;")
    w("using SharpDX.DirectWrite;")
    w("using Turbo.Plugins;")
    w("")
    w("namespace SuperD3Hud.TurboHost")
    w("{")
    w("    /// <summary>")
    w("    /// Safe-default implementations of the plugin API interfaces that the")
    w("    /// host does not fill with real data. They never return null, so a plugin")
    w("    /// reading an unsupported field gets a harmless value instead of a crash.")
    w("    /// </summary>")
    w("    public static class HostStubs")
    w("    {")
    w("        private static readonly Dictionary<Type, object> Cache =")
    w("            new Dictionary<Type, object>();")
    w("        private static readonly object Sync = new object();")
    w("")
    w("        /// <summary>Registers a real implementation, replacing a stub.</summary>")
    w("        public static void Register<T>(T instance) where T : class")
    w("        {")
    w("            lock (Sync) Cache[typeof(T)] = instance;")
    w("        }")
    w("")
    w("        /// <summary>Returns the shared stub of an interface type.</summary>")
    w("        public static T Get<T>() where T : class")
    w("        {")
    w("            lock (Sync)")
    w("            {")
    w("                object value;")
    w("                if (Cache.TryGetValue(typeof(T), out value)) return (T)value;")
    w("                Type stub = typeof(HostStubs).Assembly.GetType(")
    w('                    "SuperD3Hud.TurboHost.Stub" + typeof(T).Name);')
    w("                if (stub == null) return null;")
    w("                try { value = Activator.CreateInstance(stub); }")
    w("                catch { return null; }")
    w("                Cache[typeof(T)] = value;")
    w("                return (T)value;")
    w("            }")
    w("        }")
    w("    }")
    w("")

    emitted = 0
    skipped = []
    for iface, _bases, _body in interfaces:
        if iface in HANDWRITTEN:
            continue
        if not iface.startswith("I") or len(iface) < 2 or not iface[1].isupper():
            continue

        own_props, own_methods, bases = parsed[iface]
        props, methods = collect_members(iface, own_props, own_methods,
                                         bases, parsed)

        # Generic methods need explicit constraints, so skip those interfaces.
        if any(m[4] for m in methods):
            skipped.append(iface)
            continue

        if not props and not methods and not bases:
            continue

        base_list = ", ".join([iface] + [b for b in bases if b in interface_names])
        w("    /// <summary>Safe default of " + iface + ".</summary>")
        w("    public class Stub" + iface + " : " + base_list)
        w("    {")

        for type_name, prop, has_setter in props:
            if has_setter:
                w("        public " + type_name + " " + prop + " { get; set; }")
            else:
                w("        public " + type_name + " " + prop +
                  " { get { return " + default_for(type_name, enums, interface_names) + "; } }")

        for type_name, name, args, _generic, _skip in methods:
            params = parse_parameters(args)
            decls = ", ".join([p[0] for p in params])
            w("        public " + type_name + " " + name + "(" + decls + ")")
            w("        {")
            for decl, pname, is_out, raw_decl in params:
                if is_out:
                    value_type = raw_decl
                    if value_type.startswith("out "):
                        value_type = value_type[4:].strip()
                    elif value_type.startswith("ref "):
                        value_type = value_type[4:].strip()
                    w("            " + pname + " = default(" + value_type + ");")
            value = default_for(type_name, enums, interface_names)
            if value:
                w("            return " + value + ";")
            w("        }")

        w("    }")
        w("")
        emitted += 1

    w("}")

    io.open(OUT, "w", encoding="utf-8", newline="").write("\n".join(out) + "\n")
    print("generated", OUT)
    print("interfaces emitted:", emitted, "of", len(interfaces))
    if skipped:
        print("skipped (generic methods):", ", ".join(sorted(skipped)))


if __name__ == "__main__":
    main()
