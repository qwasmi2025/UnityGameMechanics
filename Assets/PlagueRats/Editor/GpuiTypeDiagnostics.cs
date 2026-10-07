#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PlagueRats.EditorTools
{
    /// <summary>
    /// Diagnostic helper. Run via menu: Tools > Plague Rats > Diagnose GPUI Types.
    /// Prints every GPU Instancer Pro type, its namespace, and the assembly it lives in,
    /// so you can copy the EXACT `using` directives and class names into RatDirector.cs.
    ///
    /// Use it to confirm:
    ///   - the namespace that contains GPUICoreAPI            (the core 'using')
    ///   - the namespace that contains GPUIAWComputeAnimator  (the crowd 'using')
    ///   - the assembly names to reference in your asmdef
    /// </summary>
    public static class GpuiTypeDiagnostics
    {
        [MenuItem("Tools/Plague Rats/Diagnose GPUI Types")]
        public static void Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== GPU Instancer Pro type diagnostics ===\n");

            // Names we care about for RatDirector
            string[] targets =
            {
                "GPUICoreAPI", "GPUICrowdAPI", "GPUIAWComputeAnimator",
                "GPUIProfile", "GPUICrowdInstance", "GPUIPrefabManager"
            };

            var hits = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(t => t != null && targets.Contains(t.Name))
                .OrderBy(t => t.Name);

            bool any = false;
            foreach (var t in hits)
            {
                any = true;
                sb.AppendLine($"FOUND  {t.Name}");
                sb.AppendLine($"   namespace : {t.Namespace ?? "<global>"}");
                sb.AppendLine($"   assembly  : {t.Assembly.GetName().Name}");
                sb.AppendLine($"   using     : using {t.Namespace};");
                sb.AppendLine();
            }

            if (!any)
            {
                sb.AppendLine("!! No GPUI Pro types found in loaded assemblies.");
                sb.AppendLine("   => GPU Instancer Pro (and/or Crowd Animations) is NOT imported,");
                sb.AppendLine("      or its scripts failed to compile. Import it from the Asset Store first.");
            }
            else
            {
                // Dump all GPUI namespaces present, to spot the crowd one
                var namespaces = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes)
                    .Where(t => t != null && t.Namespace != null && t.Namespace.Contains("GPUInstancer"))
                    .Select(t => $"{t.Namespace}  (asm: {t.Assembly.GetName().Name})")
                    .Distinct()
                    .OrderBy(s => s);

                sb.AppendLine("--- All GPUInstancer* namespaces present ---");
                foreach (var ns in namespaces) sb.AppendLine("   " + ns);
            }

            Debug.Log(sb.ToString());
        }

        static Type[] SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types; }
            catch { return Array.Empty<Type>(); }
        }
    }
}
#endif
