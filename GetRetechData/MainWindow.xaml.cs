using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using System.Drawing;

namespace GetRetechData
{
    public partial class MainWindow : Window
    {
        private readonly int _pageSize = 100;
        private int _currentPage = 1;
        private int _totalPages = 0;
        private int _totalRows = 0;
        private string _connString;
        private string _importConnString;
        private bool _isImporting;
        private string importServer = "";
        private readonly DispatcherTimer _autoLoadTimer;
        private DateTime _nextRunTime;
        private readonly DispatcherTimer _connCheckTimer;
        private DateTime _nextConnCheckTime;
        private DateTime _lastRunTime;
        private bool _lastRunSuccess = true;
        private bool _isBusy;
        private readonly WF.NotifyIcon _notifyIcon;
        private readonly DispatcherTimer _clockTimer;

        public MainWindow()
        {
            InitializeComponent();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateTitleClock();
            _clockTimer.Start();
            UpdateTitleClock();

            TxtLogPath.Text = $"Log: {LogWriter.LogDirectory}";
            TxtConfigPath.Text = $"Config: {ConfigManager.ConfigPath}";

            _notifyIcon = new WF.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Application.ResourceAssembly.Location),
                Visible = true,
                Text = "GetRetechData"
            };
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            Closing += (s, e) =>
            {
                if (_isImporting)
                {
                    e.Cancel = true;
                    return;
                }
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            };

            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                    HideWindow();
            };

            var config = ConfigManager.Load();

            int autoSecs = config.AutoLoadIntervalSeconds > 0 ? config.AutoLoadIntervalSeconds : 3600;
            int connSecs = config.ConnCheckIntervalSeconds > 0 ? config.ConnCheckIntervalSeconds : 60;

            _autoLoadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(autoSecs)
            };
            _autoLoadTimer.Tick += AutoLoadTimer_Tick;
            _nextRunTime = DateTime.Now.Add(_autoLoadTimer.Interval);
            _autoLoadTimer.Start();
            UpdateNextRunLabel();

            _connCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(connSecs)
            };
            _connCheckTimer.Tick += ConnCheckTimer_Tick;
            _nextConnCheckTime = DateTime.Now.Add(_connCheckTimer.Interval);
            _connCheckTimer.Start();
            UpdateConnCheckLabel();

            string host = config.OracleHost ?? "";
            string port = config.OraclePort ?? "";
            string sid = config.OracleSid ?? "";
            string user = config.OracleUser ?? "";
            string pass = config.OraclePass ?? "";

            _connString = $"User Id={user};Password={pass};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={sid})));";
            _importConnString = config.ImportConnString ?? "";

            ConfigManager.Watch(cfg => Dispatcher.Invoke(() => ApplyConfig(cfg)));
        }

        private void ApplyConfig(AppConfig config)
        {
            int autoSecs = config.AutoLoadIntervalSeconds > 0 ? config.AutoLoadIntervalSeconds : 3600;
            int connSecs = config.ConnCheckIntervalSeconds > 0 ? config.ConnCheckIntervalSeconds : 60;

            _autoLoadTimer.Interval = TimeSpan.FromSeconds(autoSecs);
            _connCheckTimer.Interval = TimeSpan.FromSeconds(connSecs);
            _nextRunTime = DateTime.Now.Add(_autoLoadTimer.Interval);
            _nextConnCheckTime = DateTime.Now.Add(_connCheckTimer.Interval);
            UpdateNextRunLabel();
            UpdateConnCheckLabel();

            string host = config.OracleHost ?? "";
            string port = config.OraclePort ?? "";
            string sid = config.OracleSid ?? "";
            string user = config.OracleUser ?? "";
            string pass = config.OraclePass ?? "";
            _connString = $"User Id={user};Password={pass};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={sid})));";
            _importConnString = config.ImportConnString ?? _importConnString;

            LogWriter.Write($"Konfigurasi diperbarui: {ConfigManager.ConfigPath}");
        }

        private void HideWindow()
        {
            _notifyIcon.ShowBalloonTip(2000, "GetRetechData", "Aplikasi berjalan di system tray. Klik dua kali untuk membuka kembali.", WF.ToolTipIcon.Info);
            Hide();
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(LogWriter.LogDirectory))
                    Directory.CreateDirectory(LogWriter.LogDirectory);
                System.Diagnostics.Process.Start("explorer.exe", LogWriter.LogDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal membuka folder log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigManager.Load();
                if (!File.Exists(ConfigManager.ConfigPath))
                    ConfigManager.Save();
                System.Diagnostics.Process.Start("notepad.exe", ConfigManager.ConfigPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal membuka config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            CheckConnection();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CheckConnection();
        }

        private void ConnCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (_isBusy || _isImporting) return;
            _nextConnCheckTime = DateTime.Now.Add(_connCheckTimer.Interval);
            UpdateConnCheckLabel();
            CheckConnection();
        }

        private void UpdateConnCheckLabel()
        {
            TxtNextConnCheck.Text = _connCheckTimer.IsEnabled
                ? $"Pengecekan koneksi berikutnya: {FormatDateTime(_nextConnCheckTime)}"
                : "Pengecekan koneksi berikutnya: -";
        }

        private void CheckConnection()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using (OracleConnection conn = new OracleConnection(_connString))
            {
                try
                {
                    TxtStatus.Text = "Menghubungkan...";
                    conn.Open();
                    TxtStatus.Text = "Status: Sukses Terhubung ke Oracle!";
                    BtnLoadData.IsEnabled = true;

                    if (!_autoLoadTimer.IsEnabled)
                    {
                        _nextRunTime = DateTime.Now.Add(_autoLoadTimer.Interval);
                        _autoLoadTimer.Start();
                        UpdateNextRunLabel();
                    }
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Status Gagal: {ex.Message}";
                    if (_autoLoadTimer.IsEnabled)
                    {
                        _autoLoadTimer.Stop();
                        UpdateNextRunLabel();
                    }
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private string BaseQuery => @"select loc,item,cast(stock_on_hand as int) as stock_on_hand,
            av_cost,First_Received,Last_Received,First_sold,Last_sold,
            cast(in_transit_qty as int) as in_transit_qty,
            soh_update_datetime,last_update_datetime
            from item_loc_soh
            where coalesce(stock_on_hand,0)<>0 or coalesce(in_transit_qty,0)<>0";

        private string CountQuery => @"select count(*) from item_loc_soh
            where coalesce(stock_on_hand,0)<>0 or coalesce(in_transit_qty,0)<>0";

        private bool LoadPage()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            int offset = (_currentPage - 1) * _pageSize;
            int endRow = offset + _pageSize;

            string pagedQuery = $@"
                select * from (
                    select a.*, rownum rn from (
                        {BaseQuery}
                        order by loc, item
                    ) a
                    where rownum <= {endRow}
                )
                where rn > {offset}";

            using (OracleConnection conn = new OracleConnection(_connString))
            {
                try
                {
                    OracleDataAdapter da = new OracleDataAdapter(pagedQuery, conn);
                    DataTable dt = new DataTable();
                    da.Fill(dt);
                    dt.Columns.Remove("rn");
                    DataGridResult.ItemsSource = dt.DefaultView;

                    TxtPageInfo.Text = $"Page {_currentPage} of {_totalPages}";
                    BtnPrev.IsEnabled = _currentPage > 1;
                    BtnNext.IsEnabled = _currentPage < _totalPages;
                    TxtStatus.Text = $"Total: {_totalRows} baris -- Page {_currentPage} of {_totalPages} ({offset + 1}-{Math.Min(_currentPage * _pageSize, _totalRows)})";
                    return true;
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Gagal: {ex.Message}";
                    return false;
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void DisabledAll()
        {
            BtnLoadData.IsEnabled = false;
            BtnConnect.IsEnabled = false;
            BtnImport.IsEnabled = false;
            BtnExport.IsEnabled = false;
            DataGridResult.IsEnabled = false;
            BtnPrev.IsEnabled = _currentPage > 1;
            BtnNext.IsEnabled = _currentPage < _totalPages;
        }
        private void EnabledAll()
        {
            BtnLoadData.IsEnabled = true;
            BtnConnect.IsEnabled = true;
            BtnImport.IsEnabled = true;
            BtnExport.IsEnabled = true;
            DataGridResult.IsEnabled = true;
            BtnPrev.IsEnabled = _currentPage > 1;
            BtnNext.IsEnabled = _currentPage < _totalPages;
        }

        private void BtnLoadData_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private async void AutoLoadTimer_Tick(object? sender, EventArgs e)
        {
            if (_isImporting || _isBusy) return;
            _isBusy = true;
            _autoLoadTimer.Stop();
            try
            {
                await RunPipelineAsync();
            }
            finally
            {
                _isBusy = false;
                _nextRunTime = DateTime.Now.Add(_autoLoadTimer.Interval);
                _autoLoadTimer.Start();
                UpdateNextRunLabel();
            }
        }

        private void UpdateTitleClock()
        {
            Title = $"GetRetechData - {DateTime.Now:dd MMMM yyyy HH:mm:ss}";
        }

        private static string FormatDateTime(DateTime dt)
        {
            return dt.ToString("dd MMMM yyyy 'pukul' HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
        }

        private void UpdateProgressPercent()
        {
            double max = ProgressBarExport.Maximum;
            double value = ProgressBarExport.Value;
            int pct = max > 0 ? (int)Math.Round(value / max * 100) : 0;
            TxtProgressPercent.Text = $"{pct}%";
        }

        private void UpdateNextRunLabel()
        {
            TxtNextRun.Text = _autoLoadTimer.IsEnabled
                ? $"Impor data berikutnya: {FormatDateTime(_nextRunTime)}"
                : "Impor data berikutnya: -";
        }

        private async Task RunPipelineAsync()
        {
            _lastRunTime = DateTime.Now;
            try
            {
                bool loadOk = LoadData();

                if (!loadOk)
                {
                    _lastRunSuccess = false;
                    UpdateLastRunLabel();
                    TxtStatus.Text = $"Load Page Gagal pada {FormatDateTime(_lastRunTime)}";
                    return;
                }

                if (_totalRows == 0)
                {
                    TxtStatus.Text = "Tidak ada data untuk diekspor/diimpor.";
                    return;
                }

                string exportDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GetRetechData");
                Directory.CreateDirectory(exportDir);
                string zipPath = System.IO.Path.Combine(exportDir, $"item_loc_soh_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

                await ExportToZipAsync(zipPath);
                await ImportFromFileAsync(zipPath, showMessage: false);

                _lastRunSuccess = true;
                UpdateLastRunLabel();
            }
            catch (Exception ex)
            {
                _lastRunSuccess = false;
                UpdateLastRunLabel();
                TxtStatus.Text = $"Gagal pipeline: {ex.Message}";
            }
        }

        private void UpdateLastRunLabel()
        {
            TxtLastRun.Text = _lastRunSuccess
                ? $"Impor data sebelumnya: Sukses {FormatDateTime(_lastRunTime)}"
                : $"Impor data sebelumnya: GAGAL {FormatDateTime(_lastRunTime)}";
        }

        private bool LoadData()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using (OracleConnection conn = new OracleConnection(_connString))
            {
                try
                {
                    TxtStatus.Text = "Menghitung total baris...";
                    OracleCommand cmd = new OracleCommand(CountQuery, conn);
                    conn.Open();
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar());

                    _currentPage = 1;
                    _totalPages = (int)Math.Ceiling((double)_totalRows / _pageSize);
                    if (_totalPages == 0) _totalPages = 1;

                    bool ok = LoadPage();
                    BtnExport.IsEnabled = _totalRows > 0;
                    return ok;
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Gagal: {ex.Message}";
                    return false;
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                LoadPage();
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                LoadPage();
            }
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP Files|*.zip",
                FileName = "item_loc_soh.zip"
            };

            if (dialog.ShowDialog() != true)
                return;

            await ExportToZipAsync(dialog.FileName);
        }

        private async Task ExportToZipAsync(string filePath)
        {
ProgressBarGrid.Visibility = Visibility.Visible;
                ProgressBarExport.IsIndeterminate = true;
                TxtProgressPercent.Text = "";
                BtnExport.IsEnabled = false;

                LogWriter.Write($"Export dimulai | Status: Berjalan | Target: {filePath}");

                try
                {
                    DisabledAll();
                    TxtStatus.Text = "Menghitung total baris...";

                int totalRows;
                using (var conn = new OracleConnection(_connString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new OracleCommand(CountQuery, conn))
                    {
                        totalRows = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }
                }

                if (totalRows == 0)
                {
                    LogWriter.Write("Export selesai | Status: Sukses | Rows berhasil diekspor: 0 | Rows gagal diekspor: 0 | Tidak ada data");
                    TxtStatus.Text = "Tidak ada data untuk diekspor.";
                    return;
                }

                ProgressBarExport.Maximum = totalRows;
                ProgressBarExport.Value = 0;
                ProgressBarExport.IsIndeterminate = false;
                UpdateProgressPercent();

                TxtStatus.Text = $"[Sedang Berjalan] Mengekspor {totalRows} baris...";

                await Task.Run(() =>
                {
                    using (var conn = new OracleConnection(_connString))
                    {
                        conn.Open();
                        string query = BaseQuery + " order by loc, item";
                        using (var cmd = new OracleCommand(query, conn))
                        using (var reader = cmd.ExecuteReader())
                        using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                if (i > 0) writer.Write('|');
                                writer.Write(reader.GetName(i));
                            }
                            writer.WriteLine();

                            int rowCount = 0;
                            var failedRows = new List<string>();
                            while (reader.Read())
                            {
                                var sb = new StringBuilder();
                                try
                                {
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        if (i > 0) sb.Append('|');
                                        if (!reader.IsDBNull(i))
                                            sb.Append(reader[i].ToString());
                                    }
                                    writer.WriteLine(sb.ToString());
                                    rowCount++;
                                }
                                catch (Exception)
                                {
                                    failedRows.Add(sb.ToString());
                                }

                                if (rowCount % 500 == 0)
                                {
                                    int current = rowCount;
                                    Dispatcher.Invoke(() =>
                                    {
                                        ProgressBarExport.Value = current;
                                        UpdateProgressPercent();
                                    });
                                }
                            }

                            Dispatcher.Invoke(() =>
                            {
                                ProgressBarExport.Value = rowCount;
                                UpdateProgressPercent();
                            });

                            if (failedRows.Count > 0)
                            {
                                foreach (var failed in failedRows)
                                    LogWriter.Write($"  EXPORT FAILED | {failed}");
                            }
                            LogWriter.Write($"Export selesai | Status: Sukses | Rows berhasil diekspor: {rowCount} | Rows gagal diekspor: {failedRows.Count} | File: {filePath}");
                        }
                    }
                });

                TxtStatus.Text = "Mengompres file...";
                ProgressBarExport.IsIndeterminate = true;
                TxtProgressPercent.Text = "";

                string zipPath = System.IO.Path.ChangeExtension(filePath, ".zip");
                string entryName = System.IO.Path.GetFileName(filePath);
                if (!entryName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    entryName = System.IO.Path.GetFileNameWithoutExtension(entryName) + ".csv";
                byte[] csvBytes = await Task.Run(() =>
                {
                    for (int retry = 0; ; retry++)
                    {
                        try
                        {
                            return File.ReadAllBytes(filePath);
                        }
                        catch (IOException) when (retry < 5)
                        {
                            Thread.Sleep(300);
                        }
                    }
                });
                await Task.Run(() =>
                {
                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        using (var memStream = new MemoryStream(csvBytes))
                        {
                            memStream.CopyTo(entryStream);
                        }
                    }
                });

                TxtStatus.Text = $"Ekspor selesai: {totalRows} baris -> {zipPath}";
                EnabledAll();
            }
            catch (Exception ex)
            {
                LogWriter.Write($"Export GAGAL | Status: Gagal | Error: {ex.InnerException?.Message ?? ex.Message}");
                TxtStatus.Text = $"Gagal ekspor: {ex.Message}";
            }
            finally
            {
                ProgressBarGrid.Visibility = Visibility.Collapsed;
                TxtProgressPercent.Text = "";
                BtnExport.IsEnabled = true;
            }
        }
        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV/ZIP Files|*.csv;*.zip",
                FileName = "item_loc_soh.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            await ImportFromFileAsync(dialog.FileName, showMessage: true);
        }

        private async Task ImportFromFileAsync(string sourcePath, bool showMessage)
        {
            ProgressBarGrid.Visibility = Visibility.Visible;
            ProgressBarExport.IsIndeterminate = true;
            TxtProgressPercent.Text = "";
            BtnImport.IsEnabled = false;
            DisabledAll();

            string csvPath = sourcePath;
            bool isTempFile = false;

            try
            {
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    TxtStatus.Text = "Membaca file ZIP...";
                    string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    isTempFile = true;

                    using (var archive = ZipFile.OpenRead(sourcePath))
                    {
                        var entry = archive.Entries.FirstOrDefault(e2 => e2.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
                        if (entry == null)
                        {
                            TxtStatus.Text = "Tidak ditemukan file CSV dalam ZIP.";
                            return;
                        }
                        csvPath = System.IO.Path.Combine(tempDir, entry.Name);
                        entry.ExtractToFile(csvPath, overwrite: true);
                    }
                }

                TxtStatus.Text = "Membaca file CSV...";
                string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    TxtStatus.Text = "File CSV kosong atau hanya header.";
                    return;
                }

                string headerLine = lines[0];
                string[] headers = headerLine.Split('|');
                int dataLines = lines.Length - 1;

                TxtStatus.Text = "Menyiapkan data...";

                DataTable dt = new DataTable();
                foreach (var header in headers)
                    dt.Columns.Add(header, typeof(string));

                var failedRows = new List<string>();
                for (int row = 1; row < lines.Length; row++)
                {
                    string[] cols = lines[row].Split('|');
                    var sb = new StringBuilder();
                    bool valid = true;
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (i > 0) sb.Append('|');
                        string value = (i < cols.Length) ? cols[i] : "";
                        sb.Append(value);
                        if (value.Length > 4000)
                            valid = false;
                    }
                    if (!valid)
                    {
                        failedRows.Add(sb.ToString());
                        continue;
                    }
                    var dr = dt.NewRow();
                    for (int i = 0; i < headers.Length; i++)
                        dr[i] = (i < cols.Length && !string.IsNullOrEmpty(cols[i])) ? cols[i] : DBNull.Value;
                    dt.Rows.Add(dr);
                }

                int validRows = dt.Rows.Count;
                LogWriter.Write($"Import dimulai | Status: Berjalan | File: {sourcePath} | Total baris dibaca: {dataLines}");

                ProgressBarExport.Maximum = validRows;
                ProgressBarExport.Value = 0;
                ProgressBarExport.IsIndeterminate = false;
                UpdateProgressPercent();

                _isImporting = true;
                TxtStatus.Text = $"Mengimpor {validRows} baris, mohon tunggu sedang diproses...";

                await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(_importConnString))
                    {
                        conn.Open();
                        importServer = conn.DataSource;

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $@"
                                CREATE TABLE #temp (
                                    {string.Join(", ", headers.Select(h => $"[{h}] NVARCHAR(4000)"))}
                                )";
                            cmd.ExecuteNonQuery();
                        }

                        using (var bulk = new SqlBulkCopy(conn))
                        {
                            bulk.DestinationTableName = "#temp";
                            bulk.BatchSize = 1000;
                            bulk.NotifyAfter = 1000;
                            bulk.SqlRowsCopied += (s, args) =>
                                Dispatcher.Invoke(() =>
                                {
                                    ProgressBarExport.Value = (int)args.RowsCopied;
                                    UpdateProgressPercent();
                                });
                            bulk.WriteToServer(dt);
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $@"
                                CREATE INDEX IX_temp_loc_item ON #temp ([loc], [item])";
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $@"
                                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'item_loc_soh')
                                BEGIN
                                    CREATE TABLE item_loc_soh (
                                        {string.Join(", ", headers.Select(h => $"[{h}] NVARCHAR(MAX)"))}
                                    )
                                END";
                            cmd.ExecuteNonQuery();
                        }

                        var updateCols = headers
                            .Where(h => !string.Equals(h, "loc", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(h, "item", StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                        string updateSet = string.Join(", ", updateCols.Select(c => $"target.[{c}] = source.[{c}]"));
                        string insertCols = string.Join(", ", headers.Select(c => $"[{c}]"));
                        string sourceCols = string.Join(", ", headers.Select(c => $"source.[{c}]"));

                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ProgressBarExport.IsIndeterminate = true;
                                TxtProgressPercent.Text = "";
                                TxtStatus.Text = "[Menunggu Proses Di Server] Menggabungkan data...";
                            });

                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandTimeout = 0;
                                cmd.CommandText = $@"
                                    MERGE item_loc_soh AS target
                                    USING #temp AS source
                                    ON target.[loc] = source.[loc] AND target.[item] = source.[item]
                                    WHEN MATCHED THEN UPDATE SET {updateSet}
                                    WHEN NOT MATCHED THEN INSERT ({insertCols}) VALUES ({sourceCols});";
                                cmd.ExecuteNonQuery();
                            }
                        }
                        finally
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "DROP TABLE #temp;";
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                });

                TxtStatus.Text = $"Impor selesai: {validRows} baris diimpor.";

                if (failedRows.Count > 0)
                {
                    foreach (var failed in failedRows)
                        LogWriter.Write($"  IMPORT FAILED | {failed}");
                }
                LogWriter.Write($"Import selesai | Status: Sukses | Rows berhasil diimpor: {validRows} | Rows gagal diimpor: {failedRows.Count} | Server: {importServer}");
            }
            catch (Exception ex)
            {
                LogWriter.Write($"Import GAGAL | Status: Gagal | Error: {ex.InnerException?.Message ?? ex.Message}");
                TxtStatus.Text = $"Gagal impor: {ex.Message}";
            }
            finally
            {
                if (isTempFile)
                {
                    try { File.Delete(csvPath); } catch { }
                    try
                    {
                        string? dir = System.IO.Path.GetDirectoryName(csvPath);
                        if (dir != null) Directory.Delete(dir, recursive: true);
                    }
                    catch { }
                }
                ProgressBarGrid.Visibility = Visibility.Collapsed;
                TxtProgressPercent.Text = "";
                BtnImport.IsEnabled = true;
                _isImporting = false;
                EnabledAll();
                if (showMessage)
                    MessageBox.Show("Proses impor ke server " + importServer + " selesai.", "Informasi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}