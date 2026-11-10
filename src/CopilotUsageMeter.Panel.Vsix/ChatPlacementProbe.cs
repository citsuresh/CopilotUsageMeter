using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CopilotUsageMeter.Panel.Vsix
{
    /// <summary>
    /// Phase 3 experiment (Exp instance only). Read-only: walks the live WPF visual tree to find the
    /// element named PART_WorkingSetExpander ("Total changes") and records type names, element names,
    /// layout kinds and sizes. It never reads or records text, titles, paths or message content.
    /// </summary>
    internal static class ChatPlacementProbe
    {
        public const string TargetName = "PART_WorkingSetExpander";

        public static string ReportPath => Path.Combine(Path.GetTempPath(), "CopilotUsageMeter-placement-probe.txt");

        public static string Run()
        {
            var report = new StringBuilder();
            report.AppendLine("Chat placement probe (structure only)");
            var windows = new List<Window>();
            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows) windows.Add(window);
            }
            report.AppendLine("top-level WPF windows: " + windows.Count);

            DependencyObject? target = null;
            Window? owner = null;
            foreach (var window in windows)
            {
                target = FindByName(window);
                if (target != null) { owner = window; break; }
            }

            if (target == null)
            {
                report.AppendLine("expander not found; falling back to the ChatWorkingSet element by type name");
                foreach (var window in windows)
                {
                    target = FindByTypeName(window, "ChatWorkingSet");
                    if (target != null) { owner = window; break; }
                }
            }

            if (target == null || owner == null)
            {
                report.AppendLine("target not found: no " + TargetName + " and no ChatWorkingSet in any WPF window");
            }
            else
            {
                DescribeTarget(report, target);
                ToggleMarker(report, owner, (FrameworkElement)target);
            }

            File.WriteAllText(ReportPath, report.ToString());
            return report.ToString();
        }

        private const string PaneGridName = "mainContentGrid";
        private const string TopMarkerName = "CopilotUsageMeterTopMarker";
        private static Grid? _paneGrid;
        private static RowDefinition? _addedRow;
        private static Dictionary<UIElement, int>? _originalRows;

        public static string ReportPathTop => Path.Combine(Path.GetTempPath(), "CopilotUsageMeter-toppane-probe.txt");

        // Fallback experiment: adds an Auto row at the top of the chat pane's main grid, shifts the
        // existing children down one row and puts a marker in it. A second run restores the grid.
        public static string RunTopOfPane()
        {
            var report = new StringBuilder();
            report.AppendLine("Top-of-pane probe (structure only)");

            if (_paneGrid != null && _addedRow != null)
            {
                if (_paneGrid.IsLoaded)
                {
                    Restore(report);
                    File.WriteAllText(ReportPathTop, report.ToString());
                    return report.ToString();
                }

                // Copilot recreated its view; the old grid is detached, so forget it and probe the live one.
                _paneGrid = null;
                _addedRow = null;
                _originalRows = null;
                report.AppendLine("previous pane grid is no longer loaded; state cleared");
            }

            Window? owner = null;
            Grid? grid = null;
            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    grid = FindGrid(window);
                    if (grid != null) { owner = window; break; }
                }
            }

            if (grid == null || owner == null)
            {
                report.AppendLine("pane grid not found: no Grid named " + PaneGridName);
                File.WriteAllText(ReportPathTop, report.ToString());
                return report.ToString();
            }

            report.AppendLine("pane grid: " + Describe(grid) + ", rows: " + grid.RowDefinitions.Count + ", columns: " + grid.ColumnDefinitions.Count);
            for (var i = 0; i < grid.RowDefinitions.Count; i++)
            {
                report.AppendLine("  row " + i + ": height=" + grid.RowDefinitions[i].Height);
            }

            for (var i = 0; i < grid.Children.Count; i++)
            {
                var item = grid.Children[i];
                report.AppendLine("  child " + i + ": " + Describe(item) + " row=" + Grid.GetRow(item) + " rowSpan=" + Grid.GetRowSpan(item));
            }

            var firstTop = double.MaxValue;
            foreach (UIElement item in grid.Children)
            {
                if (item is FrameworkElement fe && fe.IsLoaded && fe.ActualHeight > 0)
                {
                    firstTop = Math.Min(firstTop, fe.TransformToAncestor(owner).Transform(new Point(0, 0)).Y);
                }
            }

            report.AppendLine("topmost visible child y before: " + (firstTop == double.MaxValue ? -1 : (int)firstTop));
            var paneTop = grid.TransformToAncestor(owner).Transform(new Point(0, 0)).Y;
            report.AppendLine("pane grid y: " + (int)paneTop + " height: " + (int)grid.ActualHeight);

            _addedRow = new RowDefinition { Height = GridLength.Auto };
            _originalRows = new Dictionary<UIElement, int>();
            foreach (UIElement item in grid.Children) _originalRows[item] = Grid.GetRow(item);
            grid.RowDefinitions.Insert(0, _addedRow);
            foreach (UIElement item in grid.Children) Grid.SetRow(item, Grid.GetRow(item) + 1);

            var marker = new Border
            {
                Name = TopMarkerName,
                Height = 24,
                Background = Brushes.DarkOrange,
                Child = new TextBlock { Text = "Usage probe top (temporary)", Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2) },
            };
            Grid.SetRow(marker, 0);
            grid.Children.Add(marker);
            _paneGrid = grid;
            grid.UpdateLayout();

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var later = new StringBuilder();
                try
                {
                later.AppendLine("after 3 seconds: marker loaded=" + marker.IsLoaded + " visible=" + marker.IsVisible);
                if (marker.IsLoaded)
                {
                    var markerY = marker.TransformToAncestor(owner).Transform(new Point(0, 0)).Y;
                    later.AppendLine("  marker y=" + (int)markerY + " h=" + (int)marker.ActualHeight + " w=" + (int)marker.ActualWidth);
                    var after = double.MaxValue;
                    foreach (UIElement item in grid.Children)
                    {
                        if (!ReferenceEquals(item, marker) && item is FrameworkElement fe && fe.IsLoaded && fe.ActualHeight > 0)
                        {
                            after = Math.Min(after, fe.TransformToAncestor(owner).Transform(new Point(0, 0)).Y);
                        }
                    }

                    later.AppendLine("  topmost other child y after: " + (after == double.MaxValue ? -1 : (int)after));
                    later.AppendLine("  pane grid height now: " + (int)grid.ActualHeight);
                    later.AppendLine("  marker overlaps content: " + (after != double.MaxValue && markerY + marker.ActualHeight > after + 1));
                }
                }
                catch (Exception exception) { later.AppendLine("late check failed: " + exception.GetType().Name); }

                try { File.AppendAllText(ReportPathTop, later.ToString()); }
                catch (IOException) { }
            };
            timer.Start();

            File.WriteAllText(ReportPathTop, report.ToString());
            return report.ToString();
        }

        private static void Restore(StringBuilder report)
        {
            var grid = _paneGrid!;
            UIElement? marker = null;
            foreach (UIElement item in grid.Children)
            {
                if (item is FrameworkElement fe && fe.Name == TopMarkerName) marker = item;
            }

            if (marker != null) grid.Children.Remove(marker);
            foreach (UIElement item in grid.Children)
            {
                // Children recorded at insertion get their exact original row; later additions were never shifted.
                if (_originalRows != null && _originalRows.TryGetValue(item, out var original)) Grid.SetRow(item, original);
            }

            grid.RowDefinitions.Remove(_addedRow!);
            _paneGrid = null;
            _addedRow = null;
            _originalRows = null;
            report.AppendLine("restored: marker removed=" + (marker != null) + ", rows now: " + grid.RowDefinitions.Count);
        }

        private static Grid? FindGrid(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Grid grid && grid.Name == PaneGridName) return grid;
                var found = FindGrid(child);
                if (found != null) return found;
            }

            return null;
        }

        private static DependencyObject? FindByTypeName(DependencyObject root, string typeName)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child.GetType().Name == typeName) return child;
                var found = FindByTypeName(child, typeName);
                if (found != null) return found;
            }

            return null;
        }

        private static DependencyObject? FindByName(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement element &&
                    (element.Name == TargetName || System.Windows.Automation.AutomationProperties.GetAutomationId(element) == TargetName))
                {
                    return child;
                }

                var found = FindByName(child);
                if (found != null) return found;
            }

            return null;
        }

        private const string MarkerName = "CopilotUsageMeterProbeMarker";

        // Inserts a temporary marker above the child of the nearest Panel that contains the target,
        // measures its position, and removes it on the next invocation. Exp instance only.
        private static void ToggleMarker(StringBuilder report, Window owner, FrameworkElement target)
        {
            DependencyObject child = target;
            for (var up = VisualTreeHelper.GetParent(target); up != null; up = VisualTreeHelper.GetParent(up))
            {
                if (up.GetType().Name == "ChatWorkingSet") { child = up; break; }
            }

            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is System.Windows.Controls.Panel))
            {
                child = parent;
                parent = VisualTreeHelper.GetParent(parent);
            }

            var panel = parent as System.Windows.Controls.Panel;
            if (panel == null)
            {
                report.AppendLine("no Panel ancestor; cannot insert");
                return;
            }

            report.AppendLine("anchor panel: " + panel.GetType().FullName + ", children: " + panel.Children.Count);
            for (var i = 0; i < panel.Children.Count; i++)
            {
                var marker = ReferenceEquals(panel.Children[i], child) ? " <== contains target" : string.Empty;
                report.AppendLine("  child " + i + ": " + Describe(panel.Children[i]) + marker);
            }

            foreach (UIElement existing in panel.Children)
            {
                if (existing is FrameworkElement fe && fe.Name == MarkerName)
                {
                    panel.Children.Remove(existing);
                    report.AppendLine("marker removed; children now: " + panel.Children.Count);
                    return;
                }
            }

            var border = new Border
            {
                Name = MarkerName,
                Height = 24,
                Background = Brushes.DarkOrange,
                Child = new TextBlock { Text = "Usage probe (temporary)", Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2) },
            };
            var index = panel.Children.IndexOf((UIElement)child);
            panel.Children.Insert(index, border);
            panel.UpdateLayout();
            Report(report, "immediately", owner, border, target, panel);

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var later = new StringBuilder();
                try { Report(later, "after 3 seconds", owner, border, target, panel); }
                catch (Exception exception) { later.AppendLine("late check failed: " + exception.GetType().Name); }

                try { File.AppendAllText(ReportPath, later.ToString()); }
                catch (IOException) { }
            };
            timer.Start();
        }

        private static void Report(StringBuilder report, string when, Window owner, FrameworkElement marker, FrameworkElement target, System.Windows.Controls.Panel panel)
        {
            report.AppendLine("marker check (" + when + "): inPanel=" + panel.Children.Contains(marker) + ", loaded=" + marker.IsLoaded + ", visible=" + marker.IsVisible);
            if (!marker.IsLoaded || !target.IsLoaded) return;
            if (target.ActualHeight < 1 || !target.IsVisible)
            {
                report.AppendLine("  working set slot is collapsed/empty (size " + (int)target.ActualWidth + "x" + (int)target.ActualHeight + ", vis=" + target.Visibility + "); measuring against PromptBorder");
                foreach (UIElement sibling in panel.Children)
                {
                    if (sibling is FrameworkElement fe && fe.Name == "PromptBorder") target = fe;
                }
            }

            var markerPoint = marker.TransformToAncestor(owner).Transform(new Point(0, 0));
            var targetPoint = target.TransformToAncestor(owner).Transform(new Point(0, 0));
            report.AppendLine("  marker y=" + (int)markerPoint.Y + " h=" + (int)marker.ActualHeight + " x=" + (int)markerPoint.X + " w=" + (int)marker.ActualWidth);
            report.AppendLine("  target y=" + (int)targetPoint.Y + " x=" + (int)targetPoint.X + " w=" + (int)target.ActualWidth);
            report.AppendLine("  directly above target: " + (markerPoint.Y + marker.ActualHeight <= targetPoint.Y + 1));
        }

        private static void DescribeTarget(StringBuilder report, DependencyObject target)
        {
            var element = (FrameworkElement)target;
            report.AppendLine("target: " + Describe(element));
            report.AppendLine("target assembly: " + element.GetType().Assembly.GetName().Name);

            report.AppendLine("ancestors (nearest first, up to 14):");
            var current = VisualTreeHelper.GetParent(target);
            for (var depth = 0; current != null && depth < 14; depth++)
            {
                report.AppendLine("  " + depth + ": " + Describe(current));
                current = VisualTreeHelper.GetParent(current);
            }

            var parent = VisualTreeHelper.GetParent(target) as System.Windows.Controls.Panel;
            if (parent == null)
            {
                report.AppendLine("immediate parent is not a Panel; insertion would need template or wrapper changes");
                return;
            }

            report.AppendLine("immediate parent panel kind: " + parent.GetType().FullName + ", children: " + parent.Children.Count);
            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                var marker = ReferenceEquals(child, target) ? " <== target" : string.Empty;
                report.AppendLine("  child " + i + ": " + Describe(child) + marker);
            }

            if (parent is Grid grid)
            {
                report.AppendLine("grid rows: " + grid.RowDefinitions.Count + ", columns: " + grid.ColumnDefinitions.Count);
                report.AppendLine("target Grid.Row: " + Grid.GetRow(element) + ", RowSpan: " + Grid.GetRowSpan(element));
            }
        }

        private static string Describe(DependencyObject item)
        {
            var sb = new StringBuilder(item.GetType().FullName);
            if (item is FrameworkElement fe)
            {
                if (!string.IsNullOrEmpty(fe.Name)) sb.Append(" name=").Append(fe.Name);
                var automationId = System.Windows.Automation.AutomationProperties.GetAutomationId(fe);
                if (!string.IsNullOrEmpty(automationId)) sb.Append(" id=").Append(automationId);
                sb.Append(" size=").Append((int)fe.ActualWidth).Append('x').Append((int)fe.ActualHeight);
                sb.Append(" vis=").Append(fe.Visibility);
                if (fe.Parent is Grid || VisualTreeHelper.GetParent(fe) is Grid)
                {
                    sb.Append(" gridRow=").Append(Grid.GetRow(fe));
                }
            }

            return sb.ToString();
        }
    }
}
