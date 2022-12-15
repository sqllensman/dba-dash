﻿using DBADashGUI.Checks;
using Humanizer;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DBADashGUI.DBADashStatus;
using static DBADashGUI.Main;

namespace DBADashGUI
{
    public partial class Summary : UserControl, ISetContext
    {
        private List<Int32> refreshInstanceIDs;
        private bool refreshIncludeHidden;

        private DateTime lastRefresh;

        public Summary()
        {
            InitializeComponent();
            dgvTests.AutoGenerateColumns = false;
            dgvTests.Columns.AddRange(TestCols.ToArray());
        }

        private DataView dv;

        private Dictionary<string, bool> statusColumns;

        private bool FocusedView { get => focusedViewToolStripMenuItem.Checked; }
        private DBADashContext context;
        private bool IncludeHidden => context.InstanceIDs.Count == 1 || Common.ShowHidden;

        private readonly Dictionary<string, string> tabMapping = new() { { "FullBackupStatus", "tabBackups" }, { "LogShippingStatus", "tabLogShipping" }, { "DiffBackupStatus", "tabBackups" }, { "LogBackupStatus", "tabBackups" }, { "DriveStatus", "tabDrives" },
                                                            { "JobStatus", "tabJobs" }, { "CollectionErrorStatus", "tabDBADashErrorLog"}, { "AGStatus", "tabAG" }, {"LastGoodCheckDBStatus","tabLastGood"}, {"SnapshotAgeStatus","tabCollectionDates"  },
                                                            {"MemoryDumpStatus","" }, {"UptimeStatus","" }, {"CorruptionStatus","" }, {"AlertStatus","tabAlerts" }, {"FileFreeSpaceStatus","tabFiles" },
                                                            {"CustomCheckStatus","tabCustomChecks"  }, {"MirroringStatus","tabMirroring" },{"ElasticPoolStorageStatus","tabAzureSummary"},{"PctMaxSizeStatus","tabFiles"}, {"QueryStoreStatus","tabQS" },
                                                            {"LogFreeSpaceStatus","tabFiles"},{"DBMailStatus","" },{"IdentityStatus","tabIdentityColumns"  }, {"IsAgentRunningStatus","" } };

        private void ResetStatusCols()
        {
            statusColumns = new Dictionary<string, bool> { { "FullBackupStatus", false }, { "LogShippingStatus", false }, { "DiffBackupStatus", false }, { "LogBackupStatus", false }, { "DriveStatus", false },
                                                            { "JobStatus", false }, { "CollectionErrorStatus", false }, { "AGStatus", false }, {"LastGoodCheckDBStatus",false}, {"SnapshotAgeStatus",false },
                                                            {"MemoryDumpStatus",false }, {"UptimeStatus",false }, {"CorruptionStatus",false }, {"AlertStatus",false }, {"FileFreeSpaceStatus",false },
                                                            {"CustomCheckStatus",false }, {"MirroringStatus",false },{"ElasticPoolStorageStatus",false},{"PctMaxSizeStatus",false}, {"QueryStoreStatus",false },
                                                            {"LogFreeSpaceStatus",false },{"DBMailStatus",false },{"IdentityStatus",false },{"IsAgentRunningStatus",false } };
        }

        private Task<DataTable> GetSummaryAsync()
        {
            return Task<DataTable>.Factory.StartNew(() =>
            {
                using (var cn = new SqlConnection(Common.ConnectionString))
                using (var cmd = new SqlCommand("dbo.Summary_Get", cn) { CommandType = CommandType.StoredProcedure })
                using (var da = new SqlDataAdapter(cmd))
                {
                    cn.Open();

                    cmd.Parameters.AddWithValue("InstanceIDs", string.Join(",", context.InstanceIDs));
                    cmd.Parameters.AddWithValue("IncludeHidden", IncludeHidden);
                    DataTable dt = new();
                    da.Fill(dt);
                    return dt;
                }
            });
        }

        public void SetContext(DBADashContext context)
        {
            this.context = context;
            RefreshDataIfStale();
        }

        public void RefreshDataIfStale()
        {
            if (DateTime.UtcNow.Subtract(lastRefresh).TotalMinutes > 5 || ParametersChanged)
            {
                RefreshData();
            }
            else
            {
                UpdateRefreshTime(); // Ensure refresh time is in correct time zone in case of switching time zones.
                dgvSummary.Columns[0].Frozen = Common.FreezeKeyColumn;
            }
        }

