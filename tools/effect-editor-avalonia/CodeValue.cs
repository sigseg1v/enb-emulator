using System;

namespace EffectEditorAvalonia
{
    // Port of tools/effect-editor/SQLBind/CodeValue.cs verbatim — used in
    // the variable-type combo boxes (e.g. "Increase Percent (2)") so the
    // displayed text encodes the underlying integer code.
    public class CodeValue
    {
        public int code;
        public string value;

        public CodeValue() { code = 0; value = ""; }
        public CodeValue(int code) { this.code = code; this.value = ""; }
        public CodeValue(int code, string value) { this.code = code; this.value = value; }

        public static CodeValue Formatted(int code, string value)
            => new CodeValue(code, value + " (" + code + ")");

        public override string ToString() => value;

        public override bool Equals(object obj)
            => obj is CodeValue cv && code.Equals(cv.code);

        public override int GetHashCode() => code.GetHashCode();
    }
}
