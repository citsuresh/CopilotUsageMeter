using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace CopilotUsageMeter.Panel.Vsix
{
    /// <summary>
    /// Phase 3 experiment (Exp instance only; not for release). Observational: it does not modify anything, but
    /// it invokes property getters by reflection (one level deep) on the UI thread, so a getter with side effects
    /// or blocking work would run. It looks at the DataContext of the chat session views for GUID-shaped identifiers. Records only type names, member names and GUID values.
    /// Never records any other string value, so no titles, text or paths are written.
    /// </summary>
    internal static class ActiveChatProbe
    {
        private static readonly Regex GuidPattern = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
        private static readonly string[] ViewTypes = { "HarnessChatSessionView", "HarnessSessionView", "HarnessChatView" };

        public static string ReportPath => Path.Combine(Path.GetTempPath(), "CopilotUsageMeter-activechat-probe.txt");

        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("Active chat probe (type/member names and GUID values only)");
            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    foreach (var typeName in ViewTypes)
                    {
                        var views = new List<FrameworkElement>();
                        Collect(window, typeName, views);
                        report.AppendLine(typeName + " instances: " + views.Count);
                        for (var i = 0; i < views.Count; i++)
                        {
                            Inspect(report, typeName + "[" + i + "] visible=" + views[i].IsVisible, views[i]);
                        }
                    }
                }
            }

            File.WriteAllText(ReportPath, report.ToString());
        }

        private static void Collect(DependencyObject root, string typeName, List<FrameworkElement> found)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement element && child.GetType().Name == typeName) found.Add(element);
                Collect(child, typeName, found);
            }
        }

        private static void Inspect(StringBuilder report, string label, FrameworkElement element)
        {
            var context = element.DataContext;
            report.AppendLine(label + " DataContext: " + (context == null ? "null" : context.GetType().FullName));
            if (context == null) return;
            Dump(report, "  ", context, 0, new HashSet<object>());
        }

        private static void Dump(StringBuilder report, string indent, object value, int depth, HashSet<object> seen)
        {
            if (!seen.Add(value)) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var member in value.GetType().GetMembers(flags))
            {
                object? memberValue;
                Type memberType;
                try
                {
                    if (member is PropertyInfo property && property.GetIndexParameters().Length == 0 && property.CanRead)
                    {
                        memberType = property.PropertyType;
                        memberValue = property.GetValue(value);
                    }
                    else if (member is FieldInfo field && !field.Name.Contains("k__BackingField"))
                    {
                        memberType = field.FieldType;
                        memberValue = field.GetValue(value);
                    }
                    else
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                if (memberValue == null) continue;
                if (member.Name == "Telemetry" || memberType.Name.Contains("Telemetry")) continue;
                if (memberValue.GetType().Name == "CopilotSessionId")
                {
                    var idText = memberValue.ToString() ?? string.Empty;
                    report.AppendLine(indent + member.Name + " : CopilotSessionId = " + (GuidPattern.IsMatch(idText) ? "GUID " + idText : "non-GUID text, length " + idText.Length));
                }
                else if (memberType == typeof(string))
                {
                    report.AppendLine(indent + member.Name + " : string (value not recorded)");
                }
                else if (memberType.IsPrimitive || memberType.IsEnum || memberType == typeof(Guid) || memberType == typeof(DateTime))
                {
                    report.AppendLine(indent + member.Name + " : " + memberType.Name);
                }
                else if (depth < 1 && !(memberValue is System.Collections.IEnumerable) && !(memberValue is Delegate) && !(memberValue is DependencyObject))
                {
                    report.AppendLine(indent + member.Name + " : " + memberType.FullName);
                    Dump(report, indent + "    ", memberValue, depth + 1, seen);
                }
                else
                {
                    report.AppendLine(indent + member.Name + " : " + memberType.FullName);
                }
            }
        }
    }
}
