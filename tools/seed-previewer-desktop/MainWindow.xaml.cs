using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SeedPreviewer.Desktop;

public partial class MainWindow : Window
{
    private CancellationTokenSource? job;
    private string? imagePath;
    private string? resultDirectory;
    private string imageFileName = "map.png";
    private bool closeAfterJob;
    private bool panning;
    private double fitScale = 1;
    private double scale = 1;
    private Vector offset;
    private Point panStart;
    private Vector panOffset;

    public MainWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = new[] { "Continents", "Pangaea", "Swirls", "Vanillaish" };
        PresetBox.SelectedIndex = 0;
    }

    private void RandomSeedClicked(object sender, RoutedEventArgs e)
    {
        // World.GenerateSeed draws ten times from this alphabet, with replacement.
        const string alphabet = "abcdefghijklmnpqrstuvwxyzABCDEFGHIJKLMNPQRSTUVWXYZ023456789";
        SeedBox.Text = string.Create(10, alphabet, static (characters, allowed) =>
        {
            for (int i = 0; i < characters.Length; i++)
                characters[i] = allowed[Random.Shared.Next(allowed.Length)];
        });
    }

    private async void GenerateClicked(object sender, RoutedEventArgs e)
    {
        if (job != null)
            return;
        string seed = SeedBox.Text;
        if (!Regex.IsMatch(seed, @"\A[A-Za-z0-9]{1,10}\z", RegexOptions.CultureInvariant))
        {
            StatusText.Text = "Escribe una semilla de 1 a 10 letras o números.";
            SeedBox.Focus();
            return;
        }
        string preset = (string)PresetBox.SelectedItem;
        int resolution = int.Parse((string)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
            System.Globalization.CultureInfo.InvariantCulture);
        using var current = new CancellationTokenSource();
        job = current;
        SetGenerating(true);
        StatusText.Text = "Preparando generación";
        var clock = Stopwatch.StartNew();
        var progress = new Progress<string>(value =>
        {
            if (job == current && !current.IsCancellationRequested)
                StatusText.Text = value;
        });
        try
        {
            string tool = PreviewJob.FindToolDirectory();
            resultDirectory = Path.Combine(tool, ".local", "runs", Guid.NewGuid().ToString("N"));
            var result = await PreviewJob.RunAsync(tool, resultDirectory, preset, seed, resolution,
                progress, current.Token);
            ShowMap(result, preset, seed, resolution);
            StatusText.Text = $"Mapa listo · {resolution} × {resolution} · {clock.Elapsed.TotalSeconds:N1} s";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Generación cancelada.";
        }
        catch (Exception error)
        {
            StatusText.Text = error is Win32Exception
                ? "No se pudo iniciar PowerShell 7. Comprueba que pwsh esté disponible."
                : error.Message;
        }
        finally
        {
            job = null;
            SetGenerating(false);
            if (closeAfterJob)
                Close();
        }
    }

    private void ShowMap(PreviewResult result, string preset, string seed, int resolution)
    {
        var bitmap = new BitmapImage();
        using (var source = File.OpenRead(result.ImagePath))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = source;
            bitmap.EndInit();
        }
        if (bitmap.PixelWidth != resolution || bitmap.PixelHeight != resolution)
            throw new InvalidDataException("El PNG tiene una resolución inesperada.");

        using var document = JsonDocument.Parse(File.ReadAllText(result.LegendPath));
        var legend = document.RootElement.GetProperty("palette").EnumerateArray().Select(item =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.GetProperty("color").GetString()!)!);
            return new LegendItem(BiomeLabel(item.GetProperty("id").GetInt32()), brush);
        }).ToArray();

        imagePath = result.ImagePath;
        imageFileName = $"{preset}-{seed}-{resolution}-A.png";
        LegendList.ItemsSource = legend;
        LegendHint.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ViewportHelp.Visibility = Visibility.Visible;
        MapImage.Source = bitmap;
        MapImage.Width = bitmap.PixelWidth;
        MapImage.Height = bitmap.PixelHeight;
        MapTitle.Text = $"{preset} · {seed}";
        SaveButton.IsEnabled = true;
        FitButton.IsEnabled = true;
        FitMap();
    }

    private static string BiomeLabel(int id) => id switch
    {
        1 => "Praderas",
        2 => "Pantano",
        4 => "Montañas",
        8 => "Bosque Negro",
        16 => "Llanuras",
        32 => "Ashlands",
        64 => "Norte Profundo",
        256 => "Océano",
        512 => "Mistlands",
        _ => $"Bioma {id}"
    };

    private sealed record LegendItem(string Label, Brush Swatch);

    private void SetGenerating(bool active)
    {
        GenerateButton.IsEnabled = SeedBox.IsEnabled = PresetBox.IsEnabled = ResolutionBox.IsEnabled = !active;
        RandomSeedButton.IsEnabled = !active;
        CancelButton.Visibility = ProgressBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = active;
        FolderButton.IsEnabled = !active && Directory.Exists(resultDirectory);
    }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        job?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelando generación";
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (job == null)
            return;
        e.Cancel = true;
        closeAfterJob = true;
        job.Cancel();
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (imagePath == null)
            return;
        var dialog = new SaveFileDialog { FileName = imageFileName, Filter = "Imagen PNG (*.png)|*.png", DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.Copy(imagePath, dialog.FileName, overwrite: true);
            StatusText.Text = "PNG guardado.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "No se pudo guardar el PNG en esa ubicación.";
        }
    }

    private void OpenResultsClicked(object sender, RoutedEventArgs e)
    {
        if (resultDirectory == null || !Directory.Exists(resultDirectory))
            return;
        try { Process.Start(new ProcessStartInfo(resultDirectory) { UseShellExecute = true }); }
        catch (Win32Exception) { StatusText.Text = "No se pudo abrir la carpeta de resultados."; }
    }

    private void ViewportResized(object sender, SizeChangedEventArgs e) => FitMap();
    private void FitClicked(object sender, RoutedEventArgs e) => FitMap();

    private void FitMap()
    {
        if (MapImage?.Source == null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
            return;
        fitScale = Math.Min(Viewport.ActualWidth / MapImage.Width, Viewport.ActualHeight / MapImage.Height);
        scale = fitScale;
        offset = new Vector((Viewport.ActualWidth - MapImage.Width * scale) / 2,
            (Viewport.ActualHeight - MapImage.Height * scale) / 2);
        TransformMap();
    }

    private void ZoomMap(object sender, MouseWheelEventArgs e)
    {
        if (MapImage.Source == null)
            return;
        Point anchor = e.GetPosition(Viewport);
        double next = Math.Clamp(scale * Math.Pow(1.2, e.Delta / 120d), fitScale, fitScale * 12);
        Vector position = new(anchor.X, anchor.Y);
        offset = position - (position - offset) * (next / scale);
        scale = next;
        TransformMap();
        e.Handled = true;
    }

    private void StartPan(object sender, MouseButtonEventArgs e)
    {
        if (MapImage.Source == null || !Viewport.CaptureMouse())
            return;
        panning = true;
        panStart = e.GetPosition(Viewport);
        panOffset = offset;
        Viewport.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void MovePan(object sender, MouseEventArgs e)
    {
        if (!panning)
            return;
        offset = panOffset + (e.GetPosition(Viewport) - panStart);
        TransformMap();
    }

    private void EndPan(object sender, MouseButtonEventArgs e) => Viewport.ReleaseMouseCapture();

    private void PanLostCapture(object sender, MouseEventArgs e)
    {
        panning = false;
        Viewport.Cursor = Cursors.Arrow;
    }

    private void TransformMap()
    {
        MapImage.RenderTransform = new MatrixTransform(scale, 0, 0, scale, offset.X, offset.Y);
        ZoomLabel.Text = $"{scale / fitScale:P0}";
    }
}
