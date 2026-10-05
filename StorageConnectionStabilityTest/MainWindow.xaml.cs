using System.Collections.ObjectModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace StorageConnectionStabilityTest
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<DiskInfo> Disks { get; set; } = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadSystem();
            LoadDisks();
        }

        private void LoadSystem()
        {
            OsText.Text = RuntimeInformation.OSDescription;
        }

        private void LoadDisks()
        {
            Disks.Clear();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject d in searcher.Get())
                {
                    Disks.Add(new DiskInfo
                    {
                        Model = d["Model"]?.ToString(),
                        Interface = d["InterfaceType"]?.ToString(),
                        PNP = d["PNPDeviceID"]?.ToString(),
                        Device = d["DeviceID"]?.ToString()
                    });
                }

                DiskGrid.ItemsSource = Disks;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Disk bilgileri alınırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadSystem();
            LoadDisks();
        }

        private void DiskGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DiskGrid.SelectedItem is not DiskInfo d)
                return;

            DetailBox.Text = $@"Model          : {d.Model}
Bağlantı       : {d.Interface}
PNP Device ID  : {d.PNP}
Windows Device : {d.Device}";
        }
    }

    public class DiskInfo
    {
        public string? Model { get; set; }
        public string? Interface { get; set; }
        public string? PNP { get; set; }
        public string? Device { get; set; }
    }
}