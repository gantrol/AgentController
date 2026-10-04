import argparse
import hashlib
import json
import pathlib
import re
import struct


def balanced(source, start):
    stack = []
    quote = None
    escaped = False
    for index in range(start, len(source)):
        char = source[index]
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
        elif char in "`\"'":
            quote = char
        elif char in "[{(":
            stack.append(char)
        elif char in "]})":
            stack.pop()
            if not stack:
                return source[start:index + 1]
    raise ValueError("Unterminated resource expression")


def literal(source):
    source = re.sub(r'`(?:\\.|[^`\\])*`|"(?:\\.|[^"\\])*"',
                    lambda m: json.dumps(m[0][1:-1].replace("\\`", "`")) if m[0][0] == "`" else m[0], source)
    source = re.sub(r"([{,])([\w$]+):", r'\1"\2":', source)
    return json.loads(source.replace("!0", "true").replace("!1", "false"))


def export(asar, output):
    with asar.open("rb") as stream:
        header = struct.unpack("<4I", stream.read(16))
        tree = json.loads(stream.read(header[3]))
        assets = tree["files"]["webview"]["files"]["assets"]["files"]

        cache = {}

        def read(prefix):
            name = next(n for n in assets if n.startswith(prefix + "-") and n.endswith(".js") and "trash" not in n.lower())
            return read_name(name)

        def read_name(name):
            if "trash" in name.lower():
                raise ValueError("Excluded resource path")
            if name in cache:
                return cache[name]
            entry = assets[name]
            stream.seek(8 + header[1] + int(entry["offset"]))
            data = stream.read(entry["size"])
            sources.append({"path": "webview/assets/" + name, "sha256": hashlib.sha256(data).hexdigest()})
            cache[name] = data.decode("utf-8")
            return cache[name]

        sources = []
        layout = read("codex-micro-layout")
        bridge = read("codex-micro-commands")
        initial = read("app-initial")
        surface = read("codex-micro-keyboard-surface")
        messages = read("command-messages")
        chinese = read("zh-CN")
        # Resolve the exported registry from the installed command filter, not a fixed symbol.
        export_name = re.search(r"(\w+) as i[,}]", bridge).group(1)
        registry = re.search(r"(\w+) as " + export_name + r"[,}]", initial).group(1)
        registry_expression = balanced(initial, initial.index(registry + "=[") + len(registry) + 1)
        arrays = re.findall(r"\w+\((\w+),`([^`]+)`\)", registry_expression)
        handler_map = re.search(r"\w+=new Map\(\[\[`newTask`", initial)
        handler_ids = set(re.findall(r"\[`([^`]+)`", balanced(initial, initial.index("[", handler_map.start()))))
        labels = dict(re.findall(r'id:`([^`]+)`,defaultMessage:`([^`]+)`', messages))
        zh_labels = dict(re.findall(r'"([^"\\]+)":`([^`]+)`', chinese))
        commands = []
        for symbol, kind in arrays:
            if kind == "vscode-only":
                continue
            start = initial.index(symbol + "=[") + len(symbol) + 1
            expression = balanced(initial, start)
            # Expand the sole generated registry family without evaluating app code.
            expression = re.sub(r"\.\.\.\[1,2,3,4,5,6,7,8,9\]\.map\(e=>\((\{.*?\})\)\)",
                                lambda m: ",".join(m[1].replace("${e}", str(i)) for i in range(1, 10)), expression)
            for name, value in re.findall(r"(\w+):\{tool:`[^`]+`,id:`([^`]+)`\}", initial[:start]):
                expression = re.sub(r"\b\w+\." + name + r"\.id\b", "`" + value + "`", expression)
            for entry in literal(expression):
                if kind == "webview" and "electron" not in entry.get("availableIn", ["electron"]):
                    continue
                if kind == "electron-only" and entry["id"] not in handler_ids:
                    continue
                # The Micro settings picker explicitly omits this command.
                if entry["id"] == "personalitySettings":
                    continue
                title = labels.get(entry.get("titleIntlId")) or entry.get("electron", {}).get("menuTitle") or entry["id"]
                zh_title = zh_labels.get(entry.get("titleIntlId")) or zh_labels.get(entry.get("electron", {}).get("menuTitleIntlId")) or title
                commands.append({"id": entry["id"], "label": title, "labelZh": zh_title, "titleIntlId": entry.get("titleIntlId"),
                                 "kind": kind, "group": entry.get("commandMenuGroupKey", "app")})
        start = layout.index("[{id:")
        keycaps = literal(balanced(layout, start))
        for command in commands:
            linked = [k for k in keycaps if k["action"].get("command") == command["id"]]
            command["keycaps"] = [k["id"] for k in linked]
            command["icons"] = list(dict.fromkeys(k["icon"] for k in linked))
        # These are symbol references to the official renderer; they are not invented glyph names.
        mapping = re.search(r'\w+=\{("all-products":.*?)\}\}', surface).group(1)
        icons = {}
        imports = {}
        for spec, resource in re.findall(r'import\{([^}]+)\}from"\./([^"]+)"', surface):
            for exported, local in re.findall(r'([\w$]+) as ([\w$]+)', spec):
                imports[local] = (resource, exported)
        for quoted, plain, symbol in re.findall(r'(?:"([\w-]+)"|(\w+)):(\w+)', mapping):
            name = quoted or plain
            code, resolved, resource = surface, symbol, sources[3]["path"]
            if symbol in imports:
                resource, exported = imports[symbol]
                code = read_name(resource)
                resolved = re.search(r'([\w$]+) as ' + re.escape(exported) + r'[,}]', code[code.rindex("export{"):]).group(1)
                resource = "webview/assets/" + resource
            match = re.search(r'(?<![\w$])' + re.escape(resolved) + r'=\w+=>\(0,[\w$.]+\)\(`svg`,', code)
            glyph = balanced(code, match.end()) if match else ""
            rules = []
            for path_match in re.finditer(r'\(`path`,', glyph):
                path = balanced(glyph, path_match.end())
                rules.append("evenodd" if 'fillRule:`evenodd`' in path else "nonzero")
            icons[name] = {"component": symbol, "resource": resource, "export": resolved,
                           "viewBox": next(iter(re.findall(r'viewBox:`([^`]+)`', glyph)), None),
                           "paths": re.findall(r'\bd:`([^`]+)`', glyph),
                           "fillRules": rules,
                           "transforms": re.findall(r'transform:`([^`]+)`', glyph)}

    output.parent.mkdir(parents=True, exist_ok=True)
    support_source = output.with_name("CodexActionCatalog.cs")
    routes = {}
    for ids, route in re.findall(r'((?:"[^"\n]+"\s*(?:or\s*)?)+)=> "([^"]+)"', support_source.read_text(encoding="utf-8")):
        for command in re.findall(r'"([^"]+)"', ids):
            routes[command] = route
    for command in commands:
        command["softwareRoute"] = routes.get(command["id"])
    for keycap in keycaps:
        keycap["softwareRoute"] = routes.get(keycap["action"].get("command"))
    data = {"package": asar.parents[2].name, "sources": sources, "keycaps": keycaps,
            "commands": commands, "iconComponents": icons,
            "softwareExtensions": [{"id": key, "softwareRoute": value} for key, value in routes.items()
                                   if key not in {c["id"] for c in commands}]}
    output.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    rows = []
    for entry in commands:
        values = [entry["id"], entry["label"], next(iter(entry["keycaps"]), ""), entry["labelZh"]]
        rows.append("        new(" + ", ".join(json.dumps(v, ensure_ascii=False) for v in values) + "),")
    generated = output.with_name("CodexOfficialCommands.g.cs")
    generated.write_text("// Generated by scripts/export-codex-micro-catalog.py from " + data["package"] + "\n"
                         "namespace CodexMicro.Desktop.Services;\n\n"
                         "internal static partial class CodexActionCatalog\n{\n"
                         "    internal static readonly CodexActionDefinition[] Official =\n    [\n" +
                         "\n".join(rows) + "\n    ];\n}\n", encoding="utf-8")
    rows = []
    for name, icon in icons.items():
        if not icon["paths"] or icon["viewBox"] is None:
            raise ValueError("Unresolved official icon: " + name)
        left, top, width, height = map(float, icon["viewBox"].split())
        if left != 0 or top != 0 or len(icon["transforms"]) > 1 or len(icon["paths"]) != len(icon["fillRules"]):
            raise ValueError("Unsupported icon geometry: " + name)
        scale, tx, ty = 1., 0., 0.
        for transform in icon["transforms"]:
            if transform.startswith("scale("):
                scale = float(transform[6:-1])
            elif transform.startswith("translate("):
                tx, ty = map(float, transform[10:-1].split())
            else:
                raise ValueError("Unsupported icon transform: " + transform)
        paths = [json.dumps(("F0 " if rule == "evenodd" else "F1 ") + path)
                 for path, rule in zip(icon["paths"], icon["fillRules"])]
        rows.append("        [" + json.dumps(name) + "] = new(" +
                    ", ".join(str(n) for n in [width, height, scale, tx, ty]) +
                    ", Create(" + ", ".join(paths) + ")),")
    keycap_rows = ["        [" + json.dumps(k["id"]) + "] = " + json.dumps(k["icon"]) + ","
                   for k in keycaps if k["icon"] != "empty"]
    output.with_name("CodexOfficialArtwork.g.cs").write_text(
        "// Generated by scripts/export-codex-micro-catalog.py from " + data["package"] + "\n"
        "namespace CodexMicro.Desktop.Services;\n\ninternal static partial class CodexOfficialArtwork\n{\n"
        "    private static readonly IReadOnlyDictionary<string, Glyph> Icons = new Dictionary<string, Glyph>\n    {\n" +
        "\n".join(rows) + "\n    };\n"
        "    private static readonly IReadOnlyDictionary<string, string> KeycapIcons = new Dictionary<string, string>\n    {\n" +
        "\n".join(keycap_rows) + "\n    };\n}\n", encoding="utf-8")
    print(json.dumps({"commands": len(commands), "keycaps": len(keycaps), "iconComponents": len(icons), "output": str(output)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("asar", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    if "trash" in str(args.asar).lower() or "trash" in str(args.output).lower():
        raise ValueError("Excluded path")
    export(args.asar, args.output)
