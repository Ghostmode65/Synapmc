using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private readonly string _globalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".jsmacros", "scripts", "projects", "Synapmc", "global.json");
        private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly string _bookmarksPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bookmarks.json");
        private readonly string _unicodeBookmarksPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unicode_bookmarks.json");
        private readonly string _sessionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session.json");
        private readonly string _tempSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".jsmacros", "scripts", "projects", "Synapmc", "temp", "saves");
        private IHighlightingDefinition? _luaHighlighting;
        private List<string> _globalVariables = new List<string>();
        private List<string> _bookmarkedFolders = new List<string>();
        private List<string> _bookmarkedUnicode = new List<string>();
        private ICSharpCode.AvalonEdit.Search.SearchPanel? _currentSearchPanel;
        private bool _dontAskDeleteConfirmation = false;
        private bool _dontAskUnsavedConfirmation = false;
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
            LoadBookmarks();
            LoadUnicodeBookmarks();
            if (!Directory.Exists(_scriptsRoot)) Directory.CreateDirectory(_scriptsRoot);
            if (!Directory.Exists(_tempSavePath)) Directory.CreateDirectory(_tempSavePath);
            LoadScriptTree();
            
            // Restore previous session
            RestoreSession();
            
            // Add Ctrl+F shortcut for search
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // Initialize remote button border
            UpdateRemoteButtonBorder();
            
            // Hook closing event
            this.Closing += MainWindow_Closing;
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
                        if (settings.ContainsKey("DontAskDeleteConfirmation") && bool.TryParse(settings["DontAskDeleteConfirmation"], out bool dontAsk))
                        {
                            _dontAskDeleteConfirmation = dontAsk;
                        }
                        if (settings.ContainsKey("DontAskUnsavedConfirmation") && bool.TryParse(settings["DontAskUnsavedConfirmation"], out bool dontAskUnsaved))
                        {
                            _dontAskUnsavedConfirmation = dontAskUnsaved;
                        }
                        return;
                    }
                }
            }
            catch { }
            // Default to .jsmacros/scripts/macros in roaming
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _scriptsRoot = Path.Combine(roaming, ".jsmacros", "scripts", "macros");
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    ["WorkspacePath"] = _scriptsRoot,
                    ["AlwaysOnTop"] = this.Topmost.ToString(),
                    ["DontAskDeleteConfirmation"] = _dontAskDeleteConfirmation.ToString(),
                    ["DontAskUnsavedConfirmation"] = _dontAskUnsavedConfirmation.ToString()
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
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+Z - Undo
                if (GetCurrentEditor() is TextEditor editor)
                {
                    editor.Undo();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+Y - Redo
                if (GetCurrentEditor() is TextEditor editor)
                {
                    editor.Redo();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F2)
            {
                // F2 to rename tab or workspace item
                if (ScriptTabs.IsFocused || ScriptTabs.IsKeyboardFocusWithin)
                {
                    // Rename current tab
                    if (ScriptTabs.SelectedItem is TabItem tab && tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
                    {
                        string oldName = tb.Text;
                        string newName = PromptDialog.Show("Rename Tab", "Name:", oldName);
                        if (!string.IsNullOrWhiteSpace(newName) && newName != oldName)
                        {
                            tb.Text = newName;
                        }
                        e.Handled = true;
                    }
                }
                else if (ScriptTree.SelectedItem is TreeViewItem item && item.Tag is string path)
                {
                    // Rename workspace file/folder
                    Context_Rename_Click(path);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                // Delete key for tabs or workspace items
                if (ScriptTabs.IsFocused || ScriptTabs.IsKeyboardFocusWithin)
                {
                    // Delete current tab
                    if (ScriptTabs.SelectedItem is TabItem tab)
                    {
                        DeleteTab(tab);
                        e.Handled = true;
                    }
                }
                else if (ScriptTree.SelectedItem is TreeViewItem item && item.Tag is string path)
                {
                    // Delete workspace file/folder
                    Context_Delete_Click(path);
                    e.Handled = true;
                }
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
                else
                {
                    // Default global list
                    _globalVariables = new List<string>
                    {
                        "luaj",
                        "luajava",
                        "Chat",
                        "Client",
                        "FS",
                        "GlobalVars",
                        "Hud",
                        "JavaUtils",
                        "JavaWrapper",
                        "JsMacros",
                        "KeyBind",
                        "Player",
                        "PositionCommon",
                        "Reflection",
                        "Request",
                        "Time",
                        "Utils",
                        "World"
                    };
                    
                    // Save the default list to file
                    string json = JsonSerializer.Serialize(_globalVariables, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_globalPath, json);
                }
            }
            catch { }
        }

        private void LoadBookmarks()
        {
            _bookmarkedFolders.Clear();
            try
            {
                if (File.Exists(_bookmarksPath))
                {
                    string json = File.ReadAllText(_bookmarksPath);
                    _bookmarkedFolders = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch { }
        }

        private void SaveBookmarks()
        {
            try
            {
                string json = JsonSerializer.Serialize(_bookmarkedFolders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_bookmarksPath, json);
            }
            catch { }
        }

        private void LoadUnicodeBookmarks()
        {
            _bookmarkedUnicode.Clear();
            try
            {
                if (File.Exists(_unicodeBookmarksPath))
                {
                    string json = File.ReadAllText(_unicodeBookmarksPath);
                    _bookmarkedUnicode = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch { }
        }

        private void SaveUnicodeBookmarks()
        {
            try
            {
                string json = JsonSerializer.Serialize(_bookmarkedUnicode, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_unicodeBookmarksPath, json);
            }
            catch { }
        }

        private void LoadScriptTree()
        {
            ScriptTree.Items.Clear();
            
            // Add bookmarked folders first
            foreach (var bookmarkedPath in _bookmarkedFolders.ToList())
            {
                if (Directory.Exists(bookmarkedPath))
                {
                    TreeViewItem item = CreateTreeItem("📁 " + Path.GetFileName(bookmarkedPath), true, bookmarkedPath, true);
                    ScriptTree.Items.Add(item);
                    PopulateTree(bookmarkedPath, item, true);
                }
                else
                {
                    // Remove bookmark if folder doesn't exist
                    _bookmarkedFolders.Remove(bookmarkedPath);
                    SaveBookmarks();
                }
            }
            
            // Add regular workspace folder
            PopulateTree(_scriptsRoot, ScriptTree, false);
        }

        private void PopulateTree(string currentDir, ItemsControl parent, bool isBookmarked)
        {
            foreach (var dir in Directory.GetDirectories(currentDir).OrderBy(d => d))
            {
                TreeViewItem item = CreateTreeItem("📁 " + Path.GetFileName(dir), true, dir, isBookmarked);
                parent.Items.Add(item);
                PopulateTree(dir, item, isBookmarked);
            }
            foreach (var file in Directory.GetFiles(currentDir).Where(f => f.EndsWith(".lua") || f.EndsWith(".txt")).OrderBy(f => f))
            {
                parent.Items.Add(CreateTreeItem("📄 " + Path.GetFileName(file), false, file, isBookmarked));
            }
        }

        private TreeViewItem CreateTreeItem(string header, bool isFolder, string fullPath, bool isBookmarked = false)
        {
            TextBlock textBlock = new TextBlock { Text = header };
            
            // Check if this file is currently opened in any tab
            bool isOpened = false;
            if (!isFolder)
            {
                foreach (TabItem tab in ScriptTabs.Items)
                {
                    if (tab.Tag is string tabPath && tabPath == fullPath)
                    {
                        isOpened = true;
                        break;
                    }
                }
            }
            
            if (isOpened)
            {
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)); // Dull orange
            }
            else if (isBookmarked)
            {
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)); // Cornflower blue
            }
            
            TreeViewItem item = new TreeViewItem { Header = textBlock, Tag = fullPath };
            if (!isFolder)
            {
                item.MouseDoubleClick += (s, e) => 
                { 
                    if (IsInHeader(e.OriginalSource as DependencyObject, item)) 
                    {
                        // Open or switch to existing tab
                        OpenOrSwitchToTab(fullPath);
                        e.Handled = true;
                    }
                };
            }

            ContextMenu menu = new ContextMenu { StaysOpen = false };
            if (isFolder)
            {
                MenuItem ns = new MenuItem { Header = "New Script" }; ns.Click += (s, e) => CreateNewFile(fullPath, false);
                MenuItem nf = new MenuItem { Header = "New Folder" }; nf.Click += (s, e) => CreateNewFile(fullPath, true);
                menu.Items.Add(ns); menu.Items.Add(nf); menu.Items.Add(new Separator());
                
                // Add bookmark option for folders
                bool currentlyBookmarked = _bookmarkedFolders.Contains(fullPath);
                MenuItem bookmark = new MenuItem { Header = currentlyBookmarked ? "Remove Bookmark" : "Add Bookmark" };
                bookmark.Click += (s, e) => ToggleBookmark(fullPath);
                menu.Items.Add(bookmark);
                menu.Items.Add(new Separator());
            }
            else
            {
                MenuItem ex = new MenuItem { Header = "Execute" }; ex.Click += (s, e) => System.Windows.MessageBox.Show($"Executing {Path.GetFileName(fullPath)}...");
                menu.Items.Add(ex);
            }

            MenuItem ren = new MenuItem { Header = "Rename" }; ren.Click += (s, e) => Context_Rename_Click(fullPath);
            MenuItem del = new MenuItem { Header = "Delete" }; del.Click += (s, e) => Context_Delete_Click(fullPath);
            MenuItem openExplorer = new MenuItem { Header = "Open in File Explorer" };
            openExplorer.Click += (s, e) =>
            {
                if (File.Exists(fullPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                }
                else if (Directory.Exists(fullPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{fullPath}\"");
                }
            };
            menu.Items.Add(ren); 
            menu.Items.Add(del);
            menu.Items.Add(new Separator());
            menu.Items.Add(openExplorer);
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

        private void OpenOrSwitchToTab(string filePath)
        {
            // Check if file is already open
            foreach (TabItem tab in ScriptTabs.Items)
            {
                if (tab.Tag is string tabPath && tabPath == filePath)
                {
                    ScriptTabs.SelectedItem = tab;
                    return;
                }
            }
            
            // File not open, create new tab
            if (File.Exists(filePath))
            {
                string content = File.ReadAllText(filePath);
                string fileName = Path.GetFileName(filePath);
                AddNewTab(fileName, content, filePath);
                // Don't reload tree - just update the specific file's color
                UpdateFileColorInTree(filePath, true);
            }
        }
        
        private void UpdateFileColorInTree(string filePath, bool isOpened)
        {
            // Update the file's color in the tree without reloading
            foreach (TreeViewItem item in ScriptTree.Items)
            {
                if (FindTreeItemByPath(item, filePath) is TreeViewItem foundItem && foundItem.Header is TextBlock tb)
                {
                    tb.Foreground = isOpened 
                        ? new SolidColorBrush(Color.FromRgb(255, 140, 0)) 
                        : Brushes.White;
                    break;
                }
            }
        }
        
        private TreeViewItem? FindTreeItemByPath(TreeViewItem item, string path)
        {
            if (item.Tag?.ToString() == path)
                return item;
                
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                {
                    var found = FindTreeItemByPath(childItem, path);
                    if (found != null)
                        return found;
                }
            }
            
            return null;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Check for unsaved scripts
            var unsavedTabs = new List<string>();
            foreach (TabItem tab in ScriptTabs.Items)
            {
                if (tab.Content is TextEditor editor && tab.Tag is string filePath)
                {
                    // Check if file exists and content differs
                    if (File.Exists(filePath))
                    {
                        string savedContent = File.ReadAllText(filePath);
                        if (editor.Text != savedContent)
                        {
                            if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
                                unsavedTabs.Add(tb.Text);
                        }
                    }
                }
                else if (tab.Content is TextEditor ed && !string.IsNullOrWhiteSpace(ed.Text))
                {
                    // New unsaved tab with content
                    if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
                        unsavedTabs.Add(tb.Text);
                }
            }
            
            if (unsavedTabs.Count > 0 && !_dontAskUnsavedConfirmation)
            {
                string tabList = unsavedTabs.Count <= 5 
                    ? string.Join(", ", unsavedTabs) 
                    : string.Join(", ", unsavedTabs.Take(3)) + $", and {unsavedTabs.Count - 3} more";
                string message = $"You have {unsavedTabs.Count} unsaved script(s): {tabList}\n\nClose anyway?";
                
                var result = ShowConfirmDialogWithCheckbox("Unsaved Scripts", message, "Don't ask me again");
                if (!result.confirmed)
                {
                    return; // Don't close
                }
                if (result.dontAskAgain)
                {
                    _dontAskUnsavedConfirmation = true;
                    SaveSettings();
                }
            }
            
            // Save session and temp files
            SaveSession();
            Application.Current.Shutdown();
        }
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
        
        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentEditor() is TextEditor editor)
            {
                editor.Undo();
            }
        }
        
        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentEditor() is TextEditor editor)
            {
                editor.Redo();
            }
        }
        
        private void AddNewTab(string? title = null, string content = "", string? filePath = null)
        {
            if (string.IsNullOrEmpty(title))
            {
                var used = new HashSet<int>();
                foreach (TabItem it in ScriptTabs.Items) 
                {
                    if (it.Header is StackPanel sp && sp.Children[0] is TextBlock tb) 
                    { 
                        var m = Regex.Match(tb.Text, @"^Script (\d+)$"); 
                        if (m.Success) 
                            used.Add(int.Parse(m.Groups[1].Value)); 
                    }
                }
                int n = 1; 
                while (used.Contains(n)) n++;
                title = $"Script {n}";
            }

            TabItem tab = new TabItem();
            tab.Tag = filePath; // Store file path for tracking
            
            // Create header with text
            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            // Remove .lua extension from display
            string displayTitle = title.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) 
                ? title.Substring(0, title.Length - 4) 
                : title;
            
            // Limit to 25 characters and shrink font if needed
            TextBlock tbHeader = new TextBlock { 
                Text = displayTitle, 
                VerticalAlignment = VerticalAlignment.Center, 
                Style = (Style)FindResource("TabHeaderTextStyle"),
                MaxWidth = 150
            };
            
            if (displayTitle.Length > 25)
            {
                // Shrink font size for long names
                tbHeader.FontSize = 10;
                
                // If still too small, truncate the text
                if (displayTitle.Length > 35)
                {
                    tbHeader.Text = displayTitle.Substring(0, 35) + "...";
                }
            }
            
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
            
            // Add hex color highlighter
            ed.TextArea.TextView.LineTransformers.Add(new HexColorHighlighter());
            
            // Add right-click context menu for color picker and code snippets
            ContextMenu editorContextMenu = new ContextMenu { StaysOpen = false };
            MenuItem insertColorItem = new MenuItem { Header = "Insert Color..." };
            insertColorItem.Click += (s, ev) => ShowColorPickerDialog(ed);
            editorContextMenu.Items.Add(insertColorItem);
            
            MenuItem insertUnicodeItem = new MenuItem { Header = "Insert Unicode..." };
            insertUnicodeItem.Click += (s, ev) => ShowUnicodePickerDialog(ed);
            editorContextMenu.Items.Add(insertUnicodeItem);
            
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
            DeleteTab(tab);
        }

        private void DeleteTab(TabItem tab)
        {
            if (tab.Content is TextEditor ed && !string.IsNullOrEmpty(ed.Text))
            {
                if (!_dontAskDeleteConfirmation)
                {
                    var dialog = new Window 
                    { 
                        Width = 400, 
                        Height = 240, 
                        Title = "Close Tab", 
                        WindowStyle = WindowStyle.None, 
                        ResizeMode = ResizeMode.NoResize, 
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), 
                        WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                        Owner = this, 
                        Foreground = Brushes.White, 
                        BorderThickness = new Thickness(1),
                        Topmost = true
                    };
                    
                    var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
                    dialog.BorderBrush = gradient;

                    var stackPanel = new StackPanel { Margin = new Thickness(20) };
                    
                    var titleBlock = new TextBlock 
                    { 
                        Text = "Close Tab", 
                        FontSize = 16, 
                        FontWeight = FontWeights.Bold, 
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), 
                        Margin = new Thickness(0, 0, 0, 15) 
                    };
                    stackPanel.Children.Add(titleBlock);
                    
                    var messageBlock = new TextBlock 
                    { 
                        Text = "Close script with content?\nThis action cannot be undone.", 
                        Margin = new Thickness(0, 0, 0, 10), 
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.White
                    };
                    stackPanel.Children.Add(messageBlock);
                    
                    var statsBlock = new TextBlock 
                    { 
                        Text = $"Lines: {ed.LineCount}  |  Characters: {ed.Text.Length}", 
                        Margin = new Thickness(0, 0, 0, 15), 
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
                    };
                    stackPanel.Children.Add(statsBlock);
                    
                    // Add checkbox for "Don't ask me again"
                    CheckBox dontAskCheckBox = new CheckBox
                    {
                        Content = "Don't ask me again",
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 15),
                        IsChecked = false
                    };
                    stackPanel.Children.Add(dontAskCheckBox);

                    var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                    
                    var closeTabButton = new Button 
                    { 
                        Content = "Close", 
                        Width = 90, 
                        Height = 32, 
                        Margin = new Thickness(0, 0, 10, 0), 
                        Background = new SolidColorBrush(Color.FromRgb(220, 50, 50)), 
                        Foreground = Brushes.White, 
                        BorderThickness = new Thickness(1), 
                        BorderBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                        FontWeight = FontWeights.Bold
                    };
                    closeTabButton.Click += (s, ev) => 
                    { 
                        if (dontAskCheckBox.IsChecked == true)
                        {
                            _dontAskDeleteConfirmation = true;
                            SaveSettings();
                        }
                        ScriptTabs.Items.Remove(tab);
                        _tabClientAttachments.Remove(tab);
                        if (ScriptTabs.Items.Count == 0)
                            AddNewTab();
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
                    
                    buttonPanel.Children.Add(closeTabButton);
                    buttonPanel.Children.Add(cancelButton);
                    stackPanel.Children.Add(buttonPanel);
                    
                    dialog.Content = stackPanel;
                    dialog.ShowDialog();
                    return;
                }
            }
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

        private void ShowUnicodePickerDialog(TextEditor editor)
        {
            Window unicodeWindow = new Window
            {
                Width = 600,
                Height = 500,
                Title = "Unicode Symbols",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                Topmost = true,
                AllowsTransparency = false
            };
            
            // Make window draggable
            unicodeWindow.MouseLeftButtonDown += (s, e) => 
            {
                if (e.ChangedButton == MouseButton.Left)
                    unicodeWindow.DragMove();
            };

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Title bar with X button
            Grid titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleBlock = new TextBlock
            {
                Text = "Unicode Symbols",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Margin = new Thickness(0, 0, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            titleGrid.Children.Add(titleBlock);

            Button closeBtn = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10)
            };
            closeBtn.Click += (s, e) => unicodeWindow.Close();
            Grid.SetColumn(closeBtn, 1);
            titleGrid.Children.Add(closeBtn);

            Grid.SetRow(titleGrid, 0);
            mainGrid.Children.Add(titleGrid);

            TabControl tabControl = new TabControl 
            { 
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), 
                BorderThickness = new Thickness(0)
            };
            
            // Create custom TabControl template to prevent row swapping
            ControlTemplate tabControlTemplate = new ControlTemplate(typeof(TabControl));
            FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(Grid.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
            
            // Row definitions
            FrameworkElementFactory rowDef1 = new FrameworkElementFactory(typeof(RowDefinition));
            rowDef1.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
            FrameworkElementFactory rowDef2 = new FrameworkElementFactory(typeof(RowDefinition));
            rowDef2.SetValue(RowDefinition.HeightProperty, new GridLength(1, GridUnitType.Star));
            gridFactory.AppendChild(rowDef1);
            gridFactory.AppendChild(rowDef2);
            
            // ScrollViewer for tabs (prevents wrapping/swapping)
            FrameworkElementFactory scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scrollFactory.SetValue(Grid.RowProperty, 0);
            
            // TabPanel inside ScrollViewer
            FrameworkElementFactory tabPanelFactory = new FrameworkElementFactory(typeof(TabPanel));
            tabPanelFactory.Name = "HeaderPanel";
            tabPanelFactory.SetValue(Panel.IsItemsHostProperty, true);
            tabPanelFactory.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            scrollFactory.AppendChild(tabPanelFactory);
            gridFactory.AppendChild(scrollFactory);
            
            // Content presenter
            FrameworkElementFactory contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.Name = "PART_SelectedContentHost";
            contentPresenterFactory.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
            contentPresenterFactory.SetValue(Grid.RowProperty, 1);
            gridFactory.AppendChild(contentPresenterFactory);
            
            tabControlTemplate.VisualTree = gridFactory;
            tabControl.Template = tabControlTemplate;
            
            // Apply the same TabItem style that ScriptTabs uses
            Style tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            
            ControlTemplate tabTemplate = new ControlTemplate(typeof(TabItem));
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Border";
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
            borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8, 8, 0, 0));
            borderFactory.SetValue(Border.MarginProperty, new Thickness(2, 0, 0, 0));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
            
            FrameworkElementFactory contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            borderFactory.AppendChild(contentFactory);
            
            tabTemplate.VisualTree = borderFactory;
            
            // Add triggers for selected and hover states
            Trigger selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 48)), "Border"));
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(255, 140, 0)), "Border"));
            tabTemplate.Triggers.Add(selectedTrigger);
            
            Trigger hoverTrigger = new Trigger { Property = TabItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 37, 38)), "Border"));
            tabTemplate.Triggers.Add(hoverTrigger);
            
            tabItemStyle.Setters.Add(new Setter(TabItem.TemplateProperty, tabTemplate));
            
            Grid.SetRow(tabControl, 1);

            // Define Unicode categories
            var categories = new Dictionary<string, string[]>
            {
                ["Bookmarks"] = _bookmarkedUnicode.ToArray(),
                ["Arrows"] = new[] { "←", "→", "↑", "↓", "↔", "↕", "⇐", "⇒", "⇑", "⇓", "⇔", "⇕", "⬅", "➡", "⬆", "⬇", "↖", "↗", "↘", "↙", "⟵", "⟶", "⟷", "⮕", "⮐", "⮑", "⮒", "⮓" },
                ["Shapes"] = new[] { "■", "□", "▢", "▣", "▤", "▥", "▦", "▧", "▨", "▩", "●", "○", "◉", "◎", "◐", "◑", "◒", "◓", "▲", "△", "▼", "▽", "◀", "◁", "▶", "▷", "◆", "◇", "◈", "❖", "★", "☆", "✦", "✧", "✪", "✫", "✬", "✭", "✮", "✯", "⬛", "⬜", "◼", "◻", "▪", "▫" },
                ["Math"] = new[] { "±", "×", "÷", "≈", "≠", "≡", "≤", "≥", "∞", "√", "∛", "∜", "∑", "∏", "∫", "∂", "∆", "∇", "π", "°", "′", "″", "‰", "∅", "∈", "∉", "∩", "∪", "⊂", "⊃", "⊆", "⊇", "⊕", "⊗", "⊥", "∀", "∃", "∄", "∧", "∨", "¬", "⊤", "⊥", "⊢", "⊣" },
                ["Greek"] = new[] { "α", "β", "γ", "δ", "ε", "ζ", "η", "θ", "ι", "κ", "λ", "μ", "ν", "ξ", "ο", "π", "ρ", "σ", "τ", "υ", "φ", "χ", "ψ", "ω", "Α", "Β", "Γ", "Δ", "Ε", "Ζ", "Η", "Θ", "Ι", "Κ", "Λ", "Μ", "Ν", "Ξ", "Ο", "Π", "Ρ", "Σ", "Τ", "Υ", "Φ", "Χ", "Ψ", "Ω" },
                ["Box Drawing"] = new[] { "─", "━", "│", "┃", "┄", "┅", "┆", "┇", "┈", "┉", "┊", "┋", "┌", "┐", "└", "┘", "├", "┤", "┬", "┴", "┼", "═", "║", "╔", "╗", "╚", "╝", "╠", "╣", "╦", "╩", "╬", "╒", "╓", "╕", "╖", "╘", "╙", "╛", "╜", "╞", "╟", "╡", "╢", "╤", "╥", "╧", "╨", "╪", "╫" },
                ["Symbols"] = new[] { "✓", "✔", "✕", "✖", "✗", "✘", "☐", "☑", "☒", "♠", "♣", "♥", "♦", "♤", "♧", "♡", "♢", "☀", "☁", "☂", "☃", "☄", "☎", "☏", "☮", "☯", "☸", "☺", "☻", "☼", "⚠", "⚡", "⚙", "⚛", "⚝", "⚡", "✂", "✏", "✉", "✈", "♫", "♬", "⚔", "⚖", "⛏", "⚒" },
                ["Misc"] = new[] { "•", "·", "‣", "⁃", "※", "∴", "∵", "‖", "¦", "…", "‥", "⋯", "⋮", "⋰", "⋱", "¶", "§", "†", "‡", "©", "®", "™", "℃", "℉", "№", "℗", "℠", "Ω", "℧", "℮", "‰", "‱", "′", "″", "‴", "⁗", "‼", "⁇", "⁈", "⁉", "⁊" },
                ["Currency"] = new[] { "$", "¢", "£", "¥", "€", "₹", "₽", "₿", "฿", "₴", "₦", "₨", "₩", "₪", "₱", "₡", "₵", "₸", "₺", "₼", "₾", "﷼" },
                ["Emoji"] = new[] { "😀", "😁", "😂", "😃", "😄", "😅", "😆", "😇", "😈", "😉", "😊", "😋", "😌", "😍", "😎", "😏", "😐", "😑", "😒", "😓", "😔", "😕", "😖", "😗", "😘", "😙", "😚", "😛", "😜", "😝", "😞", "😟", "😠", "😡", "😢", "😣", "😤", "😥", "😦", "😧" },
                ["Fractions"] = new[] { "½", "⅓", "⅔", "¼", "¾", "⅕", "⅖", "⅗", "⅘", "⅙", "⅚", "⅐", "⅛", "⅜", "⅝", "⅞", "⅑", "⅒" },
                ["Superscript"] = new[] { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹", "⁺", "⁻", "⁼", "⁽", "⁾", "ⁿ", "ⁱ" },
                ["Subscript"] = new[] { "₀", "₁", "₂", "₃", "₄", "₅", "₆", "₇", "₈", "₉", "₊", "₋", "₌", "₍", "₎", "ₐ", "ₑ", "ₒ", "ₓ", "ₔ" },
                ["Lines"] = new[] { "―", "‒", "–", "—", "―", "‖", "∣", "∥", "╱", "╲", "╳", "⁄", "∕", "⧵", "⧹" }
            };

            foreach (var category in categories)
            {
                // Create header TextBlock with orange for selected, purple for hover
                TextBlock headerText = new TextBlock { Text = category.Key, Foreground = Brushes.White };
                
                Style headerStyle = new Style(typeof(TextBlock));
                
                DataTrigger selectedDataTrigger = new DataTrigger();
                selectedDataTrigger.Binding = new System.Windows.Data.Binding("IsSelected") 
                { 
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TabItem), 1) 
                };
                selectedDataTrigger.Value = true;
                selectedDataTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 140, 0))));
                headerStyle.Triggers.Add(selectedDataTrigger);
                
                MultiDataTrigger hoverDataTrigger = new MultiDataTrigger();
                hoverDataTrigger.Conditions.Add(new System.Windows.Condition(new System.Windows.Data.Binding("IsSelected") 
                { 
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TabItem), 1) 
                }, false));
                hoverDataTrigger.Conditions.Add(new System.Windows.Condition(new System.Windows.Data.Binding("IsMouseOver") 
                { 
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TabItem), 1) 
                }, true));
                hoverDataTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(147, 112, 219))));
                headerStyle.Triggers.Add(hoverDataTrigger);
                
                headerText.Style = headerStyle;
                
                TabItem tabItem = new TabItem
                {
                    Header = headerText,
                    Style = tabItemStyle
                };

                ScrollViewer scrollViewer = new ScrollViewer 
                { 
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
                };
                
                // Override all ScrollBar resource keys to make it dark
                var darkBrush = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                var thumbBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                var hoverBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                
                scrollViewer.Resources.Add(SystemColors.ScrollBarBrushKey, darkBrush);
                scrollViewer.Resources.Add(SystemColors.ControlBrushKey, darkBrush);
                scrollViewer.Resources.Add(SystemColors.ControlLightBrushKey, darkBrush);
                scrollViewer.Resources.Add(SystemColors.ControlLightLightBrushKey, darkBrush);
                scrollViewer.Resources.Add(SystemColors.ControlDarkBrushKey, darkBrush);
                scrollViewer.Resources.Add(SystemColors.ControlDarkDarkBrushKey, darkBrush);
                
                WrapPanel wrapPanel = new WrapPanel { Margin = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) };

                foreach (var symbol in category.Value)
                {
                    bool isBookmarkedTab = category.Key == "Bookmarks";
                    bool isBookmarked = _bookmarkedUnicode.Contains(symbol);
                    
                    Button symbolBtn = new Button
                    {
                        Content = symbol,
                        Width = 45,
                        Height = 45,
                        Margin = new Thickness(5),
                        FontSize = 20,
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(isBookmarked ? 2 : 1),
                        BorderBrush = new SolidColorBrush(isBookmarked ? Color.FromRgb(255, 140, 0) : Color.FromRgb(80, 80, 80)),
                        Tag = symbol
                    };

                    // Left click - Insert
                    symbolBtn.Click += (s, e) =>
                    {
                        int caretOffset = editor.CaretOffset;
                        editor.Document.Insert(caretOffset, symbol);
                        editor.Focus();
                    };

                    // Right click - Context menu with dark styling
                    ContextMenu symbolMenu = new ContextMenu 
                    { 
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        BorderThickness = new Thickness(1),
                        Foreground = Brushes.White,
                        Padding = new Thickness(0)
                    };
                    
                    // Create style to remove white sidebar
                    Style menuStyle = new Style(typeof(ContextMenu));
                    ControlTemplate menuTemplate = new ControlTemplate(typeof(ContextMenu));
                    FrameworkElementFactory menuBorderFactory = new FrameworkElementFactory(typeof(Border));
                    menuBorderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 48)));
                    menuBorderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
                    menuBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                    
                    FrameworkElementFactory menuStackFactory = new FrameworkElementFactory(typeof(StackPanel));
                    menuStackFactory.SetValue(StackPanel.IsItemsHostProperty, true);
                    menuStackFactory.SetValue(StackPanel.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 48)));
                    menuBorderFactory.AppendChild(menuStackFactory);
                    
                    menuTemplate.VisualTree = menuBorderFactory;
                    menuStyle.Setters.Add(new Setter(ContextMenu.TemplateProperty, menuTemplate));
                    symbolMenu.Style = menuStyle;
                    
                    MenuItem insertItem = new MenuItem 
                    { 
                        Header = "Insert", 
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    insertItem.Click += (s, e) =>
                    {
                        int caretOffset = editor.CaretOffset;
                        editor.Document.Insert(caretOffset, symbol);
                        editor.Focus();
                    };
                    
                    MenuItem copyItem = new MenuItem 
                    { 
                        Header = "Copy", 
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    copyItem.Click += (s, e) => System.Windows.Clipboard.SetText(symbol);
                    
                    bool currentlyBookmarked = _bookmarkedUnicode.Contains(symbol);
                    MenuItem bookmarkItem = new MenuItem 
                    { 
                        Header = currentlyBookmarked ? "Remove Bookmark" : "Add Bookmark", 
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    bookmarkItem.Click += (s, e) =>
                    {
                        if (_bookmarkedUnicode.Contains(symbol))
                        {
                            _bookmarkedUnicode.Remove(symbol);
                        }
                        else
                        {
                            _bookmarkedUnicode.Add(symbol);
                        }
                        SaveUnicodeBookmarks();
                        
                        // Update the button border without closing the window
                        bool nowBookmarked = _bookmarkedUnicode.Contains(symbol);
                        
                        // Find and update all buttons with this symbol across all tabs
                        foreach (TabItem ti in tabControl.Items)
                        {
                            if (ti.Content is ScrollViewer sv && sv.Content is WrapPanel wp)
                            {
                                foreach (UIElement child in wp.Children)
                                {
                                    if (child is Button b && b.Tag.ToString() == symbol)
                                    {
                                        b.BorderThickness = new Thickness(nowBookmarked ? 2 : 1);
                                        b.BorderBrush = new SolidColorBrush(nowBookmarked ? Color.FromRgb(255, 140, 0) : Color.FromRgb(80, 80, 80));
                                        
                                        // Update context menu text
                                        if (b.ContextMenu != null)
                                        {
                                            foreach (var item in b.ContextMenu.Items)
                                            {
                                                if (item is MenuItem mi && (mi.Header.ToString().Contains("Bookmark")))
                                                {
                                                    mi.Header = nowBookmarked ? "Remove Bookmark" : "Add Bookmark";
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Refresh Bookmarks tab
                        foreach (TabItem ti in tabControl.Items)
                        {
                            if (ti.Header is TextBlock tb && tb.Text == "Bookmarks")
                            {
                                if (ti.Content is ScrollViewer sv && sv.Content is WrapPanel wp)
                                {
                                    wp.Children.Clear();
                                    foreach (var bookmarkedSymbol in _bookmarkedUnicode)
                                    {
                                        Button newBtn = new Button
                                        {
                                            Content = bookmarkedSymbol,
                                            Width = 45,
                                            Height = 45,
                                            Margin = new Thickness(5),
                                            FontSize = 20,
                                            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                                            Foreground = Brushes.White,
                                            BorderThickness = new Thickness(2),
                                            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                                            Tag = bookmarkedSymbol
                                        };
                                        
                                        // Left click - Insert
                                        newBtn.Click += (s2, e2) =>
                                        {
                                            int caretOffset = editor.CaretOffset;
                                            editor.Document.Insert(caretOffset, bookmarkedSymbol);
                                            editor.Focus();
                                        };
                                        
                                        // Right click menu
                                        ContextMenu newMenu = new ContextMenu 
                                        { 
                                            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                                            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                                            BorderThickness = new Thickness(1),
                                            Foreground = Brushes.White,
                                            Padding = new Thickness(0)
                                        };
                                        
                                        Style newMenuStyle = new Style(typeof(ContextMenu));
                                        ControlTemplate newMenuTemplate = new ControlTemplate(typeof(ContextMenu));
                                        FrameworkElementFactory newBorderFactory = new FrameworkElementFactory(typeof(Border));
                                        newBorderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 48)));
                                        newBorderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
                                        newBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                                        FrameworkElementFactory newStackFactory = new FrameworkElementFactory(typeof(StackPanel));
                                        newStackFactory.SetValue(StackPanel.IsItemsHostProperty, true);
                                        newStackFactory.SetValue(StackPanel.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 48)));
                                        newBorderFactory.AppendChild(newStackFactory);
                                        newMenuTemplate.VisualTree = newBorderFactory;
                                        newMenuStyle.Setters.Add(new Setter(ContextMenu.TemplateProperty, newMenuTemplate));
                                        newMenu.Style = newMenuStyle;
                                        
                                        MenuItem newInsertItem = new MenuItem 
                                        { 
                                            Header = "Insert", 
                                            Foreground = Brushes.White,
                                            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                                            Padding = new Thickness(8, 4, 8, 4)
                                        };
                                        newInsertItem.Click += (s2, e2) =>
                                        {
                                            int caretOffset = editor.CaretOffset;
                                            editor.Document.Insert(caretOffset, bookmarkedSymbol);
                                            editor.Focus();
                                        };
                                        
                                        MenuItem newCopyItem = new MenuItem 
                                        { 
                                            Header = "Copy", 
                                            Foreground = Brushes.White,
                                            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                                            Padding = new Thickness(8, 4, 8, 4)
                                        };
                                        newCopyItem.Click += (s2, e2) => System.Windows.Clipboard.SetText(bookmarkedSymbol);
                                        
                                        MenuItem newBookmarkItem = new MenuItem 
                                        { 
                                            Header = "Remove Bookmark", 
                                            Foreground = Brushes.White,
                                            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                                            Padding = new Thickness(8, 4, 8, 4)
                                        };
                                        newBookmarkItem.Click += (s3, e3) =>
                                        {
                                            // Remove from bookmarks
                                            _bookmarkedUnicode.Remove(bookmarkedSymbol);
                                            SaveUnicodeBookmarks();
                                            
                                            // Update all tabs
                                            bool nowBookmarked = _bookmarkedUnicode.Contains(bookmarkedSymbol);
                                            foreach (TabItem ti in tabControl.Items)
                                            {
                                                if (ti.Content is ScrollViewer sv && sv.Content is WrapPanel wp)
                                                {
                                                    foreach (UIElement child in wp.Children)
                                                    {
                                                        if (child is Button b && b.Tag.ToString() == bookmarkedSymbol)
                                                        {
                                                            b.BorderThickness = new Thickness(nowBookmarked ? 2 : 1);
                                                            b.BorderBrush = new SolidColorBrush(nowBookmarked ? Color.FromRgb(255, 140, 0) : Color.FromRgb(80, 80, 80));
                                                            
                                                            if (b.ContextMenu != null)
                                                            {
                                                                foreach (var item in b.ContextMenu.Items)
                                                                {
                                                                    if (item is MenuItem mi && (mi.Header.ToString().Contains("Bookmark")))
                                                                    {
                                                                        mi.Header = nowBookmarked ? "Remove Bookmark" : "Add Bookmark";
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            
                                            // Refresh Bookmarks tab by removing this button
                                            foreach (TabItem ti in tabControl.Items)
                                            {
                                                if (ti.Header is TextBlock headerTb && headerTb.Text == "Bookmarks")
                                                {
                                                    if (ti.Content is ScrollViewer sv && sv.Content is WrapPanel wp)
                                                    {
                                                        wp.Children.Clear();
                                                        foreach (var bm in _bookmarkedUnicode)
                                                        {
                                                            Button recreateBtn = new Button
                                                            {
                                                                Content = bm,
                                                                Width = 45,
                                                                Height = 45,
                                                                Margin = new Thickness(5),
                                                                FontSize = 20,
                                                                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                                                                Foreground = Brushes.White,
                                                                BorderThickness = new Thickness(2),
                                                                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                                                                Tag = bm
                                                            };
                                                            recreateBtn.Click += (s4, e4) =>
                                                            {
                                                                int caretOffset = editor.CaretOffset;
                                                                editor.Document.Insert(caretOffset, bm);
                                                                editor.Focus();
                                                            };
                                                            wp.Children.Add(recreateBtn);
                                                        }
                                                    }
                                                    break;
                                                }
                                            }
                                        };
                                        
                                        newMenu.Items.Add(newInsertItem);
                                        newMenu.Items.Add(newCopyItem);
                                        newMenu.Items.Add(new Separator());
                                        newMenu.Items.Add(newBookmarkItem);
                                        newBtn.ContextMenu = newMenu;
                                        
                                        wp.Children.Add(newBtn);
                                    }
                                }
                                break;
                            }
                        }
                    };
                    
                    symbolMenu.Items.Add(insertItem);
                    symbolMenu.Items.Add(copyItem);
                    symbolMenu.Items.Add(new Separator());
                    symbolMenu.Items.Add(bookmarkItem);
                    symbolBtn.ContextMenu = symbolMenu;

                    wrapPanel.Children.Add(symbolBtn);
                }

                scrollViewer.Content = wrapPanel;
                tabItem.Content = scrollViewer;
                tabControl.Items.Add(tabItem);
            }

            mainGrid.Children.Add(tabControl);

            unicodeWindow.Content = mainGrid;
            unicodeWindow.ShowDialog();
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
                    BorderThickness = new Thickness(1),
                    Topmost = true
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
            if (GetCurrentEditor() is TextEditor ed && ScriptTabs.SelectedItem is TabItem currentTab)
            {
                // Check if this tab has an associated file path
                if (currentTab.Tag is string existingPath && File.Exists(existingPath))
                {
                    // File already saved, just save to same location
                    File.WriteAllText(existingPath, ed.Text);
                    LoadScriptTree();
                }
                else
                {
                    // New file, show save dialog
                    var sfd = new SaveFileDialog { Filter = "Lua (*.lua)|*.lua|All|*.*", InitialDirectory = _scriptsRoot, FileName = "script.lua" };
                    if (sfd.ShowDialog() == true) 
                    { 
                        File.WriteAllText(sfd.FileName, ed.Text);
                        currentTab.Tag = sfd.FileName; // Store the path
                        if (currentTab.Header is StackPanel sp && sp.Children[0] is TextBlock tb) 
                            tb.Text = Path.GetFileName(sfd.FileName).Replace(".lua", ""); 
                        LoadScriptTree(); 
                    }
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
            
            // Check if there's content (file has data or folder is not empty)
            bool hasContent = false;
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(p);
                hasContent = dirInfo.GetFiles("*", SearchOption.AllDirectories).Length > 0 || 
                             dirInfo.GetDirectories("*", SearchOption.AllDirectories).Length > 0;
            }
            else
            {
                var fileInfo = new FileInfo(p);
                hasContent = fileInfo.Length > 0;
            }
            
            // If don't ask setting is on and no content, delete immediately
            if (_dontAskDeleteConfirmation && !hasContent)
            {
                if (Directory.Exists(p))
                    Directory.Delete(p, true);
                else
                    File.Delete(p);
                LoadScriptTree();
                return;
            }
            
            var dialog = new Window 
            { 
                Width = 420, 
                Height = isFolder ? 280 : 300, 
                Title = "Delete", 
                WindowStyle = WindowStyle.None, 
                ResizeMode = ResizeMode.NoResize, 
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), 
                WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                Owner = this, 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(1),
                Topmost = true
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
                Margin = new Thickness(0, 0, 0, 15), 
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
            };
            stackPanel.Children.Add(pathBlock);
            
            // Add checkbox for "Don't ask me again"
            CheckBox dontAskCheckBox = new CheckBox
            {
                Content = "Don't ask me again",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15),
                IsChecked = false
            };
            stackPanel.Children.Add(dontAskCheckBox);

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
                if (dontAskCheckBox.IsChecked == true)
                {
                    _dontAskDeleteConfirmation = true;
                    SaveSettings();
                }
                
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

        private void SearchWorkspace_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceSearchInput.Focus();
        }
        
        private void WorkspaceSearchInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (WorkspaceSearchInput.Text == "Search...")
            {
                WorkspaceSearchInput.Text = "";
                WorkspaceSearchInput.Foreground = Brushes.White;
            }
        }
        
        private void WorkspaceSearchInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WorkspaceSearchInput.Text))
            {
                WorkspaceSearchInput.Text = "Search...";
                WorkspaceSearchInput.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
            }
        }
        
        private void WorkspaceSearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && WorkspaceSearchInput.Text != "Search...")
            {
                string searchQuery = WorkspaceSearchInput.Text;
                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    SearchAndHighlightInTree(searchQuery.ToLower());
                }
            }
            else if (e.Key == Key.Escape)
            {
                WorkspaceSearchInput.Text = "";
                ScriptTree.Focus();
            }
        }
        
        private void CloseWorkspaceSearch_Click(object sender, RoutedEventArgs e)
        {
            // Not needed anymore since search is always visible
        }
        
        private void WorkspaceSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Not needed anymore - replaced by WorkspaceSearchInput_KeyDown
        }

        private void SearchAndHighlightInTree(string query)
        {
            // Expand all items and look for matches
            bool foundMatch = false;
            foreach (TreeViewItem item in ScriptTree.Items)
            {
                if (SearchTreeViewItem(item, query))
                {
                    foundMatch = true;
                    break;
                }
            }
            
            if (!foundMatch)
            {
                System.Windows.MessageBox.Show($"No files or folders found matching '{query}'", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool SearchTreeViewItem(TreeViewItem item, string query)
        {
            string itemPath = item.Tag?.ToString() ?? "";
            string itemName = Path.GetFileName(itemPath).ToLower();
            
            // Check if current item matches
            if (itemName.Contains(query))
            {
                item.IsSelected = true;
                item.BringIntoView();
                
                // Expand parent items only
                TreeViewItem? parent = FindVisualParent<TreeViewItem>(item);
                while (parent != null)
                {
                    parent.IsExpanded = true;
                    parent = FindVisualParent<TreeViewItem>(parent);
                }
                
                return true;
            }
            
            // Search children without expanding this item first
            bool foundInChildren = false;
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem && SearchTreeViewItem(childItem, query))
                {
                    foundInChildren = true;
                    item.IsExpanded = true; // Only expand if a child matched
                    break;
                }
            }
            
            return foundInChildren;
        }

        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to bookmark",
                ShowNewFolderButton = false
            };
            
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                if (!_bookmarkedFolders.Contains(selectedPath))
                {
                    _bookmarkedFolders.Add(selectedPath);
                    SaveBookmarks();
                    LoadScriptTree();
                }
            }
        }

        private void ToggleBookmark(string folderPath)
        {
            if (_bookmarkedFolders.Contains(folderPath))
            {
                _bookmarkedFolders.Remove(folderPath);
            }
            else
            {
                _bookmarkedFolders.Add(folderPath);
            }
            SaveBookmarks();
            LoadScriptTree();
        }

        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptTree.SelectedItem is TreeViewItem selectedItem)
            {
                string path = selectedItem.Tag?.ToString() ?? "";
                if (!string.IsNullOrEmpty(path))
                {
                    if (File.Exists(path))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else if (Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                    }
                }
            }
        }

        private bool ShowConfirmDialog(string title, string message)
        {
            Window w = new Window { Width = 350, Height = 180, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Foreground = Brushes.White, BorderThickness = new Thickness(1), Topmost = true };
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
            w.BorderBrush = gradient;

            StackPanel s = new StackPanel { Margin = new Thickness(15) };
            TextBlock titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), Margin = new Thickness(0, 0, 0, 15) };
            s.Children.Add(titleBlock);
            TextBlock msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            s.Children.Add(msg);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bool result = false;
            Button yes = new Button { Content = "Yes", Width = 70, Height = 30, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
            yes.Click += (a, b) => { result = true; w.Close(); };
            Button no = new Button { Content = "No", Width = 70, Height = 30, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
            no.Click += (a, b) => w.Close();
            btns.Children.Add(yes); btns.Children.Add(no);
            s.Children.Add(btns);
            w.Content = s;
            w.ShowDialog();
            return result;
        }

        private (bool confirmed, bool dontAskAgain) ShowConfirmDialogWithCheckbox(string title, string message, string checkboxText)
        {
            Window w = new Window { Width = 350, Height = 220, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Foreground = Brushes.White, BorderThickness = new Thickness(1), Topmost = true };
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 217, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(123, 104, 238), 1));
            w.BorderBrush = gradient;

            StackPanel s = new StackPanel { Margin = new Thickness(15) };
            TextBlock titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), Margin = new Thickness(0, 0, 0, 15) };
            s.Children.Add(titleBlock);
            TextBlock msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) };
            s.Children.Add(msg);
            
            CheckBox checkbox = new CheckBox { Content = checkboxText, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20), IsChecked = false };
            s.Children.Add(checkbox);
            
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bool result = false;
            Button yes = new Button { Content = "Yes", Width = 70, Height = 30, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
            yes.Click += (a, b) => { result = true; w.Close(); };
            Button no = new Button { Content = "No", Width = 70, Height = 30, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
            no.Click += (a, b) => w.Close();
            btns.Children.Add(yes); btns.Children.Add(no);
            s.Children.Add(btns);
            w.Content = s;
            w.ShowDialog();
            return (result, checkbox.IsChecked == true);
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
                    string singleLineCommand = command.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                    byte[] data = Encoding.UTF8.GetBytes(singleLineCommand + "\n");
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
        
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Temp save all unsaved scripts
            TempSaveAllScripts();
        }
        
        private void TempSaveAllScripts()
        {
            foreach (TabItem tab in ScriptTabs.Items)
            {
                if (tab.Content is TextEditor editor)
                {
                    string tabName = "";
                    if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
                        tabName = tb.Text;
                    
                    // Save temp file
                    string tempFile = Path.Combine(_tempSavePath, $"{Guid.NewGuid()}.lua");
                    File.WriteAllText(tempFile, editor.Text);
                }
            }
        }
        
        private void SaveSession()
        {
            try
            {
                var session = new List<Dictionary<string, string>>();
                
                foreach (TabItem tab in ScriptTabs.Items)
                {
                    var tabInfo = new Dictionary<string, string>();
                    
                    if (tab.Header is StackPanel sp && sp.Children[0] is TextBlock tb)
                        tabInfo["Title"] = tb.Text;
                    
                    if (tab.Tag is string filePath)
                        tabInfo["FilePath"] = filePath;
                    
                    if (tab.Content is TextEditor editor)
                    {
                        // Save temp content
                        string tempFile = Path.Combine(_tempSavePath, $"{Guid.NewGuid()}.lua");
                        File.WriteAllText(tempFile, editor.Text);
                        tabInfo["TempFile"] = tempFile;
                    }
                    
                    session.Add(tabInfo);
                }
                
                string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sessionPath, json);
            }
            catch { }
        }
        
        private void RestoreSession()
        {
            try
            {
                if (!File.Exists(_sessionPath))
                {
                    AddNewTab(); // No session, create default tab
                    return;
                }
                
                string json = File.ReadAllText(_sessionPath);
                var session = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                
                if (session == null || session.Count == 0)
                {
                    AddNewTab();
                    return;
                }
                
                foreach (var tabInfo in session)
                {
                    string title = tabInfo.ContainsKey("Title") ? tabInfo["Title"] : null;
                    string filePath = tabInfo.ContainsKey("FilePath") ? tabInfo["FilePath"] : null;
                    string tempFile = tabInfo.ContainsKey("TempFile") ? tabInfo["TempFile"] : null;
                    
                    string content = "";
                    
                    // Load content from temp file if it exists
                    if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                    {
                        content = File.ReadAllText(tempFile);
                        File.Delete(tempFile); // Clean up temp file
                    }
                    else if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        content = File.ReadAllText(filePath);
                    }
                    
                    AddNewTab(title, content, filePath);
                }
            }
            catch
            {
                AddNewTab(); // Error loading session, create default tab
            }
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
            Window w = new Window { Width = 300, Height = 150, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)), WindowStartupLocation = WindowStartupLocation.CenterScreen, Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(60,60,60)), BorderThickness = new Thickness(1), Topmost = true };
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
    
    public class HexColorHighlighter : DocumentColorizingTransformer
    {
        private static readonly Regex HexColorRegex = new Regex(@"0x([0-9A-Fa-f]{6})\b", RegexOptions.Compiled);

        protected override void ColorizeLine(DocumentLine line)
        {
            int lineStartOffset = line.Offset;
            string text = CurrentContext.Document.GetText(line);
            
            foreach (Match match in HexColorRegex.Matches(text))
            {
                string hexValue = match.Groups[1].Value;
                
                // Parse hex color (format: RRGGBB)
                byte r = Convert.ToByte(hexValue.Substring(0, 2), 16);
                byte g = Convert.ToByte(hexValue.Substring(2, 2), 16);
                byte b = Convert.ToByte(hexValue.Substring(4, 2), 16);
                
                Color color = Color.FromRgb(r, g, b);
                
                // Calculate brightness to determine if we should use black or white text
                double brightness = (r * 0.299 + g * 0.587 + b * 0.114) / 255;
                Color textColor = brightness > 0.5 ? Colors.Black : Colors.White;
                
                ChangeLinePart(
                    lineStartOffset + match.Index,
                    lineStartOffset + match.Index + match.Length,
                    element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(color));
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(textColor));
                    }
                );
            }
        }
    }
}