using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace ExifCleaner
{
    public partial class MainWindow : Window
    {
        private string? _currentImagePath;
        private BitmapImage? _currentBitmap;
        private readonly ObservableCollection<KeyValuePair<string, string>> _metaItems = new();

        public MainWindow()
        {
            InitializeComponent();
            BtnOpen.Click += BtnOpen_Click;
            BtnClean.Click += BtnClean_Click;
            MetadataList.ItemsSource = _metaItems;
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Vyber obrázek",
                Filter = "Obrázky|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Všechny soubory|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    LoadImage(ofd.FileName);
                    LoadAndShowMetadata(ofd.FileName);
                    StatusText.Text = $"Načteno: {System.IO.Path.GetFileName(ofd.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Chyba při načítání souboru.";
                    MessageBox.Show(this, ex.Message, "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadImage(string path)
        {
            var bi = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bi.StreamSource = fs;
                bi.EndInit();
                bi.Freeze();
            }

            _currentImagePath = path;
            _currentBitmap = bi;
            ImagePreview.Source = bi;
        }

        private void LoadAndShowMetadata(string path)
        {
            _metaItems.Clear();

            try
            {
                var directories = ImageMetadataReader.ReadMetadata(path);

                foreach (var dir in directories)
                {
                    foreach (var tag in dir.Tags)
                    {
                        _metaItems.Add(new(tag.Name, tag.Description ?? ""));
                    }
                }

                if (_metaItems.Count == 0)
                    _metaItems.Add(new("Info", "Nebyla nalezena žádná metadata."));
            }
            catch (Exception ex)
            {
                _metaItems.Add(new("Chyba", ex.Message));
            }
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null || _currentImagePath == null)
            {
                MessageBox.Show(this, "Nejprve otevři obrázek.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                BtnClean.IsEnabled = false;
                StatusText.Text = "Probíhá čištění…";

                BitmapSource src = _currentBitmap;

                if (ChkNoise.IsChecked == true)
                {
                    src = AddSubtleNoise(src);
                }

                string ext = System.IO.Path.GetExtension(_currentImagePath).ToLowerInvariant();
                string outPath = GetCleanOutputPath(_currentImagePath);

                BitmapSource forSave = src;
                BitmapEncoder encoder = ext switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                    ".png" => new PngBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    ".tif" or ".tiff" => new TiffBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                if (encoder is JpegBitmapEncoder)
                {
                    if (forSave.Format != PixelFormats.Bgr24)
                    {
                        var conv = new FormatConvertedBitmap(forSave, PixelFormats.Bgr24, null, 0);
                        conv.Freeze();
                        forSave = conv;
                    }
                }

                var cleanFrame = BitmapFrame.Create(forSave, null, null, null);
                encoder.Frames.Add(cleanFrame);

                using (var fs = File.Create(outPath))
                {
                    encoder.Save(fs);
                }

                StatusText.Text = $"Uloženo: {System.IO.Path.GetFileName(outPath)}";
                LoadImage(outPath);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Čištění selhalo.";
                MessageBox.Show(this, ex.Message, "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnClean.IsEnabled = true;
            }
        }

        private static string GetCleanOutputPath(string inputPath)
        {
            var dir = Path.GetDirectoryName(inputPath)!;
            var file = Path.GetFileNameWithoutExtension(inputPath);
            var ext = Path.GetExtension(inputPath);
            string candidate = Path.Combine(dir, $"{file}_clean{ext}");

            int i = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{file}_clean({i}){ext}");
                i++;
            }
            return candidate;
        }

        private static BitmapSource AddSubtleNoise(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
            {
                var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                source = conv;
            }

            var wb = new WriteableBitmap(source);
            int width = wb.PixelWidth;
            int height = wb.PixelHeight;
            int bpp = wb.Format.BitsPerPixel; // 32
            int stride = (width * bpp + 7) / 8;

            var buffer = new byte[height * stride];
            wb.CopyPixels(buffer, stride, 0);

            var rnd = Random.Shared;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4; // BGRA

                    buffer[i + 0] = (byte)((buffer[i + 0] & 0xFE) | rnd.Next(2)); // B
                    buffer[i + 1] = (byte)((buffer[i + 1] & 0xFE) | rnd.Next(2)); // G
                    buffer[i + 2] = (byte)((buffer[i + 2] & 0xFE) | rnd.Next(2)); // R
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
            wb.Freeze();
            return wb;
        }
    }
}