        private bool ParametersChanged => !(context.InstanceIDs.Count == refreshInstanceIDs.Count && refreshInstanceIDs.All(context.InstanceIDs.Contains) && refreshIncludeHidden == Common.ShowHidden);

        private CancellationTokenSource cancellationTS = new();
        private bool savedLayoutLoaded;

        public void RefreshData()
        {
            if (!savedLayoutLoaded)
            {
                LoadSavedLayout();
                savedLayoutLoaded = true;
            }
            cancellationTS.Cancel(); // Cancel previous execution
            cancellationTS = new();
            dgvSummary.Columns[0].Frozen = Common.FreezeKeyColumn;
            ResetStatusCols();
            refresh1.ShowRefresh();
            splitContainer1.Visible = false;
            refreshInstanceIDs = new List<int>(context.InstanceIDs);
            refreshIncludeHidden = Common.ShowHidden;
            tsRefresh.Enabled = false;
            _ = GetSummaryAsync().ContinueWith(task =>
            {
                toolStrip1.Invoke(() => { tsRefresh.Enabled = true; tsClearFilter.Enabled = false; });
                if (task.Exception != null)
                {
                    refresh1.SetFailed("Error:" + task.Exception.ToString());
                    return Task.CompletedTask;
                }
                DataTable dt = task.Result;
                GroupSummaryByTest(ref dt);
                UpdateSummary(ref dt);
                return Task.CompletedTask;
            }, cancellationTS.Token);
        }

