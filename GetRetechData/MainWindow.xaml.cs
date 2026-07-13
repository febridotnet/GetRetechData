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

namespace GetRetechData
{
    public partial class MainWindow : Window
    {
        private readonly int _pageSize = 100;
        private int _currentPage = 1;
        private int _totalPages = 0;
        private int _totalRows = 0;
        private string _connString;

        public MainWindow()
        {
            InitializeComponent();

            string host = "10.32.159.101";
            string port = "1523";
            string sid = "hbidbrms";
            string user = "rmsprd";
            string pass = "rmsidbit";

            _connString = $"User Id={user};Password={pass};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={sid})));";
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
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
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Status Gagal: {ex.Message}";
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

        private void LoadPage()
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
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Gagal: {ex.Message}";
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
            DataGridResult.IsEnabled= false;
            BtnNext.IsEnabled = false;
            BtnPrev.IsEnabled = false;
        }
        private void EnabledAll()
        {
            BtnLoadData.IsEnabled = true;
            BtnConnect.IsEnabled = true;
            DataGridResult.IsEnabled= true;
            BtnNext.IsEnabled = true;
            BtnPrev.IsEnabled = true;
        }

        private void BtnLoadData_Click(object sender, RoutedEventArgs e)
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

                    LoadPage();
                    BtnExport.IsEnabled = _totalRows > 0;
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Gagal: {ex.Message}";
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

            string filePath = dialog.FileName;
            ProgressBarExport.Visibility = Visibility.Visible;
            ProgressBarExport.IsIndeterminate = true;
            BtnExport.IsEnabled = false;

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
                    TxtStatus.Text = "Tidak ada data untuk diekspor.";
                    return;
                }

                ProgressBarExport.Maximum = totalRows;
                ProgressBarExport.Value = 0;
                ProgressBarExport.IsIndeterminate = false;

                TxtStatus.Text = $"Mengekspor {totalRows} baris...";

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
                            while (reader.Read())
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    if (i > 0) writer.Write('|');
                                    if (!reader.IsDBNull(i))
                                        writer.Write(reader[i].ToString());
                                }
                                writer.WriteLine();

                                rowCount++;
                                if (rowCount % 500 == 0)
                                {
                                    int current = rowCount;
                                    Dispatcher.Invoke(() => ProgressBarExport.Value = current);
                                }
                            }

                            Dispatcher.Invoke(() => ProgressBarExport.Value = rowCount);
                        }
                    }
                });

                TxtStatus.Text = "Mengompres file...";
                ProgressBarExport.IsIndeterminate = true;

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
                TxtStatus.Text = $"Gagal ekspor: {ex.Message}";
            }
            finally
            {
                ProgressBarExport.Visibility = Visibility.Collapsed;
                BtnExport.IsEnabled = true;
            }
        }
    }
}