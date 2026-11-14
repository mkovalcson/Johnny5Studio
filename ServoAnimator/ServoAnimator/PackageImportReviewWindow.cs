using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ServoAnimator;

internal sealed class PackageImportReviewWindow : Window
{
    internal PackageImportReviewWindow(ProjectPackageImport plan)
    {
        Title = "Review " + (plan.Kind == ProjectPackageKind.UrdfModel ? "URDF Model" : plan.Kind.ToString()) + " Import";
        Width = 1120; Height = 650; MinWidth = 850; MinHeight = 400; FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        HelpSystem.EnableContextHelp(this, "saving");
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = $"Import {plan.Files.Count:N0} files into Configuration", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        heading.Children.Add(new TextBox { Text = plan.ConfigRoot, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 10) });
        int overwrites = plan.Overwrites.Count();
        var warning = new TextBlock { Text = overwrites == 0 ? "No existing files will be overwritten." : $"{overwrites:N0} existing files will be overwritten. Compare their dates and sizes before proceeding.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        warning.SetResourceReference(TextBlock.ForegroundProperty, overwrites == 0 ? "PrimaryText" : "WarningText"); heading.Children.Add(warning);
        heading.Children.Add(new TextBlock { Text = $"{plan.Files.Count - overwrites:N0} new files. Modification dates are shown in local time; sizes are exact bytes. Files to overwrite appear first.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var proceed = new Button { Content = "Proceed", IsDefault = true, MinWidth = 100, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        proceed.SetResourceReference(Control.BackgroundProperty, "SequenceAccentSurface");
        proceed.Click += (_, _) => DialogResult = true; buttons.Children.Add(proceed);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 100, Padding = new Thickness(14, 7, 14, 7) });
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, EnableRowVirtualization = true,
            ItemsSource = plan.Files.OrderByDescending(f => f.Exists).ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToArray(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grid.SetResourceReference(Control.BackgroundProperty, "InputBackground"); grid.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "InputBackground"); grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "PanelBackground");
        var columnStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        columnStyle.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("ControlBackground")));
        columnStyle.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("PrimaryText")));
        columnStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
        grid.ColumnHeaderStyle = columnStyle;
        var fileStyle = new Style(typeof(TextBlock));
        fileStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding(nameof(PackageImportFile.FileName))));
        void Column(string header, string property, double width, bool stretch = false) => grid.Columns.Add(new DataGridTextColumn
        { Header = header, Binding = new Binding(property), MinWidth = stretch ? 340 : 70, ElementStyle = stretch ? fileStyle : null,
            Width = new DataGridLength(width, stretch ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel) });
        Column("File in Config", nameof(PackageImportFile.FileName), 1, true);
        Column("Existing modified", nameof(PackageImportFile.ExistingModified), 212);
        Column("Existing bytes", nameof(PackageImportFile.ExistingSize), 115);
        Column("Incoming modified", nameof(PackageImportFile.IncomingModified), 212);
        Column("Incoming bytes", nameof(PackageImportFile.IncomingSize), 115);
        root.Children.Add(grid);
    }
}
