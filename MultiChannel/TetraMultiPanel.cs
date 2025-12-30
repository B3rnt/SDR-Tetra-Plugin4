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

            leftTop.Controls.AddRange(new Control[] { _add, _remove, _save });

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
