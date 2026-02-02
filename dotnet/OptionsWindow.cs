using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace SynapMc
{
    public partial class OptionsWindow : Window
    {
        public Dictionary<string, Color> UpdatedColors { get; private set; }
        public string UpdatedWorkspacePath { get; private set; }
        public bool AlwaysOnTop { get; private set; }

        public OptionsWindow(Dictionary<string, Color> currentColors, string currentWorkspacePath, bool currentAlwaysOnTop)
        {
            Width = 450;
            Height = 400;
            Title = "Options";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            Foreground = Brushes.White;
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66));
            Topmost = true;

            UpdatedColors = new Dictionary<string, Color>(currentColors);
            UpdatedWorkspacePath = currentWorkspacePath;
            AlwaysOnTop = currentAlwaysOnTop;

            Grid mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            TextBlock titleBlock = new TextBlock
            {
                Text = "Options",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(titleBlock, 0);
            mainGrid.Children.Add(titleBlock);

            // Main content area
            StackPanel mainPanel = new StackPanel();
            
            // Always on Top Checkbox
            CheckBox alwaysOnTopCheckBox = new CheckBox
            {
                Content = "Always on Top",
                IsChecked = AlwaysOnTop,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15),
                FontSize = 14
            };
            alwaysOnTopCheckBox.Checked += (s, e) => AlwaysOnTop = true;
            alwaysOnTopCheckBox.Unchecked += (s, e) => AlwaysOnTop = false;
            mainPanel.Children.Add(alwaysOnTopCheckBox);
            
            // Workspace Directory Button
            Button workspaceBtn = new Button
            {
                Content = "Workspace Directory",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 15),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            workspaceBtn.Click += (s, e) => ShowWorkspaceSettings();
            mainPanel.Children.Add(workspaceBtn);
            
            // UI Settings Button
            Button uiSettingsBtn = new Button
            {
                Content = "UI Settings",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 15),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            uiSettingsBtn.Click += (s, e) => ShowUISettings();
            mainPanel.Children.Add(uiSettingsBtn);

            // Global Button
            Button globalBtn = new Button
            {
                Content = "Global",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 15),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            globalBtn.Click += (s, e) => OpenGlobalFile();
            mainPanel.Children.Add(globalBtn);

            Grid.SetRow(mainPanel, 1);
            mainGrid.Children.Add(mainPanel);

            // Buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button saveBtn = new Button
            {
                Content = "Save",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            saveBtn.Click += (s, e) => { DialogResult = true; Close(); };

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
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(saveBtn);
            buttonPanel.Children.Add(cancelBtn);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private void ShowWorkspaceSettings()
        {
            Window workspaceWindow = new Window
            {
                Width = 500,
                Height = 200,
                Title = "Workspace Directory",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Topmost = true
            };

            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Workspace Directory",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            TextBlock label = new TextBlock
            {
                Text = "Current Path:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(label, 1);
            grid.Children.Add(label);

            TextBox pathTextBox = new TextBox
            {
                Text = UpdatedWorkspacePath,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 10),
                IsReadOnly = true
            };
            Grid.SetRow(pathTextBox, 2);
            grid.Children.Add(pathTextBox);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button browseBtn = new Button
            {
                Content = "Browse...",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            browseBtn.Click += (s, e) =>
            {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select Workspace Directory",
                    SelectedPath = UpdatedWorkspacePath,
                    ShowNewFolderButton = true
                };
                
                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    UpdatedWorkspacePath = folderDialog.SelectedPath;
                    pathTextBox.Text = UpdatedWorkspacePath;
                }
            };

            Button okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            okBtn.Click += (s, e) => workspaceWindow.Close();

            buttonPanel.Children.Add(browseBtn);
            buttonPanel.Children.Add(okBtn);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            workspaceWindow.Content = grid;
            workspaceWindow.ShowDialog();
        }

        private void ShowUISettings()
        {
            Window uiWindow = new Window
            {
                Width = 450,
                Height = 500,
                Title = "UI Settings",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                Topmost = true
            };

            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "UI Color Settings",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 15)
            };

            StackPanel colorPanel = new StackPanel();
            colorPanel.Children.Add(CreateColorPicker("Background Color:", "Background"));
            colorPanel.Children.Add(CreateColorPicker("Border Color:", "Border"));
            colorPanel.Children.Add(CreateColorPicker("Accent Color:", "Accent"));
            colorPanel.Children.Add(CreateColorPicker("Editor Background:", "EditorBackground"));
            colorPanel.Children.Add(CreateColorPicker("Control Background:", "ControlBackground"));

            scrollViewer.Content = colorPanel;
            Grid.SetRow(scrollViewer, 1);
            grid.Children.Add(scrollViewer);

            Button okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            okBtn.Click += (s, e) => uiWindow.Close();
            Grid.SetRow(okBtn, 2);
            grid.Children.Add(okBtn);

            uiWindow.Content = grid;
            uiWindow.ShowDialog();
        }

        private StackPanel CreateColorPicker(string label, string colorKey)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            TextBlock lbl = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) };
            panel.Children.Add(lbl);

            Grid colorGrid = new Grid();
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border colorPreview = new Border
            {
                Width = 40,
                Height = 25,
                Background = new SolidColorBrush(UpdatedColors[colorKey]),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                CornerRadius = new CornerRadius(3)
            };
            Grid.SetColumn(colorPreview, 1);

            Button pickBtn = new Button
            {
                Content = "Pick Color",
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            };
            pickBtn.Click += (s, e) =>
            {
                var colorDialog = new ColorPickerDialog(UpdatedColors[colorKey]);
                if (colorDialog.ShowDialog() == true)
                {
                    UpdatedColors[colorKey] = colorDialog.SelectedColor;
                    colorPreview.Background = new SolidColorBrush(colorDialog.SelectedColor);
                }
            };
            Grid.SetColumn(pickBtn, 0);

            colorGrid.Children.Add(pickBtn);
            colorGrid.Children.Add(colorPreview);
            panel.Children.Add(colorGrid);

            return panel;
        }

        private void OpenGlobalFile()
        {
            string globalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".jsmacros", "scripts", "projects", "Synapmc", "global.json");
            
            if (!File.Exists(globalPath))
            {
                // Create default global.json
                Directory.CreateDirectory(Path.GetDirectoryName(globalPath));
                File.WriteAllText(globalPath, "[\n  \"myGlobal\",\n  \"anotherGlobal\"\n]");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = globalPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open global.json: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; }
        private ColorCanvas colorCanvas;

        public ColorPickerDialog(Color initialColor)
        {
            SelectedColor = initialColor;
            Width = 420;
            Height = 450;
            Title = "Pick Color";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            Foreground = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            BorderThickness = new Thickness(1);
            Topmost = true;

            Grid titleGrid = new Grid
            {
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
            };
            
            TextBlock titleText = new TextBlock
            {
                Text = "Pick Color",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brushes.White
            };
            titleGrid.Children.Add(titleText);

            Button closeBtn = new Button
            {
                Content = "✕",
                Width = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 14
            };
            closeBtn.Click += (s, e) => { DialogResult = false; Close(); };
            titleGrid.Children.Add(closeBtn);

            StackPanel mainPanel = new StackPanel { Margin = new Thickness(20) };

            // Color Canvas from Extended.Wpf.Toolkit
            colorCanvas = new ColorCanvas
            {
                SelectedColor = initialColor,
                Height = 280,
                Margin = new Thickness(0, 0, 0, 20),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50))
            };
            mainPanel.Children.Add(colorCanvas);

            StackPanel btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 0)
            };

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
            okBtn.Click += (s, e) => { SelectedColor = colorCanvas.SelectedColor ?? initialColor; DialogResult = true; Close(); };

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
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            mainPanel.Children.Add(btnPanel);

            Grid containerGrid = new Grid();
            containerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            containerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(titleGrid, 0);
            Grid.SetRow(mainPanel, 1);
            containerGrid.Children.Add(titleGrid);
            containerGrid.Children.Add(mainPanel);

            Content = containerGrid;
        }
    }
}
