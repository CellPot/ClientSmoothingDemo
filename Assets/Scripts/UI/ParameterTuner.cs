using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Techniques;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Draws live sliders for a technique's tunable settings. The settings are found by reflection: every public
    /// float with a [Range] and every public bool declared on the technique class (base-class fields are skipped),
    /// so a new technique gets its UI for free. Remembers the values from before the first edit for "Reset".
    /// </summary>
    public class ParameterTuner
    {
        class Entry
        {
            public FieldInfo field;
            public RangeAttribute range;
            public object initial;
            public string label;
        }

        readonly Dictionary<BaseClientEntity, List<Entry>> _entries = new Dictionary<BaseClientEntity, List<Entry>>();

        List<Entry> EntriesOf(BaseClientEntity c)
        {
            if (_entries.TryGetValue(c, out var list)) return list;

            list = new List<Entry>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            // Walk from the concrete type up to (excluding) BaseClientEntity, base-most class first.
            var chain = new List<System.Type>();
            for (var t = c.GetType(); t != null && t != typeof(BaseClientEntity); t = t.BaseType) chain.Insert(0, t);

            foreach (var t in chain)
            foreach (var f in t.GetFields(flags))
            {
                if (f.IsDefined(typeof(HideInInspector), true)) continue;
                var range = f.GetCustomAttribute<RangeAttribute>();
                if (f.FieldType == typeof(float) && range != null || f.FieldType == typeof(bool))
                    list.Add(new Entry { field = f, range = range, initial = f.GetValue(c), label = Nicify(f.Name) });
            }

            _entries[c] = list;
            return list;
        }

        public void Draw(BaseClientEntity c)
        {
            var list = EntriesOf(c);
            if (list.Count == 0)
            {
                GUILayout.Label("No tunable parameters.");
                return;
            }

            foreach (var e in list)
            {
                if (e.field.FieldType == typeof(bool))
                {
                    bool v = (bool)e.field.GetValue(c);
                    bool nv = GUILayout.Toggle(v, " " + e.label);
                    if (nv != v) e.field.SetValue(c, nv);
                }
                else
                {
                    float v = (float)e.field.GetValue(c);
                    GUILayout.Label($"{e.label}: {v:0.###}");
                    float nv = GUILayout.HorizontalSlider(v, e.range.min, e.range.max);
                    if (!Mathf.Approximately(nv, v)) e.field.SetValue(c, nv);
                }
            }
        }

        public void Reset(BaseClientEntity c)
        {
            foreach (var e in EntriesOf(c)) e.field.SetValue(c, e.initial);
        }

        static string Nicify(string name)
        {
            string s = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
