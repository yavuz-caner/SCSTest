using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace StorageConnectionStabilityTest;

public partial class MainWindow : Window
{
    private List<DiskInfo> Disks = new();
    private CancellationTokenSource? TestCancellation;
    private HwndSource? WindowSource;

    private bool DeviceMonitoringEnabled = false;

    private readonly List<string> DeviceEvents = new();

    public MainWindow()
    {
        InitializeComponent();

        LoadSystem();
        LoadDisks();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowSource =
            PresentationSource.FromVisual(this) as HwndSource;

        if (WindowSource != null)
        {
            WindowSource.AddHook(WndProc);
            DeviceMonitoringEnabled = true;
        }

        StatusText.Text = "Hazır";
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (WindowSource != null)
        {
            WindowSource.RemoveHook(WndProc);
            WindowSource = null;
        }

        DeviceMonitoringEnabled = false;
    }

    private IntPtr WndProc(
    IntPtr hwnd,
    int msg,
    IntPtr wParam,
    IntPtr lParam,
    ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219;

        const int DBT_DEVICEARRIVAL = 0x8000;
        const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        if (msg == WM_DEVICECHANGE)
        {
            int eventCode = wParam.ToInt32();

            if (eventCode == DBT_DEVICEARRIVAL)
            {
                RegisterDeviceEvent(
                    "Windows cihaz ekleme olayı algılandı.");
            }
            else if (eventCode == DBT_DEVICEREMOVECOMPLETE)
            {
                RegisterDeviceEvent(
                    "Windows cihaz çıkarma olayı algılandı.");
            }
        }

        return IntPtr.Zero;
    }

    private void RegisterDeviceEvent(string message)
    {
        if (!DeviceMonitoringEnabled)
            return;

        string eventText =
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss.fff} - {message}";

        DeviceEvents.Add(eventText);

