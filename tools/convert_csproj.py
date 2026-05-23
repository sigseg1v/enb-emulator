#!/usr/bin/env python3
"""Convert old-style (.NET Framework) csproj to SDK-style targeting net10.0-windows.

Strategy:
- Read RootNamespace, AssemblyName, OutputType from old csproj
- Detect WinForms / WPF references; set <UseWindowsForms>true</UseWindowsForms> if needed
- Keep <Reference Include="..."> blocks with HintPath as-is (they point to vendored DLLs).
  Strip framework assembly references (System, System.Core, System.Xml, System.Data,
  System.Windows.Forms, System.Drawing, mscorlib) since the SDK adds them.
- Keep <ProjectReference> blocks verbatim.
- Drop <Compile Include=...> lines (SDK default is globbed). Same for EmbeddedResource
  and None patterns that the SDK handles automatically — but keep explicit ones with
  unusual attributes (CopyToOutputDirectory, DependentUpon, SubType) under <ItemGroup>.
- Switch MySql.Data 5.x reference to MySqlConnector NuGet package.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

NS = "http://schemas.microsoft.com/developer/msbuild/2003"
ET.register_namespace("", NS)

FRAMEWORK_REFS = {
    "System", "System.Core", "System.Xml", "System.Data", "mscorlib",
    "System.Data.DataSetExtensions", "System.Xml.Linq", "System.Deployment",
    "Microsoft.CSharp", "System.Net.Http", "System.Web", "System.Numerics",
    "System.IO.Compression", "System.IO.Compression.FileSystem",
    "WindowsBase", "PresentationCore", "PresentationFramework",
    "System.Configuration", "System.Management", "System.ServiceProcess",
    "System.Transactions", "System.EnterpriseServices",
    "System.Design", "System.Drawing.Design",
}

# Vendored DLLs under tools/commontools/Libs/release/ — give a uniform HintPath
# regardless of the original csproj's relative path (which depends on nesting).
VENDORED_DLLS = {
    "Meebey.SmartIrc4net": "Meebey.SmartIrc4net.dll",
    "SandDock": "SandDock.dll",
    "UMD.HCIL.Piccolo": "UMD.HCIL.Piccolo.dll",
    "LaMarvin.Windows.Forms.ColorPicker": "LaMarvin.Windows.Forms.ColorPicker.dll",
    "log4net": "log4net.dll",
}


def _vendored_hintpath(csproj_path: str, dll_name: str) -> str:
    """Compute a relative path from the csproj to tools/commontools/Libs/release/<dll>."""
    csproj_dir = os.path.dirname(os.path.abspath(csproj_path))
    target = os.path.abspath(os.path.join(
        os.path.dirname(os.path.abspath(__file__)),
        "commontools", "Libs", "release", dll_name,
    ))
    rel = os.path.relpath(target, csproj_dir)
    return rel.replace("/", "\\")  # csproj convention

# tools/ directory renames after the Phase A copy (kebab-case dirs vs. original).
# Map "..\OldDir\Foo.csproj" -> "..\new-dir\Foo.csproj".
PROJREF_RENAME = {
    "CommonTools": "commontools",
    "DataImport": "dataimport",
    "MissionEditor": "missioneditor",
    "ItemEditor": "itemeditor",
    "EnBPatcher": "enbpatcher",
    "LaunchNet7": "launchnet7",
    "ToolsLauncher": "toolslauncher",
    "ToolsPatcher": "toolspatcher",
    "TalkTreeEditor": "talktreeeditor",
    "ChunkTypes": "chunktypes",
    "UdpDump": "udpdump",
    "Unmix": "unmix",
}


def normalize_projref(path: str) -> str:
    # Backslashes -> forward slashes for Linux.
    p = path.replace("\\", "/")
    parts = p.split("/")
    # Rename only the segment that names a tools/ sibling: the segment that
    # follows a "..". Inner csproj subdirectories keep their original casing.
    fixed = []
    rename_next = False
    for seg in parts:
        if rename_next and seg in PROJREF_RENAME:
            fixed.append(PROJREF_RENAME[seg])
            rename_next = False
        else:
            fixed.append(seg)
            rename_next = (seg == "..")
    return "/".join(fixed)

WINFORMS_REFS = {"System.Windows.Forms", "System.Drawing", "System.Drawing.Design"}


def q(tag):
    return f"{{{NS}}}{tag}"


def text_of(node, tag, default=""):
    el = node.find(q(tag))
    if el is None or el.text is None:
        return default
    return el.text.strip()


def convert(old_path, dest_path):
    tree = ET.parse(old_path)
    root = tree.getroot()
    pg = root.find(q("PropertyGroup"))
    root_ns = text_of(pg, "RootNamespace") or text_of(pg, "AssemblyName") or "App"
    asm_name = text_of(pg, "AssemblyName") or root_ns
    out_type = text_of(pg, "OutputType") or "Exe"
    # WinExe means windows-subsystem GUI; treat as needing WinForms.
    uses_winforms = False
    uses_wpf = False
    refs = []        # list of (Include, HintPath or None)
    proj_refs = []   # list of (Include, Name)
    package_refs = []  # list of (id, version)
    embedded = []    # list of (path, dep_upon)
    content = []     # list of (path, copy_to_output)
    resources = []
    pages = []

    for ig in root.findall(q("ItemGroup")):
        for ref in ig.findall(q("Reference")):
            inc = ref.attrib.get("Include", "")
            # Take only the simple name (strip version/culture/key)
            simple = inc.split(",")[0].strip()
            hint = ref.find(q("HintPath"))
            hint_text = hint.text.strip() if hint is not None and hint.text else None
            if simple in WINFORMS_REFS:
                uses_winforms = True
                continue
            if simple in {"PresentationCore", "PresentationFramework", "WindowsBase"}:
                uses_wpf = True
                continue
            if simple in FRAMEWORK_REFS:
                continue
            # Net-7-era MySql.Data ABI is unbuildable on .NET 10. Map to NuGet MySqlConnector.
            if simple == "MySql.Data":
                # Use Oracle's official NuGet (drop-in API). Pin a current 9.x.
                package_refs.append(("MySql.Data", "9.4.0"))
                continue
            if simple == "IniFiles":
                # Vendored DLL ships as Content/lib; convert to a HintPath ref.
                refs.append((simple, "lib\\IniFiles.dll"))
                continue
            if simple in VENDORED_DLLS:
                refs.append((simple, _vendored_hintpath(dest_path, VENDORED_DLLS[simple])))
                continue
            # Keep everything else (vendored or GAC-resolved third-party)
            refs.append((simple, hint_text))
        for pr in ig.findall(q("ProjectReference")):
            inc = normalize_projref(pr.attrib.get("Include", ""))
            name_el = pr.find(q("Name"))
            name = name_el.text.strip() if name_el is not None and name_el.text else None
            proj_refs.append((inc, name))
        for er in ig.findall(q("EmbeddedResource")):
            path = er.attrib.get("Include", "")
            dep = er.find(q("DependentUpon"))
            dep_text = dep.text.strip() if dep is not None and dep.text else None
            embedded.append((path, dep_text))
        for c in ig.findall(q("Content")):
            path = c.attrib.get("Include", "")
            cp = c.find(q("CopyToOutputDirectory"))
            cp_text = cp.text.strip() if cp is not None and cp.text else None
            content.append((path, cp_text))

    # OutputType normalisation
    if out_type.lower() == "winexe":
        out_type_final = "WinExe"
        uses_winforms = True  # WinExe in this codebase always means WinForms.
    elif out_type.lower() == "library":
        out_type_final = "Library"
    else:
        out_type_final = "Exe"

    if uses_winforms:
        tfm = "net10.0-windows"
    elif uses_wpf:
        tfm = "net10.0-windows"
    else:
        tfm = "net10.0"

    lines = []
    lines.append('<Project Sdk="Microsoft.NET.Sdk">')
    lines.append("  <PropertyGroup>")
    lines.append(f"    <OutputType>{out_type_final}</OutputType>")
    lines.append(f"    <TargetFramework>{tfm}</TargetFramework>")
    lines.append(f"    <RootNamespace>{root_ns}</RootNamespace>")
    lines.append(f"    <AssemblyName>{asm_name}</AssemblyName>")
    if uses_winforms:
        lines.append("    <UseWindowsForms>true</UseWindowsForms>")
    if uses_wpf:
        lines.append("    <UseWPF>true</UseWPF>")
    lines.append("    <Nullable>disable</Nullable>")
    lines.append("    <LangVersion>latest</LangVersion>")
    lines.append("    <ImplicitUsings>disable</ImplicitUsings>")
    lines.append("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>")
    lines.append("    <NoWarn>CS0108;CS0114;CS0414;CS0219;CS0162;CS0168;CS0169;CS0612;CS0618;CS0628;CS1591;CS8981;CS0436;NU1701;MSB3277</NoWarn>")
    lines.append("  </PropertyGroup>")
    if refs or package_refs:
        lines.append("  <ItemGroup>")
        for simple, hint in refs:
            if hint:
                lines.append(f'    <Reference Include="{simple}">')
                lines.append(f"      <HintPath>{hint}</HintPath>")
                lines.append("    </Reference>")
            else:
                lines.append(f'    <Reference Include="{simple}" />')
        for pid, pver in package_refs:
            lines.append(f'    <PackageReference Include="{pid}" Version="{pver}" />')
        lines.append("  </ItemGroup>")
    if proj_refs:
        lines.append("  <ItemGroup>")
        for inc, _name in proj_refs:
            lines.append(f'    <ProjectReference Include="{inc}" />')
        lines.append("  </ItemGroup>")
    if embedded:
        lines.append("  <ItemGroup>")
        for path, dep in embedded:
            # SDK globs .resx automatically and pairs with .cs via convention.
            # Only emit explicit entries when DependentUpon names something unusual.
            if path.lower().endswith(".resx") and dep:
                lines.append(f'    <EmbeddedResource Update="{path}">')
                lines.append(f"      <DependentUpon>{dep}</DependentUpon>")
                lines.append("    </EmbeddedResource>")
            elif not path.lower().endswith(".resx"):
                lines.append(f'    <EmbeddedResource Include="{path}" />')
        lines.append("  </ItemGroup>")
    if content:
        lines.append("  <ItemGroup>")
        for path, cp in content:
            if cp:
                lines.append(f'    <Content Include="{path}">')
                lines.append(f"      <CopyToOutputDirectory>{cp}</CopyToOutputDirectory>")
                lines.append("    </Content>")
            else:
                lines.append(f'    <Content Include="{path}" />')
        lines.append("  </ItemGroup>")
    lines.append("</Project>")
    return "\n".join(lines) + "\n"


def main():
    if len(sys.argv) < 2:
        print("Usage: convert_csproj.py <csproj> [<csproj> ...]")
        sys.exit(2)
    for path in sys.argv[1:]:
        backup = path + ".old"
        if os.path.exists(path) and not os.path.exists(backup):
            os.rename(path, backup)
        if not os.path.exists(backup):
            print(f"skip (no source): {path}")
            continue
        src = backup
        try:
            new_content = convert(src, path)
        except Exception as e:
            print(f"FAIL {path}: {e}")
            if not os.path.exists(path):
                os.rename(backup, path)
            continue
        with open(path, "w", encoding="utf-8") as f:
            f.write(new_content)
        print(f"ok   {path}")


if __name__ == "__main__":
    main()
