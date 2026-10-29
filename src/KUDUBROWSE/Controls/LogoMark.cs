using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kudubrowse.Controls
{
    /// <summary>The KUDUBROWSE mark: a globe with a shield-shaped curve. Uses the text colour.</summary>
    public class LogoMark : Viewbox
    {
        public LogoMark(double size = 40)
        {
            Width = size;
            Height = size;
            Focusable = false;
            IsHitTestVisible = false;

            var canvas = new Canvas { Width = 64, Height = 64 };
            var globe = new Ellipse { Width = 44, Height = 44, StrokeThickness = 3.5 };
            Canvas.SetLeft(globe, 10);
            Canvas.SetTop(globe, 10);
            Bind(globe);
            canvas.Children.Add(globe);
            var meridian = new Ellipse { Width = 18, Height = 44, StrokeThickness = 3 };
            Canvas.SetLeft(meridian, 23);
            Canvas.SetTop(meridian, 10);
            Bind(meridian);
            canvas.Children.Add(meridian);
            foreach (var y in new[] { 25.0, 39.0 })
            {
                var line = new Line { X1 = 13, Y1 = y, X2 = 51, Y2 = y, StrokeThickness = 3 };
                Bind(line);
                canvas.Children.Add(line);
            }
            Child = canvas;
        }

        private void Bind(Shape shape) =>
            shape.SetBinding(Shape.StrokeProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), Source = this });
    }

    /// <summary>The app tile: the mark in white on a teal rounded square.</summary>
    public static class LogoTile
    {
        public static readonly Color Teal = Color.FromRgb(0x0E, 0x8C, 0x7E);

        public static Border Create(double size)
        {
            var mark = new LogoMark(size);
            mark.SetValue(TextElement.ForegroundProperty, Brushes.White);
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.234),
                Background = new SolidColorBrush(Teal),
                Child = mark
            };
        }
    }
}