        // Test sırasında kullanıcıya göster.
        if (TestCancellation != null)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }
    }

    // ---------------------------------------------------------
    // WINDOWS / SİSTEM BİLGİSİ
    // ---------------------------------------------------------

    private void LoadSystem()
    {
        OsText.Text =
            $"İşletim Sistemi: {Environment.OSVersion}   |   " +
            $"64-bit: {Environment.Is64BitOperatingSystem}   |   " +
            $"Bilgisayar: {Environment.MachineName}";
    }


    // ---------------------------------------------------------
    // DİSKLERİ LİSTELE
    // ---------------------------------------------------------

    private void LoadDisks()
    {
        Disks.Clear();

        try
        {
            using var searcher =
                new ManagementObjectSearcher(
                    "SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get())
            {
                var info = new DiskInfo
                {
                    DeviceId = disk["DeviceID"]?.ToString() ?? "",
                    Model = disk["Model"]?.ToString() ?? "",
                    InterfaceType = disk["InterfaceType"]?.ToString() ?? "",
                    PNPDeviceID = disk["PNPDeviceID"]?.ToString() ?? "",
                    SerialNumber = disk["SerialNumber"]?.ToString() ?? ""
                };

                if (long.TryParse(
                    disk["Size"]?.ToString(),
                    out long size))
                {
                    info.SizeBytes = size;
                    info.SizeText = FormatSize(size);
                }

                info.DiskNumber = GetDiskNumber(info.DeviceId);

                info.VidPid = ExtractVidPid(info.PNPDeviceID);

                GetUsbDeviceInfo(
                    info.PNPDeviceID,
                    out string deviceName,
                    out string manufacturer,
                    out string service);

                info.DeviceName = deviceName;
                info.Manufacturer = manufacturer;
                info.Service = service;

                info.DriveLetter = GetDriveLetters(info.DeviceId);

                Disks.Add(info);
            }

            DiskGrid.ItemsSource = null;
            DiskGrid.ItemsSource = Disks;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Disk bilgileri alınırken hata oluştu:\n\n" +
                ex.Message,
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    // ---------------------------------------------------------
    // DİSK NUMARASI
    // ---------------------------------------------------------

    private int GetDiskNumber(string deviceId)
    {
        Match m = Regex.Match(
            deviceId,
            @"PHYSICALDRIVE(\d+)",
            RegexOptions.IgnoreCase);

        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out int number))
        {
            return number;
        }

        return -1;
    }


    // ---------------------------------------------------------
    // DİSK -> PARTITION -> DRIVE LETTER
    // ---------------------------------------------------------

    private string GetDriveLetters(string diskDeviceId)
    {
        var result = new List<string>();

        try
        {
            // Önce fiziksel diskin numarasını bul.
            Match match = Regex.Match(
                diskDeviceId,
                @"PHYSICALDRIVE(\d+)",
                RegexOptions.IgnoreCase);

            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, out int diskNumber))
            {
                return "";
            }

            // Windows'taki tüm Logical Diskleri dolaş.
            using var logicalSearcher =
                new ManagementObjectSearcher(
                    "SELECT DeviceID, VolumeName, DriveType " +
                    "FROM Win32_LogicalDisk");

            foreach (ManagementObject logicalDisk
                     in logicalSearcher.Get())
            {
                string drive =
                    logicalDisk["DeviceID"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(drive))
                    continue;

                // Bu sürücü harfinin bağlı olduğu partition'ı bul.
                string escapedDrive =
                    drive.Replace("\\", "\\\\");

                string partitionQuery =
                    $"ASSOCIATORS OF " +
                    $"{{Win32_LogicalDisk.DeviceID='{escapedDrive}'}} " +
                    "WHERE AssocClass=Win32_LogicalDiskToPartition";

                using var partitionSearcher =
                    new ManagementObjectSearcher(partitionQuery);

                foreach (ManagementObject partition
                         in partitionSearcher.Get())
                {
                    string partitionDevice =
                        partition["DeviceID"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(partitionDevice))
                        continue;

                    // Partition'ın hangi fiziksel diske bağlı olduğunu bul.
                    string escapedPartition =
                        partitionDevice.Replace("\\", "\\\\");

                    string diskQuery =
                        $"ASSOCIATORS OF " +
                        $"{{Win32_DiskPartition.DeviceID='{escapedPartition}'}} " +
                        "WHERE AssocClass=Win32_DiskDriveToDiskPartition";

                    using var diskSearcher =
                        new ManagementObjectSearcher(diskQuery);

                    foreach (ManagementObject physicalDisk
                             in diskSearcher.Get())
                    {
                        string physicalDevice =
                            physicalDisk["DeviceID"]?.ToString() ?? "";

                        if (string.IsNullOrWhiteSpace(physicalDevice))
                            continue;

                        Match diskMatch = Regex.Match(
                            physicalDevice,
                            @"PHYSICALDRIVE(\d+)",
                            RegexOptions.IgnoreCase);

                        if (!diskMatch.Success)
                            continue;

                        if (!int.TryParse(
                            diskMatch.Groups[1].Value,
                            out int foundDiskNumber))
                            continue;

                        if (foundDiskNumber == diskNumber)
                        {
                            result.Add(drive);
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            // WMI ilişkilendirmesi okunamazsa boş bırakılır.
        }

        return string.Join(", ", result);
    }


    // ---------------------------------------------------------
    // USB VID / PID
    // ---------------------------------------------------------

    private string ExtractVidPid(string pnp)
    {
        if (string.IsNullOrWhiteSpace(pnp))
            return "-";

        Match vid =
            Regex.Match(
                pnp,
                @"VID_([0-9A-F]{4})",
                RegexOptions.IgnoreCase);

        Match pid =
            Regex.Match(
                pnp,
                @"PID_([0-9A-F]{4})",
                RegexOptions.IgnoreCase);

        if (vid.Success && pid.Success)
        {
            return
                $"VID_{vid.Groups[1].Value.ToUpper()} / " +
                $"PID_{pid.Groups[1].Value.ToUpper()}";
        }

        return "-";
    }


    // ---------------------------------------------------------
    // WINDOWS PNP CİHAZ BİLGİSİ
    // ---------------------------------------------------------

    private void GetUsbDeviceInfo(
        string pnpDeviceId,
        out string deviceName,
        out string manufacturer,
        out string service)
    {
        deviceName = "-";
        manufacturer = "-";
        service = "-";

        if (string.IsNullOrWhiteSpace(pnpDeviceId))
            return;

        try
        {
            string escaped =
                pnpDeviceId.Replace("\\", "\\\\")
                           .Replace("'", "\\'");

            using var searcher =
                new ManagementObjectSearcher(
                    $"SELECT Name, Manufacturer, Service " +
                    $"FROM Win32_PnPEntity " +
                    $"WHERE PNPDeviceID='{escaped}'");

            foreach (ManagementObject obj in searcher.Get())
            {
                deviceName =
                    obj["Name"]?.ToString() ?? "-";

                manufacturer =
                    obj["Manufacturer"]?.ToString() ?? "-";

                service =
                    obj["Service"]?.ToString() ?? "-";

                break;
            }
        }
        catch
        {
        }
    }


    // ---------------------------------------------------------
    // DİSK SEÇİMİ
    // ---------------------------------------------------------

    private void DiskGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DiskGrid.SelectedItem is not DiskInfo d)
            return;

        DetailBox.Text =
$@"MODEL
{d.Model}

Kapasite
{d.SizeText}

Windows Disk
PhysicalDrive{d.DiskNumber}

Sürücü
{d.DriveLetter}

Arayüz
{d.InterfaceType}

USB / PNP
{d.PNPDeviceID}

VID / PID
{d.VidPid}

Windows Cihaz Adı
{d.DeviceName}

Üretici
{d.Manufacturer}

Windows Service
{d.Service}

Seri No
{d.SerialNumber}

NOT:
Kontrolcü/bridge çipi Windows tarafından doğrudan
tanımlanabiliyorsa cihaz adı ve PNP bilgisi üzerinden
rapora alınacaktır. Windows tarafından çip modeli
açıkça bildirilmezse tahmin yapılmayacaktır.";
    }


    // ---------------------------------------------------------
    // YAZMA TESTİ
    // ---------------------------------------------------------

    private async void StartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DiskGrid.SelectedItem is not DiskInfo disk)
        {
            MessageBox.Show(
                "Önce test yapılacak depolama birimini seçin.",
                "Uyarı",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(disk.DriveLetter))
        {
            MessageBox.Show(
                "Seçilen fiziksel diske bağlı bir sürücü harfi bulunamadı.",
                "Uyarı",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string drive =
            disk.DriveLetter
                .Split(',')[0]
                .Trim();

        if (!double.TryParse(
            SizeBox.Text.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double gb) ||
            gb <= 0)
        {
            MessageBox.Show(
                "Geçerli bir test miktarı girin.",
                "Uyarı",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        long targetBytes =
            (long)(gb * 1024 * 1024 * 1024);

        try
        {
            var driveInfo = new DriveInfo(drive);

            if (!driveInfo.IsReady)
            {
                MessageBox.Show(
                    "Seçilen sürücü hazır değil.",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            if (driveInfo.AvailableFreeSpace < targetBytes)
            {
                MessageBox.Show(
                    $"Test için yeterli boş alan yok.\n\n" +
                    $"Gerekli: {FormatSize(targetBytes)}\n" +
                    $"Boş alan: {FormatSize(driveInfo.AvailableFreeSpace)}",
                    "Yetersiz Alan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Disk Bilgisi Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        SetTestRunning(true);

        DeviceEvents.Clear();

        StatusText.Text = "Test başlatılıyor...";

        TestCancellation =
            new CancellationTokenSource();

        try
        {
             await Task.Run(() =>
                 RunWriteTestAsync(
                    disk,
                    drive,
                    targetBytes,
                    TestCancellation.Token));
        }
        finally
        {
            TestCancellation.Dispose();
            TestCancellation = null;

            SetTestRunning(false);
        }
    }


    // ---------------------------------------------------------
    // TEST MOTORU
    // ---------------------------------------------------------

    private async Task RunWriteTestAsync(
        DiskInfo disk,
        string drive,
        long targetBytes,
        CancellationToken token)
    {
        string root =
            drive.EndsWith("\\")
                ? drive
                : drive + "\\";

        string testDirectory =
            Path.Combine(root, "StorageConnectionStabilityTest");

        Directory.CreateDirectory(testDirectory);

        string fileName =
            $"SCS_Test_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bin";

        string testFile =
            Path.Combine(testDirectory, fileName);

        const int bufferSize =
            64 * 1024 * 1024; // 64 MB

        byte[] buffer =
            new byte[bufferSize];

        // Sıfır yerine tekrarlanabilir veri paterni
        // kullanıyoruz.
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] =
                (byte)((i * 31 + 17) & 0xFF);
        }

        long totalWritten = 0;

        var speeds = new List<double>();

        Stopwatch totalWatch =
            Stopwatch.StartNew();

        string resultStatus =
            "BİLİNMİYOR";

        string failureReason = "";

        DateTime? failureTime = null;

        int? win32Error = null;

        string? win32Message = null;

        DateTime startTime =
            DateTime.Now;

        Dispatcher.Invoke(() =>
        {
            StatusText.Text =
                "Test ediliyor – Veri yazılıyor...";

            TestProgress.Value = 0;
        });

        IntPtr handle =
            NativeMethods.CreateFile(
                testFile,
                NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ |
                NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.CREATE_ALWAYS,
                NativeMethods.FILE_ATTRIBUTE_NORMAL |
                NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN,
                IntPtr.Zero);

        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            int error =
                Marshal.GetLastWin32Error();

            resultStatus =
                "DOSYA AÇILAMADI";

            failureTime =
                DateTime.Now;

            win32Error = error;

            win32Message =
                new System.ComponentModel.Win32Exception(error)
                    .Message;

            failureReason =
                "Test dosyası oluşturulamadı.";

            string finalReport =
                BuildReport(
                    disk,
                    drive,
                    targetBytes,
                    startTime,
                    DateTime.Now,
                    totalWritten,
                    speeds,
                    resultStatus,
                    failureReason,
                    failureTime,
                    win32Error,
                    win32Message,
                    testFile);

            SaveReport(finalReport);

            SummaryBox.Text =
                $"SONUÇ: Test dosyası oluşturulamadı. " +
                $"Windows hata kodu: {error}.";

            return;
        }

        try
        {
            while (totalWritten < targetBytes)
            {
                token.ThrowIfCancellationRequested();

                int toWrite =
                    (int)Math.Min(
                        bufferSize,
                        targetBytes - totalWritten);

                Stopwatch chunkWatch =
                    Stopwatch.StartNew();

                bool success =
                    NativeMethods.WriteFile(
                        handle,
                        buffer,
                        toWrite,
                        out uint written,
                        IntPtr.Zero);

                chunkWatch.Stop();

                if (!success ||
                    written != toWrite)
                {
                    int error =
                        Marshal.GetLastWin32Error();

                    failureTime =
                        DateTime.Now;

                    win32Error = error;

                    win32Message =
                        new System.ComponentModel.Win32Exception(error)
                            .Message;

                    failureReason =
                        "Dosya yazma işlemi sırasında Windows I/O hatası oluştu.";

                    resultStatus =
                        "BAĞLANTI / I/O HATASI";

                    break;
                }

                totalWritten += written;

                double seconds =
                    Math.Max(
                        chunkWatch.Elapsed.TotalSeconds,
                        0.000001);

                double speedMB =
                    written / 1024d / 1024d / seconds;

                speeds.Add(speedMB);

                // Her bloktan sonra diske flush.
                bool flushOk =
                    NativeMethods.FlushFileBuffers(handle);

                if (!flushOk)
                {
                    int error =
                        Marshal.GetLastWin32Error();

                    failureTime =
                        DateTime.Now;

                    win32Error = error;

                    win32Message =
                        new System.ComponentModel.Win32Exception(error)
                            .Message;

                    failureReason =
                        "FlushFileBuffers sırasında Windows I/O hatası oluştu.";

                    resultStatus =
                        "BAĞLANTI / I/O HATASI";

                    break;
                }

                double progress =
                    targetBytes == 0
                        ? 0
                        : totalWritten * 100d /
                          targetBytes;

                Dispatcher.Invoke(() =>
                {
                    TestProgress.Value =
                        Math.Min(100, progress);

                    StatusText.Text =
    $"Test ediliyor – Veri yazılıyor: " +
    $"{FormatSize(totalWritten)} / " +
    $"{FormatSize(targetBytes)}   " +
    $"{progress:0.0}%   " +
    $"Anlık: {speedMB:0.0} MB/s";
                });
            }

            if (resultStatus == "BİLİNMİYOR")
            {
                resultStatus =
                    totalWritten >= targetBytes
                        ? "BAŞARILI"
                        : "TAMAMLANMADI";
            }
        }
        catch (OperationCanceledException)
        {
            resultStatus =
                "KULLANICI TARAFINDAN DURDURULDU";

            failureReason =
                "Test kullanıcı tarafından durduruldu.";
        }
        catch (Exception ex)
        {
            resultStatus =
                "BEKLENMEYEN HATA";

            failureReason =
                ex.Message;

            failureTime =
                DateTime.Now;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);

            totalWatch.Stop();
        }

        DateTime endTime =
            DateTime.Now;

        // Test dosyasını temizlemeye çalış.
        bool deleteSuccess = false;

        try
        {
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }

            deleteSuccess = true;

            if (Directory.Exists(testDirectory) &&
                Directory.GetFiles(testDirectory).Length == 0)
            {
                Directory.Delete(testDirectory);
            }
        }
        catch
        {
            // Rapor içerisinde belirtilir.
        }

        string report =
            BuildReport(
                disk,
                drive,
                targetBytes,
                startTime,
                endTime,
                totalWritten,
                speeds,
                resultStatus,
                failureReason,
                failureTime,
                win32Error,
                win32Message,
                testFile);

        report +=
            $"\r\nTest dosyası temizliği: " +
            (deleteSuccess
                ? "Başarılı"
                : "Başarısız / dosya kalmış olabilir.");

        SaveReport(report);

        string summary =
            BuildShortSummary(
                drive,
                targetBytes,
                totalWritten,
                resultStatus,
                failureTime,
                win32Error,
                win32Message);

        Dispatcher.Invoke(() =>
        {
            TestProgress.Value =
                resultStatus == "BAŞARILI"
                    ? 100
                    : TestProgress.Value;

            if (resultStatus == "BAŞARILI")
            {
                StatusText.Text =
                    "Yazma testi tamamlandı";
            }
            else if (resultStatus == "KULLANICI TARAFINDAN DURDURULDU")
            {
                StatusText.Text =
                    "Test kullanıcı tarafından durduruldu";
            }
            else if (resultStatus == "BAĞLANTI / I/O HATASI")
            {
                StatusText.Text =
                    "Test durduruldu – Bağlantı / I/O hatası";
            }
            else
            {
                StatusText.Text =
                    $"Test tamamlandı – {resultStatus}";
            }

            SummaryBox.Text =
                summary;

            DetailBox.Text =
                BuildDeviceDetail(disk);
        });
    }


    // ---------------------------------------------------------
    // RAPOR
    // ---------------------------------------------------------

    private string BuildReport(
        DiskInfo disk,
        string drive,
        long targetBytes,
        DateTime start,
        DateTime end,
        long totalWritten,
        List<double> speeds,
        string status,
        string failureReason,
        DateTime? failureTime,
        int? errorCode,
        string? errorMessage,
        string testFile)
    {
        double average =
            speeds.Count > 0
                ? Average(speeds)
                : 0;

        double minimum =
            speeds.Count > 0
                ? Min(speeds)
                : 0;

        double maximum =
            speeds.Count > 0
                ? Max(speeds)
                : 0;

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "STORAGE CONNECTION STABILITY TEST RAPORU");

        sb.AppendLine(
            "========================================");

        sb.AppendLine();

        sb.AppendLine(
            $"Rapor tarihi       : {DateTime.Now:dd.MM.yyyy HH:mm:ss.fff}");

        sb.AppendLine(
            $"Test başlangıcı    : {start:dd.MM.yyyy HH:mm:ss.fff}");

        sb.AppendLine(
            $"Test bitişi        : {end:dd.MM.yyyy HH:mm:ss.fff}");

        sb.AppendLine(
            $"Test süresi        : {(end - start)}");

        sb.AppendLine();

        sb.AppendLine("İŞLETİM SİSTEMİ");
        sb.AppendLine("----------------");

        sb.AppendLine(
            $"OS                 : {Environment.OSVersion}");

        sb.AppendLine(
            $"64-bit              : {Environment.Is64BitOperatingSystem}");

        sb.AppendLine(
            $"Bilgisayar          : {Environment.MachineName}");

        sb.AppendLine();

        sb.AppendLine("TEST EDİLEN BİRİM");
        sb.AppendLine("-----------------");

        sb.AppendLine(
            $"Sürücü              : {drive}");

        sb.AppendLine(
            $"Disk                : PhysicalDrive{disk.DiskNumber}");

        sb.AppendLine(
            $"Model               : {disk.Model}");

        sb.AppendLine(
            $"Kapasite            : {disk.SizeText}");

        sb.AppendLine(
            $"Arayüz              : {disk.InterfaceType}");

        sb.AppendLine(
            $"Seri No             : {disk.SerialNumber}");

        sb.AppendLine();

        sb.AppendLine("KONTROLCÜ / USB BAĞLANTISI");
        sb.AppendLine("---------------------------");

        sb.AppendLine(
            $"VID / PID           : {disk.VidPid}");

        sb.AppendLine(
            $"Windows cihaz adı   : {disk.DeviceName}");

        sb.AppendLine(
            $"Üretici             : {disk.Manufacturer}");

        sb.AppendLine(
            $"Windows Service     : {disk.Service}");

        sb.AppendLine(
            $"PNP Device ID       : {disk.PNPDeviceID}");

        sb.AppendLine();

        sb.AppendLine("TEST YÖNTEMİ");
        sb.AppendLine("------------");

        sb.AppendLine(
            "Seçilen sürücü üzerinde gerçek bir dosya oluşturularak " +
            "ardışık yazma işlemi gerçekleştirilmiştir.");

        sb.AppendLine(
            "Her yazma bloğundan sonra FlushFileBuffers uygulanarak " +
            "Windows I/O sonucunun kontrol edilmesi sağlanmıştır.");

        sb.AppendLine(
            "Yazma işlemi sırasında Windows tarafından bildirilen " +
            "I/O hataları anında kaydedilmiştir.");

        sb.AppendLine(
            "Test, depolama medyasının sağlık durumunu belirlemek " +
            "amacıyla yapılmamıştır.");

        sb.AppendLine();

        sb.AppendLine("TEST PARAMETRELERİ");
        sb.AppendLine("------------------");

        sb.AppendLine(
            $"Hedef veri miktarı : {FormatSize(targetBytes)}");

        sb.AppendLine(
            $"Blok boyutu        : 64 MB");

        sb.AppendLine(
            $"Yazılan veri       : {FormatSize(totalWritten)}");

        sb.AppendLine();

        sb.AppendLine("HIZ SONUÇLARI");
        sb.AppendLine("-------------");

        sb.AppendLine(
            $"Minimum hız        : {minimum:0.00} MB/s");

        sb.AppendLine(
            $"Maksimum hız       : {maximum:0.00} MB/s");

        sb.AppendLine(
            $"Ortalama hız       : {average:0.00} MB/s");

        sb.AppendLine();

        sb.AppendLine("SONUÇ");
        sb.AppendLine("-----");

        sb.AppendLine(
            $"Durum              : {status}");

        if (failureTime.HasValue)
        {
            sb.AppendLine(
                $"Olay zamanı        : {failureTime.Value:dd.MM.yyyy HH:mm:ss.fff}");
        }

        if (errorCode.HasValue)
        {
            sb.AppendLine(
                $"Windows hata kodu  : {errorCode.Value}");

            sb.AppendLine(
                $"Windows hata mesajı: {errorMessage}");
        }

        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            sb.AppendLine(
                $"Açıklama           : {failureReason}");
        }

        sb.AppendLine();

        sb.AppendLine("TEST DOSYASI");
        sb.AppendLine("------------");

        sb.AppendLine(
            $"Dosya              : {testFile}");

        sb.AppendLine();

        sb.AppendLine("SERVİS FORMU İÇİN KISA SONUÇ");
        sb.AppendLine("-----------------------------");

        sb.AppendLine(
            BuildShortSummary(
                drive,
                targetBytes,
                totalWritten,
                status,
                failureTime,
                errorCode,
                errorMessage));

        return sb.ToString();
    }


    // ---------------------------------------------------------
    // KISA SERVİS FORMU SONUCU
    // ---------------------------------------------------------

    private string BuildShortSummary(
        string drive,
        long targetBytes,
        long written,
        string status,
        DateTime? failureTime,
        int? errorCode,
        string? errorMessage)
    {
        if (status == "BAŞARILI")
        {
            return
                $"Test: {FormatSize(targetBytes)} ardışık yazma testi. " +
                $"Sonuç: {FormatSize(written)} veri kesintisiz yazıldı, " +
                $"bağlantı/I/O kesintisi tespit edilmedi.";
        }

        if (status == "BAĞLANTI / I/O HATASI")
        {
            string time =
                failureTime.HasValue
                    ? failureTime.Value.ToString(
                        "dd.MM.yyyy HH:mm:ss.fff")
                    : "-";

            return
                $"Test: {FormatSize(targetBytes)} ardışık yazma testi. " +
                $"Sonuç: {FormatSize(written)} veri sonrasında " +
                $"bağlantı/I/O hatası tespit edildi. " +
                $"Olay: {time}, Windows hata kodu: {errorCode} " +
                $"({errorMessage}).";
        }

        if (status == "KULLANICI TARAFINDAN DURDURULDU")
        {
            return
                $"Test: {FormatSize(targetBytes)} ardışık yazma testi. " +
                $"Sonuç: Test kullanıcı tarafından {FormatSize(written)} " +
                $"veri yazıldıktan sonra durduruldu.";
        }

        return
            $"Test: {FormatSize(targetBytes)} ardışık yazma testi. " +
            $"Sonuç: {status}. Yazılan veri: {FormatSize(written)}.";
    }


    // ---------------------------------------------------------
    // RAPORU KAYDET
    // ---------------------------------------------------------

    private void SaveReport(string report)
    {
        try
        {
            string reportDirectory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Reports");

            Directory.CreateDirectory(reportDirectory);

            string file =
                Path.Combine(
                    reportDirectory,
                    $"SCS_Report_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");

            File.WriteAllText(
                file,
                report,
                Encoding.UTF8);

            Dispatcher.Invoke(() =>
            {
                StatusText.Text =
                    $"Rapor oluşturuldu: {Path.GetFileName(file)}";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text =
                    "Rapor kaydedilemedi: " + ex.Message;
            });
        }
    }


    // ---------------------------------------------------------
    // DETAY GÖRÜNTÜ
    // ---------------------------------------------------------

    private string BuildDeviceDetail(DiskInfo d)
    {
        return
$@"MODEL
{d.Model}

Kapasite
{d.SizeText}

Windows Disk
PhysicalDrive{d.DiskNumber}

Sürücü
{d.DriveLetter}

Arayüz
{d.InterfaceType}

USB / PNP
{d.PNPDeviceID}

VID / PID
{d.VidPid}

Windows Cihaz Adı
{d.DeviceName}

Üretici
{d.Manufacturer}

Windows Service
{d.Service}

Seri No
{d.SerialNumber}

Kontrolcü / bridge:
Windows tarafından doğrudan çip modeli bildiriliyorsa
cihaz adı / PNP bilgisi rapora alınır.
Model açıkça bildirilmiyorsa tahmin yapılmaz.";
    }


    // ---------------------------------------------------------
    // STOP
    // ---------------------------------------------------------

    private void StopButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        TestCancellation?.Cancel();
    }


    // ---------------------------------------------------------
    // YENİLE
    // ---------------------------------------------------------

    private void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TestCancellation != null)
            return;

        LoadSystem();
        LoadDisks();

        DetailBox.Clear();
        SummaryBox.Clear();

        StatusText.Text =
            "Bilgiler yenilendi.";
    }


    // ---------------------------------------------------------
    // BUTON DURUMLARI
    // ---------------------------------------------------------

    private void SetTestRunning(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        DiskGrid.IsEnabled = !running;
    }


    // ---------------------------------------------------------
    // YARDIMCI
    // ---------------------------------------------------------

    private static string FormatSize(long bytes)
    {
        double gb =
            bytes / 1024d / 1024d / 1024d;

        if (gb >= 1)
            return $"{gb:0.00} GB";

        double mb =
            bytes / 1024d / 1024d;

        return $"{mb:0.00} MB";
    }


    private static double Average(List<double> values)
    {
        double sum = 0;

        foreach (double value in values)
            sum += value;

        return values.Count == 0
            ? 0
            : sum / values.Count;
    }


    private static double Min(List<double> values)
    {
        double result = double.MaxValue;

        foreach (double value in values)
        {
            if (value < result)
                result = value;
        }

        return result == double.MaxValue
            ? 0
            : result;
    }


    private static double Max(List<double> values)
    {
        double result = 0;

        foreach (double value in values)
        {
            if (value > result)
                result = value;
        }

        return result;
    }
}


