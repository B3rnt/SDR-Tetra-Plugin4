using SDRSharp.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SDRSharp.Tetra.MultiChannel
{
    public class TetraMultiPanel : UserControl
    {
        private readonly ISharpControl _control;
        private readonly WideIqSource _wideSource;

        private readonly BindingList<ChannelSettings> _channels;
        private readonly Dictionary<Guid, TetraChannelRunner> _runners = new();

        private DataGridView _grid;
        private Button _add;
        private Button _remove;
        private Button _save;
        private Button _scanMcch;
        private TabControl _tabs;

        public TetraMultiPanel(ISharpControl control)
        {
            _control = control;

            // Load channels first (so GUI is ready even if IQ hook fails)
            var list = ChannelSettingsStore.Load();
            _channels = new BindingList<ChannelSettings>(list);

            BuildUi();

            // Start wide IQ source
            _wideSource = new WideIqSource(_control);

            // Create runners for existing channels
            foreach (var ch in _channels.ToList())
                EnsureRunner(ch);

            _grid.DataSource = _channels;
            _grid.CellEndEdit += (_, __) => OnGridChanged();
            _grid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (_, __) => OnGridChanged();
        }

        private void BuildUi()
        {
            this.Dock = DockStyle.Fill;

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 360
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ChannelSettings.Enabled), HeaderText = "On", Width = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChannelSettings.Name), HeaderText = "Naam", Width = 110 });

            var freqCol = new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChannelSettings.FrequencyHz), HeaderText = "Frequentie (Hz)", Width = 120 };
            _grid.Columns.Add(freqCol);

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ChannelSettings.AgcEnabled), HeaderText = "AGC", Width = 45 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChannelSettings.AgcTargetRms), HeaderText = "AGC tgt", Width = 70 });

            var leftTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.LeftToRight };
            _add = new Button { Text = "Toevoegen", Width = 90 };
            _remove = new Button { Text = "Verwijderen", Width = 90 };
            _save = new Button { Text = "Opslaan", Width = 70 };
            _scanMcch = new Button { Text = "Scan MCCH", Width = 90 };

            leftTop.Controls.AddRange(new Control[] { _add, _remove, _save, _scanMcch });

            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_grid);
            left.Controls.Add(leftTop);

            _tabs = new TabControl { Dock = DockStyle.Fill };

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(_tabs);

            this.Controls.Add(split);

            _add.Click += (_, __) => AddChannel();
            _remove.Click += (_, __) => RemoveSelected();
            _save.Click += (_, __) => SaveAll();
            _scanMcch.Click += (_, __) => ScanMcchAndAddChannels();
            _grid.SelectionChanged += (_, __) => SyncSelectedTab();
        }

        private void AddChannel()
        {
            var ch = new ChannelSettings
            {
                Name = "TETRA-" + (_channels.Count + 1),
                FrequencyHz = _control.Frequency, // start at current tuned frequency
                Enabled = true
            };
            _channels.Add(ch);
            EnsureRunner(ch);
            SaveAll();
        }

        private void RemoveSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;
            var ch = _grid.SelectedRows[0].DataBoundItem as ChannelSettings;
            if (ch == null) return;

            if (_runners.TryGetValue(ch.Id, out var runner))
            {
                _wideSource.RemoveSink(runner);
                runner.Dispose();
                _runners.Remove(ch.Id);
            }

            // Remove tab
            for (int i = _tabs.TabPages.Count - 1; i >= 0; i--)
            {
                if (_tabs.TabPages[i].Tag is Guid id && id == ch.Id)
                    _tabs.TabPages.RemoveAt(i);
            }

            _channels.Remove(ch);
            SaveAll();
        }

        private void EnsureRunner(ChannelSettings ch)
        {
            if (_runners.ContainsKey(ch.Id))
                return;

            var runner = new TetraChannelRunner(_control, ch);
            _runners[ch.Id] = runner;
            _wideSource.AddSink(runner);

            var tp = new TabPage($"{ch.Name}  ({FormatHz(ch.FrequencyHz)})")
            {
                Tag = ch.Id
            };
            runner.Gui.Dock = DockStyle.Fill;
            tp.Controls.Add(runner.Gui);
            _tabs.TabPages.Add(tp);
        }

        private void OnGridChanged()
        {
            // Update runner settings and tab titles
            foreach (var ch in _channels)
            {
                EnsureRunner(ch);
                if (_runners.TryGetValue(ch.Id, out var runner))
                    runner.UpdateSettings(ch);

                for (int i = 0; i < _tabs.TabPages.Count; i++)
                {
                    if (_tabs.TabPages[i].Tag is Guid id && id == ch.Id)
                        _tabs.TabPages[i].Text = $"{ch.Name}  ({FormatHz(ch.FrequencyHz)})";
                }
            }
        }

        private void SyncSelectedTab()
        {
            if (_grid.SelectedRows.Count == 0) return;
            if (!(_grid.SelectedRows[0].DataBoundItem is ChannelSettings ch)) return;

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                if (_tabs.TabPages[i].Tag is Guid id && id == ch.Id)
                {
                    _tabs.SelectedIndex = i;
                    break;
                }
            }
        }

        private void SaveAll()
        {
            ChannelSettingsStore.Save(_channels.ToList());
        }


        private async void ScanMcchAndAddChannels()
        {
            if (_scanRunning) return;
            _scanRunning = true;

            try
            {
                _add.Enabled = _remove.Enabled = _save.Enabled = _scanMcch.Enabled = false;

                // Determine the current wideband span (RawIQ)
                var centerHz = GetCenterFrequencyHz(_control);
                // Sample rate is taken from the wideband IQ hook. (ISharpControl
                // does not expose SampleRate in all SDR# builds.)
                double fs = _wideSource.LastSampleRate;
                if (fs <= 1)
                {
                    MessageBox.Show("Kan sample rate niet bepalen. Start de receiver eerst en probeer opnieuw.");
                    return;
                }

                // Candidate frequencies: 25 kHz raster within the currently open IQ bandwidth
                var step = 25_000L;
                var half = (long)(fs / 2.0);
                var guard = 40_000L; // keep away from edges where filters roll off
                var start = centerHz - half + guard;
                var end = centerHz + half - guard;

                // Snap to 25 kHz grid
                start = (start / step) * step;
                end = (end / step) * step;

                var existing = new HashSet<long>(_channels.Select(c => c.FrequencyHz));

                // Scan ALL 25 kHz raster frequencies within the visible IQ bandwidth.
                // (We will only *add* the ones that are not already present.)
                var candidates = new List<long>();
                for (long f = start; f <= end; f += step)
                {
                    if (f <= 0) continue;
                    candidates.Add(f);
                }

                if (candidates.Count == 0)
                {
                    MessageBox.Show("Geen 25 kHz kanalen binnen de huidige bandbreedte (controleer sample rate / center)." );
                    return;
                }

                var found = new HashSet<long>();

                // Scan in small batches to avoid CPU spikes
                const int batchSize = 12;
                const int batchTimeoutMs = 6000;

                for (int i = 0; i < candidates.Count; i += batchSize)
                {
                    var batch = candidates.Skip(i).Take(batchSize).ToList();
                    var runners = new List<TetraChannelRunner>();

                    try
                    {
                        foreach (var f in batch)
                        {
                            var tmp = new ChannelSettings
                            {
                                Name = "SCAN",
                                FrequencyHz = f,
                                Enabled = true,
                                // Enable AGC for probes so they lock more reliably across varying levels
                                AgcEnabled = true,
                                AgcTargetRms = 0.25f
                            };

                            var r = new TetraChannelRunner(_control, tmp);
                            // Probes are headless: ensure the demodulator is actually started
                            // (normally the user ticks the "Demodulator" checkbox).
                            r.Panel.SetDemodulatorEnabled(true);
                            r.Panel.SysInfoBroadcastReceived += () =>
                            {
                                lock (found) { found.Add(f); }
                            };

                            runners.Add(r);
                            _wideSource.AddSink(r);
                        }

                        // Wait a bit so each probe can catch a broadcast burst
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < batchTimeoutMs)
                            await System.Threading.Tasks.Task.Delay(100);
                    }
                    finally
                    {
                        foreach (var r in runners)
                        {
                            try { _wideSource.RemoveSink(r); } catch { }
                            try { r.Dispose(); } catch { }
                        }
                    }
                }

                // Add discovered MCCH channels (only the missing ones)
                var toAdd = found.Where(f => !existing.Contains(f)).OrderBy(f => f).ToList();
                if (found.Count == 0)
                {
                    MessageBox.Show("Geen MCCH gevonden binnen de huidige bandbreedte.");
                    return;
                }
                if (toAdd.Count == 0)
                {
                    MessageBox.Show($"MCCH gevonden binnen de huidige bandbreedte ({found.Count}), maar ze staan al in de lijst.");
                    return;
                }

                foreach (var f in toAdd)
                {
                    var ch = new ChannelSettings
                    {
                        Name = "MCCH-" + FormatHz(f),
                        FrequencyHz = f,
                        Enabled = true
                    };
                    _channels.Add(ch);
                    EnsureRunner(ch);
                }

                SaveAll();
                MessageBox.Show($"MCCH gevonden en toegevoegd: {toAdd.Count} kanaal/kanalen.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Scan MCCH fout: " + ex.Message);
            }
            finally
            {
                _add.Enabled = _remove.Enabled = _save.Enabled = _scanMcch.Enabled = true;
                _scanRunning = false;
            }
        }

        private bool _scanRunning;

		private static long GetCenterFrequencyHz(ISharpControl control)
		{
			try
			{
				// Prefer CenterFrequency if available (RawIQ is centered on hardware LO)
				var pCenter = control.GetType().GetProperty("CenterFrequency");
				if (pCenter != null)
				{
					var v = pCenter.GetValue(control, null);
					if (v is long l) return l;
					if (v is int i) return i;
					if (v is double d) return (long)d;
				}

				// Fall back to the currently tuned frequency.
				return control.Frequency;
			}
			catch
			{
				return control.Frequency;
			}
		}




        public void Shutdown()
        {
            SaveAll();
            foreach (var r in _runners.Values)
            {
                try { _wideSource.RemoveSink(r); } catch { }
                try { r.Dispose(); } catch { }
            }
            try { _wideSource.Dispose(); } catch { }
        }

        private static string FormatHz(long hz)
        {
            if (hz <= 0) return "0 Hz";
            if (hz >= 1_000_000) return (hz / 1_000_000.0).ToString("0.000###", CultureInfo.InvariantCulture) + " MHz";
            if (hz >= 1_000) return (hz / 1_000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";
            return hz.ToString(CultureInfo.InvariantCulture) + " Hz";
        }
    }
}