        public void LoadSavedLayout()
        {
            try
            {
                SummarySavedView saved = SummarySavedView.GetDefaultSavedView();

                if (saved != null)
                {
                    Common.ShowHidden = saved.ShowHidden;
                    focusedViewToolStripMenuItem.Checked = saved.FocusedView;
                    showTestSummaryToolStripMenuItem.Checked = saved.ShowTestSummary;
                    splitContainer1.Panel1Collapsed = !showTestSummaryToolStripMenuItem.Checked;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading saved view\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private readonly List<DataGridViewColumn> TestCols = new()
        {           new DataGridViewLinkColumn(){ Name="Test", HeaderText="Test", DataPropertyName="DisplayText", SortMode = DataGridViewColumnSortMode.Automatic},
                    new DataGridViewLinkColumn(){ Name="OK", HeaderText="Instance Count OK", DataPropertyName="OK", SortMode = DataGridViewColumnSortMode.Automatic },
                    new DataGridViewLinkColumn(){ Name = "Warning",  HeaderText="Instance Count Warning", DataPropertyName="Warning", SortMode = DataGridViewColumnSortMode.Automatic },
                    new DataGridViewLinkColumn(){ Name = "Critical",  HeaderText="Instance Count Critical", DataPropertyName="Critical", SortMode = DataGridViewColumnSortMode.Automatic },
                    new DataGridViewLinkColumn(){ Name = "NA",  HeaderText="Instance Count N/A", DataPropertyName="NA",SortMode = DataGridViewColumnSortMode.Automatic },
        };

        private static DataTable GroupedByTestSchema()
        {
            DataTable grouped = new();
            grouped.Columns.Add("Test", typeof(string));
            grouped.Columns.Add("DisplayText", typeof(string));
            grouped.Columns.Add("OK", typeof(int));
            grouped.Columns.Add("Warning", typeof(int));
            grouped.Columns.Add("Critical", typeof(int));
            grouped.Columns.Add("NA", typeof(int));
            grouped.Columns.Add("Total", typeof(int));
            grouped.Columns.Add("Status", typeof(DBADashStatusEnum));
            grouped.Columns.Add("IsFocusedRow", typeof(bool));
            return grouped;
        }

        private void GroupSummaryByTest(ref DataTable dt)
        {
            DataTable grouped = GroupedByTestSchema();
            Dictionary<string, DataRow> tests = new();

            // Add a row for each test with zeros/defaults
            foreach (string statusCol in statusColumns.Keys)
            {
                DataRow row = grouped.NewRow();
                row["Test"] = statusCol;
                row["DisplayText"] = dgvSummary.Columns[statusCol].HeaderText;
                row["OK"] = 0;
                row["Warning"] = 0;
                row["Critical"] = 0;
                row["NA"] = 0;
                row["Total"] = 0;
                row["Status"] = DBADashStatusEnum.NA;
                row["IsFocusedRow"] = false;
                tests.Add(statusCol, row);
            }
            // Add count of instances by status for each test
            foreach (DataRow row in dt.Rows)
            {
                foreach (string statusCol in statusColumns.Keys)
                {
                    DataRow groupedRow = tests[statusCol];
                    var status = (DBADashStatus.DBADashStatusEnum)Convert.ToInt32(row[statusCol] == DBNull.Value ? 3 : row[statusCol]);
                    groupedRow[status.ToString()] = (int)groupedRow[status.ToString()] + 1;
                    groupedRow["Total"] = (int)groupedRow["Total"] + 1;
                    if (status == DBADashStatusEnum.Warning || status == DBADashStatusEnum.Critical)
                    {
                        groupedRow["IsFocusedRow"] = true;
                    }
                }
            }
            tests.Values.CopyToDataTable(grouped, LoadOption.OverwriteChanges);

            dv = new DataView(grouped, TestRowFilter, "DisplayText", DataViewRowState.CurrentRows);
            dgvTests.Invoke((Action)(() =>
            {
                dgvTests.DataSource = dv;
                dgvTests.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            }));
            // Auto size split container
            if (dgvTests.Rows.Count > 0)
            {
                splitContainer1.Invoke(() =>
                {
                    splitContainer1.SplitterDistance = ((dgvTests.Rows[0].Height + 1) * dgvTests.Rows.Count) + dgvTests.ColumnHeadersHeight;
                });
            }
        }

        private void SetStatusColumnVisiblity()
        {
            // hide columns that all have status N/A
            foreach (var col in statusColumns)
            {
                dgvSummary.Columns[col.Key].Visible = col.Value;
            }
        }

        private void HideStatusColumns()
        {
            // hide all status columns
            foreach (var col in statusColumns)
            {
                dgvSummary.Columns[col.Key].Visible = false;
            }
        }

        private void UpdateSummary(ref DataTable dt)
        {
            GroupSummaryByTest(ref dt);
            dgvSummary.AutoGenerateColumns = false;
            var cols = (statusColumns.Keys).ToList<string>();
            dt.Columns.Add("IsFocusedRow", typeof(bool));
            foreach (DataRow row in dt.Rows)
            {
                bool isFocusedRow = false;
                foreach (string col in cols)
                {
                    var status = (DBADashStatus.DBADashStatusEnum)Convert.ToInt32(row[col] == DBNull.Value ? 3 : row[col]);
                    if (!(status == DBADashStatus.DBADashStatusEnum.NA || (status == DBADashStatus.DBADashStatusEnum.OK && FocusedView)))
                    {
                        statusColumns[col] = true;
                        isFocusedRow = true;
                    }
                }

                if (row["IsAgentRunning"] != DBNull.Value && (bool)row["IsAgentRunning"] == false)
                {
                    isFocusedRow = true;
                    statusColumns["JobStatus"] = true;
                }
                row["IsFocusedRow"] = isFocusedRow;
            }
            dgvSummary.Invoke(() => SetStatusColumnVisiblity());
            dgvSummary.Invoke((Action)(() => colShowInSummary.Visible = IncludeHidden));

            dv = new DataView(dt, SummaryRowFilter, "Instance", DataViewRowState.CurrentRows);
            dgvSummary.Invoke((Action)(() =>
            {
                dgvSummary.DataSource = dv;
                dgvSummary.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            }
            ));
            lastRefresh = DateTime.UtcNow;
            toolStrip1.Invoke(() =>
            {
                UpdateRefreshTime();
                lblRefreshTime.ForeColor = DBADashStatusEnum.OK.GetColor();
            }
            );
            refresh1.Invoke((Action)(() => refresh1.Visible = false));
            splitContainer1.Invoke(() => splitContainer1.Visible = true);
            timer1.Enabled = true;
        }

        private string SummaryRowFilter => FocusedView ? "IsFocusedRow=1" : "";
        private string TestRowFilter => FocusedView ? "IsFocusedRow=1" : "OK>0 OR Warning>0 OR Critical>0";

        private void DgvSummary_RowAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            for (Int32 idx = e.RowIndex; idx < e.RowIndex + e.RowCount; idx += 1)
            {
                var row = (DataRowView)dgvSummary.Rows[idx].DataBoundItem;
                bool isAzure = row["InstanceID"] == DBNull.Value;
                var cols = (statusColumns.Keys).ToList<string>();
                foreach (var col in cols)
                {
                    var status = (DBADashStatus.DBADashStatusEnum)Convert.ToInt32(row[col] == DBNull.Value ? 3 : row[col]);
                    dgvSummary.Rows[idx].Cells[col].SetStatusColor(status);
                }
                string DBMailStatus = Convert.ToString(row["DBMailStatusDescription"]);
                dgvSummary.Rows[idx].Cells["DBMailStatus"].ToolTipText = DBMailStatus;

                dgvSummary.Rows[idx].Cells["FullBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["DiffBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["LogBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["DriveStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["JobStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["LogShippingStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["AGStatus"].Value = (int)row["AGStatus"] == 3 ? "" : "View";
                dgvSummary.Rows[idx].Cells["QueryStoreStatus"].Value = (int)row["QueryStoreStatus"] == 3 ? "" : "View";
                if (row["IsAgentRunning"] != DBNull.Value && (bool)row["IsAgentRunning"] == false)
                {
                    dgvSummary.Rows[idx].Cells["JobStatus"].SetStatusColor(Color.Black);
                    dgvSummary.Rows[idx].Cells["JobStatus"].Value = "Not Running";
                }

                string uptimeString;
                if (row["sqlserver_uptime"] != DBNull.Value)
                {
                    Int32 uptime = (Int32)row["sqlserver_uptime"];
                    Int32 addUptime = (Int32)row["AdditionalUptime"];
                    if (uptime < 120)
                    {
                        uptimeString = uptime.ToString() + " Mins (+" + addUptime.ToString() + "mins)";
                    }
                    else if (uptime < 1440)
                    {
                        uptimeString = (uptime / 60).ToString("0") + " Hours  (+" + addUptime.ToString() + "mins)";
                    }
                    else
                    {
                        uptimeString = (uptime / 1440).ToString("0") + " days";
                    }
                    dgvSummary.Rows[idx].Cells["UptimeStatus"].Value = uptimeString;
                }

                if (row["SnapshotAgeMin"] == DBNull.Value || row["SnapshotAgeMax"] == DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = "N/A";
                }
                else
                {
                    Int32 snapshotAgeMin = (Int32)row["SnapshotAgeMin"];
                    Int32 snapshotAgeMax = (Int32)row["SnapshotAgeMax"];
                    if (snapshotAgeMax == snapshotAgeMin)
                    {
                        dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = snapshotAgeMax + "mins";
                    }
                    else
                    {
                        dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = snapshotAgeMin.ToString() + " to " + snapshotAgeMax.ToString() + "mins";
                    }
                }
                if (row["DaysSinceLastGoodCheckDB"] != DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].Value = ((Int32)row["DaysSinceLastGoodCheckDB"]).ToString() + " days";
                }
                string oldestLastGoodCheckDB = "Unknown";
                if (row["OldestLastGoodCheckDBTime"] != DBNull.Value)
                {
                    if ((DateTime)row["OldestLastGoodCheckDBTime"] == DateTime.Parse("1900-01-01"))
                    {
                        oldestLastGoodCheckDB = "Never";
                        dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].Value = "Never";
                    }
                    else
                    {
                        oldestLastGoodCheckDB = ((DateTime)row["OldestLastGoodCheckDBTime"]).ToAppTimeZone().ToString("yyyy-MM-dd HH:mm");
                    }
                }
                if (row["LastGoodCheckDBCriticalCount"] != DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].ToolTipText = "Last Good CheckDB Critical:" + (Int32)row["LastGoodCheckDBCriticalCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB Warning:" + (Int32)row["LastGoodCheckDBWarningCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB Good:" + (Int32)row["LastGoodCheckDBHealthyCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB NA:" + (Int32)row["LastGoodCheckDBNACount"] + Environment.NewLine +
                                                                               "Oldest Last Good CheckDB:" + oldestLastGoodCheckDB;
                    ;
                }
                if (row["LastMemoryDump"] != DBNull.Value)
                {
                    DateTime lastMemoryDump = (DateTime)row["LastMemoryDump"];
                    DateTime lastMemoryDumpUTC = (DateTime)row["LastMemoryDumpUTC"];
                    Int32 memoryDumpCount = (Int32)row["MemoryDumpCount"];
                    string lastMemoryDumpStr;

                    if (Math.Abs(lastMemoryDumpUTC.ToAppTimeZone().Subtract(lastMemoryDump).TotalMinutes) > 10)
                    {
                        lastMemoryDumpStr = "Last Memory Dump (local time): " + lastMemoryDumpUTC.ToAppTimeZone().ToString() + Environment.NewLine +
                           "Last Memory Dump (server time): " + lastMemoryDump.ToString() + Environment.NewLine +
                           "Total Memory Dumps: " + memoryDumpCount; ;
                    }
                    else
                    {
                        lastMemoryDumpStr = "Last Memory Dump: " + lastMemoryDumpUTC.ToAppTimeZone().ToString() + Environment.NewLine +
                           "Total Memory Dumps: " + memoryDumpCount; ;
                    }

                    dgvSummary.Rows[idx].Cells["MemoryDumpStatus"].Value = DateTime.UtcNow.Subtract(lastMemoryDumpUTC).Humanize(1);

                    dgvSummary.Rows[idx].Cells["MemoryDumpStatus"].ToolTipText = lastMemoryDumpStr;
                }
                string lastAlert = "Never";
                string lastAlertDays = "Never";
                string lastCriticalAlert = "Never";
                if (row["LastAlert"] != DBNull.Value)
                {
                    DateTime lastAlertD = (DateTime)row["LastAlert"];
                    lastAlert = lastAlertD.ToAppTimeZone().ToString("yyyy-MM-dd HH:mm");
                    if (DateTime.UtcNow.Subtract(lastAlertD).TotalHours < 24)
                    {
                        lastAlertDays = DateTime.UtcNow.Subtract(lastAlertD).TotalHours.ToString("0") + "hrs";
                    }
                    else
                    {
                        lastAlertDays = DateTime.UtcNow.Subtract(lastAlertD).TotalDays.ToString("0") + " days";
                    }
                }
                if (row["LastCritical"] != DBNull.Value)
                {
                    lastCriticalAlert = (((DateTime)row["LastCritical"]).ToAppTimeZone()).ToString("yyyy-MM-dd HH:mm");
                }
                Int32 totalAlerts = row["TotalAlerts"] == DBNull.Value ? 0 : (Int32)row["TotalAlerts"];

                dgvSummary.Rows[idx].Cells["AlertStatus"].Value = lastAlertDays;
                dgvSummary.Rows[idx].Cells["AlertStatus"].ToolTipText = "Last Alert:" + lastAlert + Environment.NewLine +
                                                                        "Last Critical Alert:" + lastCriticalAlert + Environment.NewLine +
                                                                        "Total Alerts:" + totalAlerts;
                dgvSummary.Rows[idx].Cells["ElasticPoolStorageStatus"].Value = isAzure ? "View" : "";
            }
        }

        private void DgvSummary_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var sortCol = "";
            if (dgvSummary.Columns[e.ColumnIndex] == FullBackupStatus)
            {
                sortCol = "FullBackupStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == AlertStatus)
            {
                sortCol = "AlertStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == SnapshotAgeStatus)
            {
                sortCol = "SnapshotAgeMax";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == JobStatus)
            {
                sortCol = "JobStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == AGStatus)
            {
                sortCol = "AGStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == CorruptionStatus)
            {
                sortCol = "CorruptionStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == UptimeStatus)
            {
                sortCol = "sqlserver_uptime";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == DriveStatus)
            {
                sortCol = "DriveStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == MemoryDumpStatus)
            {
                sortCol = "LastMemoryDump";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LastGoodCheckDBStatus)
            {
                sortCol = "LastGoodCheckDBStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == DiffBackupStatus)
            {
                sortCol = "DiffBackupStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogBackupStatus)
            {
                sortCol = "LogBackupStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogShippingStatus)
            {
                sortCol = "LogShippingStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == FileFreeSpaceStatus)
            {
                sortCol = "FileFreeSpaceStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogFreeSpaceStatus)
            {
                sortCol = "LogFreeSpaceStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == CollectionErrorStatus)
            {
                sortCol = "CollectionErrorStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == ElasticPoolStorageStatus)
            {
                sortCol = "ElasticPoolStorageStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == PctMaxSizeStatus)
            {
                sortCol = "PctMaxSizeStatus";
            }

            if (sortCol != "")
            {
                if (dv.Sort == sortCol)
                {
                    dv.Sort = sortCol += " DESC";
                }
                else
                {
                    dv.Sort = sortCol;
                }
            }
        }

        private void TsRefresh_Click(object sender, EventArgs e)
        {
            RefreshData();
        }

        public event EventHandler<InstanceSelectedEventArgs> Instance_Selected;

        private void DgvSummary_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataRowView row = (DataRowView)dgvSummary.Rows[e.RowIndex].DataBoundItem;
                if (e.ColumnIndex == LastGoodCheckDBStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabLastGood" });
                    }
                }
                else if (e.ColumnIndex == FullBackupStatus.Index || e.ColumnIndex == DiffBackupStatus.Index || e.ColumnIndex == LogBackupStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabBackups" });
                    }
                }
                else if (e.ColumnIndex == LogShippingStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabLogShipping" });
                    }
                }
                else if (e.ColumnIndex == DriveStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabDrives" });
                    }
                }
                else if (e.ColumnIndex == JobStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabJobs" });
                    }
                }
                else if (e.ColumnIndex == FileFreeSpaceStatus.Index || e.ColumnIndex == PctMaxSizeStatus.Index || e.ColumnIndex == LogFreeSpaceStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabFiles" });
                }
                else if (e.ColumnIndex == CustomCheckStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabCustomChecks" });
                }
                else if (e.ColumnIndex == CollectionErrorStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabDBADashErrorLog" });
                    }
                    else
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = -1, Instance = (string)row["InstanceGroupName"], Tab = "tabDBADashErrorLog" });
                    }
                }
                else if (e.ColumnIndex == SnapshotAgeStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabCollectionDates" });
                }
                else if (e.ColumnIndex == AlertStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabAlerts" });
                    }
                }
                else if (e.ColumnIndex == ElasticPoolStorageStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabAzureSummary" });
                }
                else if (e.ColumnIndex == AGStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabAG" });
                }
                else if (e.ColumnIndex == QueryStoreStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabQS" });
                }
                else if (e.ColumnIndex == UptimeStatus.Index)
                {
                    var frm = new UptimeThresholdConfig() { InstanceID = (Int32)row["InstanceID"] };
                    frm.ShowDialog();
                    if (frm.DialogResult == DialogResult.OK)
                    {
                        RefreshData();
                    }
                }
                else if (e.ColumnIndex == IdentityStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabIdentityColumns" });
                }
            }
        }

        private void FocusedViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RefreshData();
        }

        private void UpdateRefreshTime()
        {
            lblRefreshTime.Text = "Refresh Time: " + DateHelper.ToAppTimeZone(lastRefresh).ToString();
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (DateTime.UtcNow.Subtract(lastRefresh).TotalMinutes > 60)
            {
                lblRefreshTime.ForeColor = DBADashStatusEnum.Critical.GetColor();
                timer1.Enabled = false;
            }
            else if (DateTime.UtcNow.Subtract(lastRefresh).TotalMinutes > 10)
            {
                lblRefreshTime.ForeColor = DBADashStatusEnum.Warning.GetColor();
            }
        }

        private void ConfigureThresholdsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var frm = new MemoryDumpThresholdsConfig();
            frm.ShowDialog(this);
            if (frm.DialogResult == DialogResult.OK)
            {
                RefreshData();
            }
        }

        private void AcknowledgeDumpsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MemoryDumpThresholds.Acknowledge();
            MessageBox.Show("Memory dump acknowledge date updated", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshData();
        }

        private void DgvTests_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            for (Int32 idx = e.RowIndex; idx < e.RowIndex + e.RowCount; idx += 1)
            {
                var gridRow = dgvTests.Rows[idx];
                var row = (DataRowView)gridRow.DataBoundItem;
                gridRow.Cells["Test"].SetStatusColor(DBADashStatusEnum.NA);
                if ((int)row[DBADashStatusEnum.OK.ToString()] > 0)
                {
                    gridRow.Cells[DBADashStatusEnum.OK.ToString()].SetStatusColor(DBADashStatusEnum.OK);
                    gridRow.Cells["Test"].SetStatusColor(DBADashStatusEnum.OK);
                }
                else
                {
                    gridRow.Cells[DBADashStatusEnum.OK.ToString()].SetStatusColor(DBADashStatusEnum.NA);
                }
                if ((int)row[DBADashStatusEnum.Warning.ToString()] > 0)
                {
                    gridRow.Cells[DBADashStatusEnum.Warning.ToString()].SetStatusColor(DBADashStatusEnum.Warning);
                    gridRow.Cells["Test"].SetStatusColor(DBADashStatusEnum.Warning);
                }
                else
                {
                    gridRow.Cells[DBADashStatusEnum.Warning.ToString()].SetStatusColor(DBADashStatusEnum.OK);
                }
                if ((int)row[DBADashStatusEnum.Critical.ToString()] > 0)
                {
                    gridRow.Cells[DBADashStatusEnum.Critical.ToString()].SetStatusColor(DBADashStatusEnum.Critical);
                    gridRow.Cells["Test"].SetStatusColor(DBADashStatusEnum.Critical);
                }
                else
                {
                    gridRow.Cells[DBADashStatusEnum.Critical.ToString()].SetStatusColor(DBADashStatusEnum.OK);
                }
                gridRow.Cells[DBADashStatusEnum.NA.ToString()].SetStatusColor(DBADashStatusEnum.NA);
            }
        }

        private void DgvTests_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataRowView row = (DataRowView)dgvTests.Rows[e.RowIndex].DataBoundItem;
                string test = (string)row["Test"];
                string tab = (string)tabMapping[test];
                if (e.ColumnIndex == 0)
                {
                    if (string.IsNullOrEmpty(tab))
                    {
                        FilterByStatus(new List<DBADashStatusEnum>() { DBADashStatusEnum.Warning, DBADashStatusEnum.Critical }, test);
                    }
                    else
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = context.InstanceID, Instance = context.InstanceName, Tab = tab });
                    }
                }
                else if (e.ColumnIndex >= 1 && e.ColumnIndex <= 4)
                {
                    DBADashStatusEnum status = e.ColumnIndex switch
                    {
                        1 => DBADashStatusEnum.OK,
                        2 => DBADashStatusEnum.Warning,
                        3 => DBADashStatusEnum.Critical,
                        4 => DBADashStatusEnum.NA,
                        _ => throw new Exception("Invalid ColumnIndex"),
                    };
                    FilterByStatus(status, test);
                }
            }
        }

        private void FilterByStatus(DBADashStatusEnum status, string test)
        {
            FilterByStatus(new List<DBADashStatusEnum>() { status }, test);
        }

        private void FilterByStatus(List<DBADashStatusEnum> statuses, string test)
        {
            var dv = (DataView)dgvSummary.DataSource;
            StringBuilder sbFilter = new();
            foreach (DBADashStatusEnum status in statuses)
            {
                if (sbFilter.Length > 0)
                {
                    sbFilter.Append(" OR ");
                }
                sbFilter.Append(test + " = " + Convert.ToInt32(status).ToString());
            }
            dv.RowFilter = sbFilter.ToString();
            HideStatusColumns();
            dgvSummary.Columns[test].Visible = true;
            tsClearFilter.Enabled = true;
        }

        private void TsClearFilter_Click(object sender, EventArgs e)
        {
            var dv = (DataView)dgvSummary.DataSource;
            dv.RowFilter = SummaryRowFilter;
            tsClearFilter.Enabled = false;
            SetStatusColumnVisiblity();
        }

        private void ShowTestSummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            splitContainer1.Panel1Collapsed = !showTestSummaryToolStripMenuItem.Checked;
        }

        private void ExportSummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Common.PromptSaveDataGridView(ref dgvSummary);
        }

        private void ExportTestSummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Common.PromptSaveDataGridView(ref dgvTests);
        }

        private void CopySummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Common.CopyDataGridViewToClipboard(dgvSummary);
        }

        private void CopyTestSummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Common.CopyDataGridViewToClipboard(dgvTests);
        }

        private void SaveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SummarySavedView saved = new()
            {
                ShowHidden = Common.ShowHidden,
                ShowTestSummary = showTestSummaryToolStripMenuItem.Checked,
                FocusedView = focusedViewToolStripMenuItem.Checked,
                Name = "Default"
            };
            try
            {
                saved.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving view options\n", ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}