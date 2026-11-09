using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VanBaz;

public sealed class ColorPickerDialog : FormDialog
{
    private readonly TextBox code;
    private readonly Border preview;
    private readonly Slider[] channels = new Slider[4];
    private bool updating;
    public string SelectedColorText => Format((Color)ColorConverter.ConvertFromString(code.Text.Trim()));

    public ColorPickerDialog(string initial, string label) : base("Выбор цвета · " + label, 530, 510)
    {
        Section("Цвет"); SetAcceptText("Выбрать");
        Color original; try { original = (Color)ColorConverter.ConvertFromString(initial); } catch { original = Colors.Black; }
        var samples = new Grid { Margin = new(0, 0, 0, 12) };
        samples.ColumnDefinitions.Add(new()); samples.ColumnDefinitions.Add(new());
        Border Sample(string title, Color color, int column)
        {
            var stack = new StackPanel { Margin = new(4) }; Grid.SetColumn(stack, column); samples.Children.Add(stack);
            stack.Children.Add(new TextBlock { Text = title, Margin = new(0, 0, 0, 5) });
            var sample = new Border { Height = 46, BorderThickness = new(1), BorderBrush = Brushes.Gray, Background = new SolidColorBrush(color) };
            stack.Children.Add(sample); return sample;
        }
        Sample("Было", original, 0); preview = Sample("Выбранный цвет", original, 1); Element(samples);
        var palette = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8, Margin = new(0, 0, 0, 12) };
        foreach (var hex in new[] { "#000000", "#404040", "#808080", "#C0C0C0", "#FFFFFF", "#800000", "#FF0000", "#FF8080", "#804000", "#FF8000", "#FFC000", "#FFFF00", "#808000", "#008000", "#00C000", "#80FF80", "#008080", "#00FFFF", "#0080FF", "#0000FF", "#000080", "#800080", "#C000C0", "#FF80FF" })
        {
            var button = new Button { Tag = hex, ToolTip = hex, Height = 30, Margin = new(2), Padding = new(3) };
            System.Windows.Automation.AutomationProperties.SetName(button, "Цвет " + hex);
            button.Content = new Border { Width = 32, Height = 18, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), BorderBrush = Brushes.Gray, BorderThickness = new(1) };
            button.Click += (_, _) => SetText("hex", hex);
            palette.Children.Add(button);
        }
        Element(palette);
        code = Text("hex", "Код цвета", Format(original)); code.MaxLength = 32;
        var names = new[] { "Красный", "Зелёный", "Синий", "Непрозрачность" };
        for (int i = 0; i < channels.Length; i++)
        {
            var row = new DockPanel { Margin = new(0, 5, 0, 5) };
            var caption = new TextBlock { Text = names[i], Width = 112, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
            var number = new TextBlock { Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(number, Dock.Right); row.Children.Add(number);
            var slider = channels[i] = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 16, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(slider, names[i]); number.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Value") { Source = slider, StringFormat = "0" }); row.Children.Add(slider);
            slider.ValueChanged += (_, _) => { if (!updating) code.Text = Format(Color.FromArgb((byte)channels[3].Value, (byte)channels[0].Value, (byte)channels[1].Value, (byte)channels[2].Value)); };
            Element(row);
        }
        void Update()
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(code.Text.Trim()); updating = true;
                preview.Background = new SolidColorBrush(color);
                channels[0].Value = color.R; channels[1].Value = color.G; channels[2].Value = color.B; channels[3].Value = color.A;
                SetError("");
            }
            catch { SetError("Выберите цвет из палитры или введите #RRGGBB / #AARRGGBB."); }
            finally { updating = false; }
        }
        code.TextChanged += (_, _) => Update(); Update();
        Validate = () => { try { _ = SelectedColorText; return true; } catch { return SetError("Неверный код цвета. Например: #287EB0."); } };
    }
    private static string Format(Color color) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
