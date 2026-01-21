using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Editor.Editor
{
    public static class ImGuiAutoInspector
    {
        public static bool DrawObject(object target, float dragSpeed = 0.05f)
        {
            bool changed = false;
            var type = target.GetType();

            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (p.GetIndexParameters().Length != 0) continue;

                changed |= DrawMember(
                    name: p.Name,
                    valueType: p.PropertyType,
                    getter: () => p.GetValue(target),
                    setter: v => p.SetValue(target, v),
                    dragSpeed: dragSpeed
                );
            }

            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                changed |= DrawMember(
                    name: f.Name,
                    valueType: f.FieldType,
                    getter: () => f.GetValue(target),
                    setter: v => f.SetValue(target, v),
                    dragSpeed: dragSpeed
                );
            }

            return changed;
        }

        private static bool DrawMember(
            string name,
            Type valueType,
            Func<object?> getter,
            Action<object?> setter,
            float dragSpeed)
        {
            // float
            if (valueType == typeof(float))
            {
                float v = (float)(getter() ?? 0f);
                if (ImGui.DragFloat(name, ref v, dragSpeed))
                {
                    setter(v);
                    return true;
                }
                return false;
            }

            // int
            if (valueType == typeof(int))
            {
                int v = (int)(getter() ?? 0);
                if (ImGui.DragInt(name, ref v, 1))
                {
                    setter(v);
                    return true;
                }
                return false;
            }

            // bool
            if (valueType == typeof(bool))
            {
                bool v = (bool)(getter() ?? false);
                if (ImGui.Checkbox(name, ref v))
                {
                    setter(v);
                    return true;
                }
                return false;
            }

            // Vector3
            if (valueType == typeof(Vector3))
            {
                Vector3 v = (Vector3)(getter() ?? Vector3.Zero);
                if (ImGui.DragFloat3(name, ref v, dragSpeed))
                {
                    setter(v);
                    return true;
                }
                return false;
            }

            // string
            if (valueType == typeof(string))
            {
                string s = (string?)getter() ?? "";
                if (ImGui.InputText(name, ref s, 256))
                {
                    setter(s);
                    return true;
                }
                return false;
            }

            ImGui.TextDisabled($"{name}: ({valueType.Name}) not supported");
            return false;
        }
    }
}