// =============================================================
// DİSK BİLGİ SINIFI
// =============================================================

public class DiskInfo
{
    public int DiskNumber { get; set; }

    public string DeviceId { get; set; } = "";

    public string Model { get; set; } = "";

    public string InterfaceType { get; set; } = "";

    public string PNPDeviceID { get; set; } = "";

    public string SerialNumber { get; set; } = "";

    public long SizeBytes { get; set; }

    public string SizeText { get; set; } = "";

    public string DriveLetter { get; set; } = "";

    public string VidPid { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string Service { get; set; } = "";
}


// =============================================================
// WINDOWS NATIVE API
// =============================================================

internal static class NativeMethods
{
    public static readonly IntPtr INVALID_HANDLE_VALUE =
        new IntPtr(-1);

    public const uint GENERIC_WRITE =
        0x40000000;

    public const uint FILE_SHARE_READ =
        0x00000001;

    public const uint FILE_SHARE_WRITE =
        0x00000002;

    public const uint CREATE_ALWAYS =
        2;

    public const uint FILE_ATTRIBUTE_NORMAL =
        0x00000080;

    public const uint FILE_FLAG_SEQUENTIAL_SCAN =
        0x08000000;


    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);


    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);


    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlushFileBuffers(
        IntPtr hFile);


    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(
        IntPtr hObject);
}