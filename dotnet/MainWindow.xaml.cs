using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Search;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SynapMc
{
    public class ClientInfo
    {
        public TcpClient Client { get; set; }
        public string PlayerName { get; set; } = "Unknown";
        public string Version { get; set; } = "Unknown";
        public string Server { get; set; } = "Unknown";
        public DateTime ConnectedAt { get; set; } = DateTime.Now;

        public ClientInfo(TcpClient client)
        {
            Client = client;
        }

        public override string ToString()
        {
            return $"{PlayerName} ({Version}) - {Server}";
        }
    }

    public partial class MainWindow : Window
    {
        private string _scriptsRoot;
        private readonly string _globalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "global.json");
        private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private IHighlightingDefinition? _luaHighlighting;
        private List<string> _globalVariables = new List<string>();
        private ICSharpCode.AvalonEdit.Search.SearchPanel? _currentSearchPanel;
        private bool _remoteServerEnabled = false;
        private TcpListener? _tcpListener;
        private List<ClientInfo> _connectedClients = new List<ClientInfo>();
        private readonly object _clientsLock = new object();
        private ClientInfo? _selectedClient = null;
        private bool _executeToAllClients = false;
        private Dictionary<TabItem, List<ClientInfo>> _tabClientAttachments = new Dictionary<TabItem, List<ClientInfo>>();
        private readonly Dictionary<string, Color> _themeColors = new Dictionary<string, Color>
        {
            ["Background"] = Color.FromRgb(45, 45, 48),
            ["Border"] = Color.FromRgb(62, 62, 66),
            ["Accent"] = Color.FromRgb(255, 140, 0),
            ["EditorBackground"] = Color.FromRgb(0, 0, 0),
            ["ControlBackground"] = Color.FromRgb(60, 60, 60)
        };

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadLuaHighlighting();
            LoadGlobalVariables();
            if (!Directory.Exists(_scriptsRoot)) Directory.CreateDirectory(_scriptsRoot);
            LoadScriptTree();
            AddNewTab();
            
            // Add Ctrl+F shortcut for search
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // Initialize remote button border
            UpdateRemoteButtonBorder();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (settings != null)
                    {
                        if (settings.ContainsKey("WorkspacePath"))
                        {
                            _scriptsRoot = settings["WorkspacePath"];
                        }
                        if (settings.ContainsKey("AlwaysOnTop") && bool.TryParse(settings["AlwaysOnTop"], out bool alwaysOnTop))
                        {
                            this.Topmost = alwaysOnTop;
                        }
                        return;
                    }
                }
            }
            catch { }
            _scriptsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    ["WorkspacePath"] = _scriptsRoot,
                    ["AlwaysOnTop"] = this.Topmost.ToString()
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SearchBar.Visibility = Visibility.Visible;
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBar.Visibility == Visibility.Visible)
            {
                SearchBar.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void LoadLuaHighlighting()
        {
            try
            {
                using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("SynapMc.LuaHighlighting.xshd");
                if (s != null)
                {
                    using var reader = new XmlTextReader(s);
                    _luaHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch { }
        }

        private void LoadGlobalVariables()
        {
            _globalVariables.Clear();
            try
            {
                if (File.Exists(_globalPath))
                {
                    string json = File.ReadAllText(_globalPath);
                    _globalVariables = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch { }
        }

        private void LoadScriptTree()
        {
            ScriptTree.Items.Clear();
            PopulateTree(_scriptsRoot, ScriptTree);
        }

        private void PopulateTree(string currentDir, ItemsControl parent)
        {
            foreach (var dir in Directory.GetDirectories(currentDir).OrderBy(d => d))
            {
                TreeViewItem item = CreateTreeItem("📁 " + Path.GetFileName(dir), true, dir);
                parent.Items.Add(item);
                PopulateTree(dir, item);
            }
            foreach (var file in Directory.GetFiles(currentDir).Where(f => f.EndsWith(".lua") || f.EndsWith(".txt")).OrderBy(f => f))
            {
                parent.Items.Add(CreateTreeItem("📄 " + Path.GetFileName(file), false, file));
            }
        }

        private TreeViewItem CreateTreeItem(string header, bool isFolder, string fullPath)
        {
            TreeViewItem item = new TreeViewItem { Header = header, Tag = fullPath };
            if (!isFolder)
            {
                item.MouseDoubleClick += (s, e) => { if (IsInHeader(e.OriginalSource as DependencyObject, item)) AddNewTab(Path.GetFileName(fullPath), File.ReadAllText(fullPath)); e.Handled = true; };
            }

            ContextMenu menu = new ContextMenu { StaysOpen = false };
            if (isFolder)
            {
                MenuItem ns = new MenuItem { Header = "New Script" }; ns.Click += (s, e) => CreateNewFile(fullPath, false);
                MenuItem nf = new MenuItem { Header = "New Folder" }; nf.Click += (s, e) => CreateNewFile(fullPath, true);
                menu.Items.Add(ns); menu.Items.Add(nf); menu.Items.Add(new Separator());
            }
            else
            {
                MenuItem ex = new MenuItem { Header = "Execute" }; ex.Click += (s, e) => System.Windows.MessageBox.Show($"Executing {Path.GetFileName(fullPath)}...");
                menu.Items.Add(ex);
            }

            MenuItem ren = new MenuItem { Header = "Rename" }; ren.Click += (s, e) => Context_Rename_Click(fullPath);
            MenuItem del = new MenuItem { Header = "Delete" }; del.Click += (s, e) => Context_Delete_Click(fullPath);
            menu.Items.Add(ren); menu.Items.Add(del);
            item.ContextMenu = menu;
            return item;
        }

        private void TreeViewItemHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && FindVisualParent<TreeViewItem>(el) is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                if (tvi.HasItems) tvi.IsExpanded = !tvi.IsExpanded;
                e.Handled = true;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            return parentObject == null ? null : parentObject is T parent ? parent : FindVisualParent<T>(parentObject);
        }

        private bool IsInHeader(DependencyObject? obj, TreeViewItem item)
        {
            while (obj != null && obj != item) { if (obj is ItemsPresenter) return false; obj = VisualTreeHelper.GetParent(obj); }
            return true;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void Minimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void AddTab_Click(object sender, RoutedEventArgs e) => AddNewTab();
        private void AddNewTab(string? title = null, string content = "")
        {
            if (string.IsNullOrEmpty(title))
            {
                var used = new HashSet<int>();
                foreach (TabItem it in ScriptTabs.Items) if (it.Header is StackPanel sp && sp.Children[0] is TextBlock tb) { var m = Regex.Match(tb.Text, @"^Script (\d+)\.lua$"); if (m.Success) used.Add(int.Parse(m.Groups[1].Value)); }
                int n = 1; while (used.Contains(n)) n++;
                title = $"Script {n}.lua";
            }

            TabItem tab = new TabItem();
            
            // Create header with text
            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock tbHeader = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("TabHeaderTextStyle") };
            headerPanel.Children.Add(tbHeader);
            tab.Header = headerPanel;

            // Create context menu
            ContextMenu menu = new ContextMenu { StaysOpen = false };
            MenuItem save = new MenuItem { Header = "Save" }; 
            save.Click += (s, e) => { ScriptTabs.SelectedItem = tab; SaveFile_Click(s, e); };
            MenuItem saveAs = new MenuItem { Header = "Save As..." }; 
            saveAs.Click += (s, e) => { ScriptTabs.SelectedItem = tab; SaveFile_Click(s, e); };
            MenuItem attachClients = new MenuItem { Header = "Attach Clients..." };
            attachClients.Click += (s, e) => ShowAttachClientsForTab(tab);
            MenuItem close = new MenuItem { Header = "Close" }; 
            close.Click += (s, e) => RequestCloseTab(tab);
            MenuItem closeOthers = new MenuItem { Header = "Close Others" }; 
            closeOthers.Click += (s, e) => CloseOtherTabs(tab);
            menu.Items.Add(save); 
            menu.Items.Add(saveAs);
            menu.Items.Add(new Separator());
            menu.Items.Add(attachClients);
            menu.Items.Add(new Separator());
            menu.Items.Add(close); 
            menu.Items.Add(closeOthers);
            
            // Attach context menu to header panel
            headerPanel.ContextMenu = menu;
            headerPanel.MouseRightButtonUp += (s, e) => { menu.PlacementTarget = headerPanel; menu.IsOpen = true; e.Handled = true; };

            TextEditor ed = new TextEditor { FontFamily = new FontFamily("Consolas"), FontSize = 14, ShowLineNumbers = true, SyntaxHighlighting = _luaHighlighting, Background = new SolidColorBrush(_themeColors["EditorBackground"]), Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)), Text = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            ed.Options.EnableHyperlinks = false; ed.Options.EnableEmailHyperlinks = false;
            
            // Add global variable highlighter
            ed.TextArea.TextView.LineTransformers.Add(new GlobalVariableColorizer(_globalVariables));
            
            // Add right-click context menu for color picker and code snippets
            ContextMenu editorContextMenu = new ContextMenu { StaysOpen = false };
            MenuItem insertColorItem = new MenuItem { Header = "Insert Color..." };
            insertColorItem.Click += (s, ev) => ShowColorPickerDialog(ed);
            editorContextMenu.Items.Add(insertColorItem);
            
            editorContextMenu.Items.Add(new Separator());
            
            MenuItem insertForPairsItem = new MenuItem { Header = "Insert for-pairs loop" };
            insertForPairsItem.Click += (s, ev) => 
            {
                int offset = ed.CaretOffset;
                string snippet = "for i,v in pairs() do\n\t\nend";
                ed.Document.Insert(offset, snippet);
                // Move cursor inside the pairs()
                ed.CaretOffset = offset + 17; // Position inside pairs()
                ed.Focus();
            };
            editorContextMenu.Items.Add(insertForPairsItem);
            
            MenuItem insertPcallItem = new MenuItem { Header = "Insert pcall function" };
            insertPcallItem.Click += (s, ev) => 
            {
                int offset = ed.CaretOffset;
                string snippet = "local success,result = pcall(function()\n\t\nend)";
                ed.Document.Insert(offset, snippet);
                // Move cursor inside the function body
                ed.CaretOffset = offset + 42; // Position inside function body
                ed.Focus();
            };
            editorContextMenu.Items.Add(insertPcallItem);
            
            editorContextMenu.Items.Add(new Separator());
            
            MenuItem insertJavaWrapperItem = new MenuItem { Header = "Insert JavaWrapper async" };
            insertJavaWrapperItem.Click += (s, ev) => 
            {
                int offset = ed.CaretOffset;
                string snippet = "JavaWrapper:methodToJavaAsync(function()\n\t\nend)";
                ed.Document.Insert(offset, snippet);
                // Move cursor at methodToJavaAsync to replace it
                ed.Select(offset, 12); // Select "JavaWrapper"
                ed.Focus();
            };
            editorContextMenu.Items.Add(insertJavaWrapperItem);
            
            ed.ContextMenu = editorContextMenu;
            
            tab.Content = ed; ScriptTabs.Items.Add(tab); ScriptTabs.SelectedItem = tab;
        }

        private void RequestCloseTab(TabItem tab) 
        { 
            if (tab.Content is TextEditor ed && !string.IsNullOrEmpty(ed.Text)) 
                if (!ShowConfirmDialog("Close Tab", "Close script with content?")) 
                    return; 
            ScriptTabs.Items.Remove(tab); 
            _tabClientAttachments.Remove(tab); // Clean up attachments
            if (ScriptTabs.Items.Count == 0) 
                AddNewTab(); 
        }
        
        private void CloseOtherTabs(TabItem keepTab)
        {
            var toRemove = ScriptTabs.Items.Cast<TabItem>().Where(t => t != keepTab).ToList();
            foreach (var tab in toRemove)
            {
                // Skip tabs with attached clients
                if (_tabClientAttachments.ContainsKey(tab) && _tabClientAttachments[tab].Count > 0)
                {
                    continue;
                }
                
                if (tab.Content is TextEditor ed && !string.IsNullOrEmpty(ed.Text))
                {
                    if (!ShowConfirmDialog("Close Tab", $"Close script with unsaved content?"))
                    {
                        continue;
                    }
                }
                ScriptTabs.Items.Remove(tab);
                _tabClientAttachments.Remove(tab); // Clean up attachments
            }
        }
        
        private void ShowColorPickerDialog(TextEditor editor)
        {
            var colorDialog = new Xceed.Wpf.Toolkit.ColorCanvas();
            Window pickerWindow = new Window
            {
                Width = 450,
                Height = 480,
                Title = "Pick Color",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1)
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(20) };
            colorDialog.Height = 320;
            colorDialog.Width = 400;
            colorDialog.Margin = new Thickness(0, 0, 0, 15);
            colorDialog.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            panel.Children.Add(colorDialog);

            StackPanel btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            okBtn.Click += (s2, e2) =>
            {
                Color selectedColor = colorDialog.SelectedColor ?? Colors.White;
                string hexText = $"0x{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                int caretOffset = editor.CaretOffset;
                editor.Document.Insert(caretOffset, hexText);
                pickerWindow.DialogResult = true;
                pickerWindow.Close();
            };

            Button cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            cancelBtn.Click += (s2, e2) => pickerWindow.Close();

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            pickerWindow.Content = panel;
            pickerWindow.ShowDialog();
        }

        private TextEditor? GetCurrentEditor() => (ScriptTabs.SelectedItem as TabItem)?.Content as TextEditor;

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            OptionsWindow optWin = new OptionsWindow(_themeColors, _scriptsRoot, this.Topmost);
            if (optWin.ShowDialog() == true)
            {
                foreach (var kvp in optWin.UpdatedColors)
                {
                    _themeColors[kvp.Key] = kvp.Value;
                }
                if (!string.IsNullOrEmpty(optWin.UpdatedWorkspacePath) && optWin.UpdatedWorkspacePath != _scriptsRoot)
                {
                    _scriptsRoot = optWin.UpdatedWorkspacePath;
                    SaveSettings();
                    if (!Directory.Exists(_scriptsRoot)) Directory.CreateDirectory(_scriptsRoot);
                    LoadScriptTree();
                }
                if (this.Topmost != optWin.AlwaysOnTop)
                {
                    this.Topmost = optWin.AlwaysOnTop;
                    SaveSettings();
                }
                ApplyThemeColors();
            }
        }

        private void ApplyThemeColors()
        {
            this.Background = new SolidColorBrush(_themeColors["Background"]);
            this.BorderBrush = new SolidColorBrush(_themeColors["Border"]);
            foreach (TabItem tab in ScriptTabs.Items)
            {
                if (tab.Content is TextEditor ed)
                {
                    ed.Background = new SolidColorBrush(_themeColors["EditorBackground"]);
                }
            }
        }

        private async void Execute_Click(object sender, RoutedEventArgs e) 
        { 
            if (GetCurrentEditor() is TextEditor ed)
            {
                if (_remoteServerEnabled && _connectedClients.Count > 0)
                {
                    await SendCommandToClients(ed.Text);
                }
            }
        }
        private void Clear_Click(object sender, RoutedEventArgs e) 
        { 
            if (GetCurrentEditor() is TextEditor ed && !string.IsNullOrEmpty(ed.Text)) 
            {
                var dialog = new Window 
                { 
                    Width = 400, 
                    Height = 220, 
                    Title = "Clear Script", 
                    WindowStyle = WindowStyle.None, 
                    ResizeMode = ResizeMode.NoResize, 
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), 
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                    Owner = this, 
                    Foreground = Brushes.White, 
                    BorderThickness = new Thickness(1) 
                };
                
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
                dialog.BorderBrush = gradient;

                var stackPanel = new StackPanel { Margin = new Thickness(20) };
                
                var titleBlock = new TextBlock 
                { 
                    Text = "Clear Script", 
                    FontSize = 16, 
                    FontWeight = FontWeights.Bold, 
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), 
                    Margin = new Thickness(0, 0, 0, 15) 
                };
                stackPanel.Children.Add(titleBlock);
                
                var messageBlock = new TextBlock 
                { 
                    Text = "Are you sure you want to clear the editor?\nThis action cannot be undone.", 
                    Margin = new Thickness(0, 0, 0, 10), 
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.White
                };
                stackPanel.Children.Add(messageBlock);
                
                var statsBlock = new TextBlock 
                { 
                    Text = $"Lines: {ed.LineCount}  |  Characters: {ed.Text.Length}", 
                    Margin = new Thickness(0, 0, 0, 20), 
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
                };
                stackPanel.Children.Add(statsBlock);

                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                
                var clearButton = new Button 
                { 
                    Content = "Clear", 
                    Width = 90, 
                    Height = 32, 
                    Margin = new Thickness(0, 0, 10, 0), 
                    Background = new SolidColorBrush(Color.FromRgb(220, 50, 50)), 
                    Foreground = Brushes.White, 
                    BorderThickness = new Thickness(1), 
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                    FontWeight = FontWeights.Bold
                };
                clearButton.Click += (s, ev) => { ed.Clear(); dialog.Close(); };
                
                var cancelButton = new Button 
                { 
                    Content = "Cancel", 
                    Width = 90, 
                    Height = 32, 
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), 
                    Foreground = Brushes.White, 
                    BorderThickness = new Thickness(1), 
                    BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) 
                };
                cancelButton.Click += (s, ev) => dialog.Close();
                
                buttonPanel.Children.Add(clearButton);
                buttonPanel.Children.Add(cancelButton);
                stackPanel.Children.Add(buttonPanel);
                
                dialog.Content = stackPanel;
                dialog.ShowDialog();
            }
        }
        private void OpenFile_Click(object sender, RoutedEventArgs e) { var ofd = new OpenFileDialog { Filter = "Lua (*.lua)|*.lua|All|*.*" }; if (ofd.ShowDialog() == true) AddNewTab(Path.GetFileName(ofd.FileName), File.ReadAllText(ofd.FileName)); }
        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentEditor() is TextEditor ed)
            {
                var sfd = new SaveFileDialog { Filter = "Lua (*.lua)|*.lua|All|*.*", InitialDirectory = _scriptsRoot, FileName = "script.lua" };
                if (sfd.ShowDialog() == true) 
                { 
                    File.WriteAllText(sfd.FileName, ed.Text); 
                    if (ScriptTabs.SelectedItem is TabItem t && t.Header is StackPanel sp && sp.Children[0] is TextBlock tb) 
                        tb.Text = Path.GetFileName(sfd.FileName); 
                    LoadScriptTree(); 
                }
            }
        }

        private void CreateNewFile(string dir, bool folder)
        {
            string name = PromptDialog.Show(folder ? "New Folder" : "New Script", "Name:", folder ? "New Folder" : "script.lua");
            if (!string.IsNullOrWhiteSpace(name)) { string p = Path.Combine(dir, name); if (folder) Directory.CreateDirectory(p); else File.WriteAllText(p, ""); LoadScriptTree(); }
        }

        private void Context_Delete_Click(string p) 
        { 
            bool isFolder = Directory.Exists(p);
            string name = Path.GetFileName(p);
            
            var dialog = new Window 
            { 
                Width = 420, 
                Height = isFolder ? 240 : 260, 
                Title = "Delete", 
                WindowStyle = WindowStyle.None, 
                ResizeMode = ResizeMode.NoResize, 
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), 
                WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                Owner = this, 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(1) 
            };
            
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
            dialog.BorderBrush = gradient;

            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            
            var titleBlock = new TextBlock 
            { 
                Text = $"Delete {(isFolder ? "Folder" : "File")}", 
                FontSize = 16, 
                FontWeight = FontWeights.Bold, 
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), 
                Margin = new Thickness(0, 0, 0, 15) 
            };
            stackPanel.Children.Add(titleBlock);
            
            var messageBlock = new TextBlock 
            { 
                Text = $"Are you sure you want to delete '{name}'?\nThis action cannot be undone.", 
                Margin = new Thickness(0, 0, 0, 10), 
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            };
            stackPanel.Children.Add(messageBlock);
            
            // Show file/folder stats
            var statsText = "";
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(p);
                var fileCount = dirInfo.GetFiles("*", SearchOption.AllDirectories).Length;
                var folderCount = dirInfo.GetDirectories("*", SearchOption.AllDirectories).Length;
                statsText = $"Contains: {fileCount} file(s), {folderCount} folder(s)";
            }
            else
            {
                var fileInfo = new FileInfo(p);
                var sizeKb = fileInfo.Length / 1024.0;
                var lineCount = File.ReadAllLines(p).Length;
                statsText = $"Size: {sizeKb:F2} KB  |  Lines: {lineCount}";
            }
            
            var statsBlock = new TextBlock 
            { 
                Text = statsText, 
                Margin = new Thickness(0, 0, 0, 10), 
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
            };
            stackPanel.Children.Add(statsBlock);
            
            var pathBlock = new TextBlock 
            { 
                Text = $"Path: {p}", 
                Margin = new Thickness(0, 0, 0, 20), 
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
            };
            stackPanel.Children.Add(pathBlock);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            var deleteButton = new Button 
            { 
                Content = "Delete", 
                Width = 90, 
                Height = 32, 
                Margin = new Thickness(0, 0, 10, 0), 
                Background = new SolidColorBrush(Color.FromRgb(220, 50, 50)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(1), 
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                FontWeight = FontWeights.Bold
            };
            deleteButton.Click += (s, ev) => 
            { 
                if (Directory.Exists(p)) 
                    Directory.Delete(p, true); 
                else 
                    File.Delete(p); 
                LoadScriptTree(); 
                dialog.Close(); 
            };
            
            var cancelButton = new Button 
            { 
                Content = "Cancel", 
                Width = 90, 
                Height = 32, 
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(1), 
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) 
            };
            cancelButton.Click += (s, ev) => dialog.Close();
            
            buttonPanel.Children.Add(deleteButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);
            
            dialog.Content = stackPanel;
            dialog.ShowDialog();
        }
        private void Context_Rename_Click(string p)
        {
            string old = Path.GetFileName(p); string n = PromptDialog.Show("Rename", "Name:", old);
            if (!string.IsNullOrWhiteSpace(n) && n != old) { string np = Path.Combine(Path.GetDirectoryName(p)!, n); if (File.Exists(np) || Directory.Exists(np)) return; if (Directory.Exists(p)) Directory.Move(p, np); else File.Move(p, np); LoadScriptTree(); }
        }

        private void ListContext_NewScript_Click(object sender, RoutedEventArgs e) => CreateNewFile((ScriptTree.SelectedItem as TreeViewItem)?.Tag?.ToString() ?? _scriptsRoot, false);
        private void ListContext_NewFolder_Click(object sender, RoutedEventArgs e) => CreateNewFile((ScriptTree.SelectedItem as TreeViewItem)?.Tag?.ToString() ?? _scriptsRoot, true);
        private void ListContext_Refresh_Click(object sender, RoutedEventArgs e) => LoadScriptTree();

        private bool ShowConfirmDialog(string title, string message)
        {
            Window w = new Window { Width = 350, Height = 180, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Foreground = Brushes.White, BorderThickness = new Thickness(1) };
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
            w.BorderBrush = gradient;

            StackPanel s = new StackPanel { Margin = new Thickness(15) };
            TextBlock titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), Margin = new Thickness(0, 0, 0, 15) };
            s.Children.Add(titleBlock);
            s.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap });

            StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button yes = new Button { Content = "Yes", Width = 80, Height = 30, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0)) };
            yes.Click += (se, ev) => { w.DialogResult = true; w.Close(); };
            Button no = new Button { Content = "No", Width = 80, Height = 30, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
            no.Click += (se, ev) => { w.DialogResult = false; w.Close(); };
            b.Children.Add(yes); b.Children.Add(no); s.Children.Add(b);
            w.Content = s;
            return w.ShowDialog() == true;
        }

        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentEditor() is TextEditor editor)
            {
                SearchInEditor(editor, SearchTextBox.Text, forward: true);
            }
        }

        private void SearchPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentEditor() is TextEditor editor)
            {
                SearchInEditor(editor, SearchTextBox.Text, forward: false);
            }
        }

        private void CloseSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBar.Visibility = Visibility.Collapsed;
        }

        private void ShowAttachClientsForTab(TabItem tab)
        {
            if (!_remoteServerEnabled || _connectedClients.Count == 0)
            {
                System.Windows.MessageBox.Show("No clients connected. Start the remote server first.", "No Clients", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Window
            {
                Title = $"Attach Clients to Tab",
                Width = 400,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listBox = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Margin = new Thickness(0, 0, 0, 10)
            };

            lock (_clientsLock)
            {
                foreach (var client in _connectedClients)
                {
                    listBox.Items.Add(client);
                }
                
                // Pre-select already attached clients
                if (_tabClientAttachments.ContainsKey(tab))
                {
                    foreach (var attachedClient in _tabClientAttachments[tab])
                    {
                        listBox.SelectedItems.Add(attachedClient);
                    }
                }
            }

            Grid.SetRow(listBox, 0);
            grid.Children.Add(listBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            
            var attachButton = new Button { Content = "Attach Selected", Width = 100, Margin = new Thickness(0, 0, 5, 0) };
            attachButton.Click += (s, ev) =>
            {
                var selected = listBox.SelectedItems.Cast<ClientInfo>().ToList();
                if (selected.Count > 0)
                {
                    _tabClientAttachments[tab] = selected;
                    UpdateTabHeaderColor(tab);
                }
                else
                {
                    _tabClientAttachments.Remove(tab);
                    UpdateTabHeaderColor(tab);
                }
                dialog.Close();
            };

            var clearButton = new Button { Content = "Clear", Width = 80, Margin = new Thickness(0, 0, 5, 0) };
            clearButton.Click += (s, ev) =>
            {
                _tabClientAttachments.Remove(tab);
                UpdateTabHeaderColor(tab);
                dialog.Close();
            };

            var closeButton = new Button { Content = "Close", Width = 80 };
            closeButton.Click += (s, ev) => dialog.Close();

            buttonPanel.Children.Add(attachButton);
            buttonPanel.Children.Add(clearButton);
            buttonPanel.Children.Add(closeButton);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        private void UpdateTabHeaderColor(TabItem tab)
        {
            if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
            {
                if (_tabClientAttachments.ContainsKey(tab) && _tabClientAttachments[tab].Count > 0)
                {
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x39, 0xCF, 0x39));
                }
                else
                {
                    // Reset to default style binding
                    tb.ClearValue(TextBlock.ForegroundProperty);
                }
            }
        }

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            if (!_remoteServerEnabled || _connectedClients.Count == 0)
            {
                System.Windows.MessageBox.Show("No clients connected. Start the remote server first.", "No Clients", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a selection dialog
            var dialog = new Window
            {
                Title = "Select Client",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Margin = new Thickness(0, 0, 0, 10)
            };

            // Create statusText early so it can be used in event handlers
            var statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // Create checkbox early so it can be referenced in handlers
            var allClientsCheckBox = new System.Windows.Controls.CheckBox 
            { 
                Content = "All Clients", 
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                IsChecked = _executeToAllClients
            };

            // Custom item template with circle indicator
            var itemTemplate = new DataTemplate();
            var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
            stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var ellipseFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.WidthProperty, 10.0);
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.HeightProperty, 10.0);
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.MarginProperty, new Thickness(0, 0, 10, 0));
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.FillProperty, new SolidColorBrush(Colors.Red));
            ellipseFactory.Name = "StatusCircle";

            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());

            stackPanelFactory.AppendChild(ellipseFactory);
            stackPanelFactory.AppendChild(textBlockFactory);
            itemTemplate.VisualTree = stackPanelFactory;
            listBox.ItemTemplate = itemTemplate;

            lock (_clientsLock)
            {
                foreach (var client in _connectedClients)
                {
                    listBox.Items.Add(client);
                }
                if (_selectedClient != null)
                {
                    listBox.SelectedItem = _selectedClient;
                }
            }

            // Update circle colors based on selection
            Action updateCircles = () =>
            {
                foreach (var item in listBox.Items)
                {
                    var container = listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (container != null)
                    {
                        var ellipse = FindVisualChild<System.Windows.Shapes.Ellipse>(container);
                        if (ellipse != null)
                        {
                            ellipse.Fill = new SolidColorBrush(item == _selectedClient ? Color.FromArgb(0xFF, 0x39, 0xCF, 0x39) : Colors.Red);
                        }
                    }
                }
            };

            // Handle click to toggle selection using PreviewMouseLeftButtonDown to avoid multi-trigger
            listBox.PreviewMouseLeftButtonDown += (s, ev) =>
            {
                var item = GetListBoxItemFromPoint(listBox, ev.GetPosition(listBox));
                if (item != null && item.Content is ClientInfo client)
                {
                    // Toggle: if already selected, deselect
                    if (_selectedClient == client)
                    {
                        _selectedClient = null;
                        _executeToAllClients = false;
                        allClientsCheckBox.IsChecked = false;
                        statusText.Text = "No client selected";
                    }
                    else
                    {
                        _selectedClient = client;
                        _executeToAllClients = false;
                        allClientsCheckBox.IsChecked = false;
                        statusText.Text = $"Executing to: {client}";
                    }
                    updateCircles();
                    ev.Handled = true;
                }
            };

            // Update circles after items are generated
            listBox.Loaded += (s, ev) => updateCircles();

            Grid.SetRow(listBox, 0);
            grid.Children.Add(listBox);

            // Checkbox and close button
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            
            // Set up checkbox event handlers
            allClientsCheckBox.Checked += (s, ev) =>
            {
                _executeToAllClients = true;
                _selectedClient = null;
                statusText.Text = "Executing to: All Clients";
                updateCircles();
            };
            allClientsCheckBox.Unchecked += (s, ev) =>
            {
                _executeToAllClients = false;
                statusText.Text = "No client selected";
                updateCircles();
            };

            var closeButton = new Button { Content = "Close", Width = 80 };
            closeButton.Click += (s, ev) => dialog.Close();

            buttonPanel.Children.Add(allClientsCheckBox);
            buttonPanel.Children.Add(closeButton);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            Grid.SetRow(statusText, 2);
            grid.Children.Add(statusText);

            dialog.Content = grid;

            dialog.Show();
        }

        private async void Remote_Click(object sender, RoutedEventArgs e)
        {
            _remoteServerEnabled = !_remoteServerEnabled;
            
            if (_remoteServerEnabled)
            {
                await StartRemoteServer();
            }
            else
            {
                StopRemoteServer();
            }
            
            UpdateRemoteButtonBorder();
        }

        private void UpdateRemoteButtonBorder()
        {
            if (RemoteButton.Template.FindName("border", RemoteButton) is Border border)
            {
                border.BorderBrush = new SolidColorBrush(_remoteServerEnabled ? Color.FromArgb(0xFF, 0x39, 0xCF, 0x39) : Colors.Red);
            }
            
            // Update button content based on connection status
            if (_remoteServerEnabled && _connectedClients.Count > 0)
            {
                RemoteButton.Content = "🔗";  // Connected icon
            }
            else if (_remoteServerEnabled)
            {
                RemoteButton.Content = "📡";  // Server running icon
            }
            else
            {
                RemoteButton.Content = "Remote";  // Default text
            }
        }

        private async Task StartRemoteServer()
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.IPv6Any, 8080);
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                _tcpListener.Start();
                
                _ = Task.Run(async () =>
                {
                    while (_remoteServerEnabled && _tcpListener != null)
                    {
                        try
                        {
                            var client = await _tcpListener.AcceptTcpClientAsync();
                            var clientInfo = new ClientInfo(client);
                            
                            // Add client immediately to the list
                            lock (_clientsLock)
                            {
                                _connectedClients.Add(clientInfo);
                            }
                            
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateRemoteButtonBorder();
                            });
                            
                            // Read IDENTIFY message in background to update client info
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var stream = client.GetStream();
                                    using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
                                    {
                                        var message = await reader.ReadLineAsync();
                                        if (!string.IsNullOrEmpty(message) && message.StartsWith("IDENTIFY|"))
                                        {
                                            var parts = message.Split('|');
                                            if (parts.Length >= 4)
                                            {
                                                clientInfo.PlayerName = parts[1];
                                                clientInfo.Version = parts[2];
                                                clientInfo.Server = parts[3];
                                            }
                                        }
                                    }
                                }
                                catch { }
                            });
                        }
                        catch (Exception)
                        {
                            break;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start server: {ex.Message}");
                _remoteServerEnabled = false;
            }
        }

        private void StopRemoteServer()
        {
            try
            {
                _tcpListener?.Stop();
                _tcpListener = null;
                
                lock (_clientsLock)
                {
                    foreach (var clientInfo in _connectedClients)
                    {
                        try { clientInfo.Client.Close(); } catch { }
                    }
                    _connectedClients.Clear();
                    _selectedClient = null;
                }
            }
            catch { }
        }

        private async Task SendCommandToClients(string command)
        {
            List<ClientInfo> clientsCopy;
            lock (_clientsLock)
            {
                // FIRST: Check if current tab has specific clients attached
                if (ScriptTabs.SelectedItem is TabItem currentTab && _tabClientAttachments.ContainsKey(currentTab))
                {
                    var attachedClients = _tabClientAttachments[currentTab];
                    clientsCopy = attachedClients.Where(c => _connectedClients.Contains(c)).ToList();
                }
                // SECOND: Check if "All Clients" checkbox is enabled
                else if (_executeToAllClients)
                {
                    clientsCopy = _connectedClients.ToList();
                }
                // THIRD: Only send if a specific client is selected
                else if (_selectedClient != null && _connectedClients.Contains(_selectedClient))
                {
                    clientsCopy = new List<ClientInfo> { _selectedClient };
                }
                else
                {
                    // No client selected and checkbox not checked, don't send
                    return;
                }
            }
            
            List<ClientInfo> disconnected = new List<ClientInfo>();
            
            foreach (var clientInfo in clientsCopy)
            {
                try
                {
                    if (!clientInfo.Client.Connected)
                    {
                        disconnected.Add(clientInfo);
                        continue;
                    }
                    
                    var stream = clientInfo.Client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                }
                catch
                {
                    disconnected.Add(clientInfo);
                }
            }
            
            lock (_clientsLock)
            {
                foreach (var dc in disconnected)
                {
                    _connectedClients.Remove(dc);
                    if (_selectedClient == dc)
                    {
                        _selectedClient = null;
                    }
                    try { dc.Client.Close(); } catch { }
                }
            }
            
            // Update UI if clients disconnected
            if (disconnected.Count > 0)
            {
                await Dispatcher.InvokeAsync(() => UpdateRemoteButtonBorder());
            }
        }

        private void SearchInEditor(TextEditor editor, string searchText, bool forward)
        {
            if (string.IsNullOrEmpty(searchText)) return;

            string text = editor.Text;
            int startIndex = forward ? editor.CaretOffset : editor.CaretOffset - 1;

            int foundIndex = -1;
            if (forward)
            {
                foundIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                if (foundIndex == -1 && startIndex > 0)
                {
                    // Wrap around
                    foundIndex = text.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                foundIndex = text.LastIndexOf(searchText, startIndex >= 0 ? startIndex : 0, StringComparison.OrdinalIgnoreCase);
                if (foundIndex == -1)
                {
                    // Wrap around
                    foundIndex = text.LastIndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (foundIndex >= 0)
            {
                editor.Select(foundIndex, searchText.Length);
                editor.CaretOffset = foundIndex + (forward ? searchText.Length : 0);
                editor.ScrollToLine(editor.Document.GetLineByOffset(foundIndex).LineNumber);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static ListBoxItem? GetListBoxItemFromPoint(ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as DependencyObject;
            while (element != null)
            {
                if (element is ListBoxItem item)
                    return item;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }
    }

    public static class PromptDialog
    {
        public static string Show(string title, string msg, string def = "")
        {
            Window w = new Window { Width = 300, Height = 150, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)), WindowStartupLocation = WindowStartupLocation.CenterScreen, Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(60,60,60)), BorderThickness = new Thickness(1) };
            StackPanel s = new StackPanel { Margin = new Thickness(10) };
            s.Children.Add(new TextBlock { Text = msg, Margin = new Thickness(0,0,0,10) });
            TextBox t = new TextBox { Text = def, Background = new SolidColorBrush(Color.FromRgb(30,30,30)), Foreground = Brushes.White, BorderBrush = Brushes.Gray, Padding = new Thickness(2) };
            s.Children.Add(t);
            StackPanel b = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,15,0,0) };
            Button ok = new Button { Content = "OK", Width = 60, Height = 25, Margin = new Thickness(0,0,10,0), Background = new SolidColorBrush(Color.FromRgb(60,60,60)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            ok.Click += (se, ev) => { w.DialogResult = true; w.Close(); };
            Button c = new Button { Content = "Cancel", Width = 60, Height = 25, Background = new SolidColorBrush(Color.FromRgb(60,60,60)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            c.Click += (se, ev) => { w.DialogResult = false; w.Close(); };
            b.Children.Add(ok); b.Children.Add(c); s.Children.Add(b);
            w.Content = s; t.Focus(); t.SelectAll();
            return w.ShowDialog() == true ? t.Text : def;
        }
    }

    public class GlobalVariableColorizer : DocumentColorizingTransformer
    {
        private readonly List<string> _globalVariables;
        private static readonly Brush GlobalBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xFA, 0x8C));

        public GlobalVariableColorizer(List<string> globalVariables)
        {
            _globalVariables = globalVariables;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (_globalVariables.Count == 0) return;

            int lineStartOffset = line.Offset;
            string text = CurrentContext.Document.GetText(line);
            
            foreach (var global in _globalVariables)
            {
                if (string.IsNullOrWhiteSpace(global)) continue;
                
                int index = 0;
                while ((index = text.IndexOf(global, index)) >= 0)
                {
                    // Check if it's a whole word
                    bool isWordStart = index == 0 || !char.IsLetterOrDigit(text[index - 1]) && text[index - 1] != '_';
                    bool isWordEnd = index + global.Length >= text.Length || !char.IsLetterOrDigit(text[index + global.Length]) && text[index + global.Length] != '_';
                    
                    if (isWordStart && isWordEnd)
                    {
                        ChangeLinePart(
                            lineStartOffset + index,
                            lineStartOffset + index + global.Length,
                            element => element.TextRunProperties.SetForegroundBrush(GlobalBrush)
                        );
                    }
                    index += global.Length;
                }
            }
        }
    }
}