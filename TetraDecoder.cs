using SDRSharp.Radio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace SDRSharp.Tetra
{
    unsafe class TetraDecoder
    {
        public delegate void DataReadyDelegate(List<ReceivedData> data);
        public event DataReadyDelegate DataReady;

        public delegate void SyncInfoReadyDelegate(ReceivedData syncInfo);
        public event SyncInfoReadyDelegate SyncInfoReady;

        // UI batching
        private readonly object _dataBatchLock = new object();
        private readonly List<ReceivedData> _batchedData = new List<ReceivedData>(256);
        private bool _flushPosted;
        private long _lastFlushTicks;
        private const int FlushIntervalMs = 100;
        
        // OPTIMIZATION: Pre-calculate ticks per interval to avoid float math in hot path
        private static readonly long _flushIntervalTicks = (long)(FlushIntervalMs / 1000.0 * Stopwatch.Frequency);

        private PhyLevel _phyLevel = new PhyLevel();
        private LowerMacLevel _lowerMac = new LowerMacLevel();
        private MacLevel _parse = new MacLevel();

        private UnsafeBuffer _bbBuffer;
        private byte* _bbBufferPtr;
        private UnsafeBuffer _bkn1Buffer;
        private byte* _bkn1BufferPtr;
        private UnsafeBuffer _bkn2Buffer;
        private byte* _bkn2BufferPtr;
        private UnsafeBuffer _sb1Buffer;
        private byte* _sb1BufferPtr;

        private LogicChannel _logicChannel = new LogicChannel();
        private NetworkTime _networkTime = new NetworkTime();

        private ReceivedData _syncInfo = new ReceivedData();
        private List<ReceivedData> _data = new List<ReceivedData>();
        
        private int _timeCounter;
        private float _badBurstCounter;
        private float _averageBer;
        
        private Control _owner;
        private ITetraDecoderHost _panel;
        
        private int _fpass;
        private unsafe void* _ch1InitStruct;
        private unsafe void* _ch2InitStruct;
        private unsafe void* _ch3InitStruct;
        private unsafe void* _ch4InitStruct;

        short[] _cdc = new short[276];
        short[] _sdc = new short[480];
        private bool _haveErrors;

        private readonly TetraRuntimeContext _runtime = new TetraRuntimeContext();

        public int NetworkTimeTN { get; internal set; }
        public int NetworkTimeFN { get; internal set; }
        public int NetworkTimeMN { get; internal set; }
        public float Mer { get; set; }
        public Mode TetraMode { get; set; }
        public bool BurstReceived { get; internal set; }
        public float Ber { get; internal set; }
        public bool HaveErrors { get { return _haveErrors; } }

        public TetraDecoder(ITetraDecoderHost owner)
        {
            _panel = owner;
            _owner = owner as Control;
            if (_owner == null)
                throw new ArgumentException("Decoder host must be a WinForms Control", nameof(owner));

            _bbBuffer = UnsafeBuffer.Create(30);
            _bbBufferPtr = (byte*)_bbBuffer;
            _bkn1Buffer = UnsafeBuffer.Create(216);
            _bkn1BufferPtr = (byte*)_bkn1Buffer;
            _bkn2Buffer = UnsafeBuffer.Create(216);
            _bkn2BufferPtr = (byte*)_bkn2Buffer;
            _sb1Buffer = UnsafeBuffer.Create(120);
            _sb1BufferPtr = (byte*)_sb1Buffer;

            _fpass = 1;

            _ch1InitStruct = NativeMethods.tetra_decode_init();
            _ch2InitStruct = NativeMethods.tetra_decode_init();
            _ch3InitStruct = NativeMethods.tetra_decode_init();
            _ch4InitStruct = NativeMethods.tetra_decode_init();
        }

        public void Dispose()
        {
            _bbBuffer?.Dispose(); _bbBuffer = null;
            _bkn1Buffer?.Dispose(); _bkn1Buffer = null;
            _bkn2Buffer?.Dispose(); _bkn2Buffer = null;
            _sb1Buffer?.Dispose(); _sb1Buffer = null;

            (_phyLevel as IDisposable)?.Dispose(); _phyLevel = null;
            (_lowerMac as IDisposable)?.Dispose(); _lowerMac = null;
            (_parse as IDisposable)?.Dispose(); _parse = null;
        }

        public int Process(Burst burst, float* audioOut)
        {
            var prevRuntime = TetraRuntime.Current;
            TetraRuntime.Current = _runtime;
            try
            {
                bool mmOnly = _panel != null && _panel.MmOnlyMode;
                var trafficChannel = 0;

                BurstReceived = (burst.Type != BurstType.None);

                _timeCounter++;
                Ber = _averageBer;
                if (_timeCounter > 100)
                {
                    Mer = (_badBurstCounter / _timeCounter) * 100.0f;
                    _timeCounter = 0;
                    _badBurstCounter = 0;
                }

                _networkTime.AddTimeSlot();

                if (burst.Type == BurstType.None)
                {
                    _haveErrors = true;
                    if (TetraMode == Mode.TMO) _badBurstCounter++;
                    return trafficChannel;
                }

                _haveErrors = false;
                _parse.ResetAACH();
                _data.Clear();
                _syncInfo.Clear();

                // SYNC BURST
                if (burst.Type == BurstType.SYNC)
                {
                    _phyLevel.ExtractSBChannels(burst, _sb1BufferPtr);
                    _logicChannel = _lowerMac.ExtractLogicChannelFromSB(_sb1BufferPtr, _sb1Buffer.Length);
                    
                    _badBurstCounter += _logicChannel.CrcIsOk ? 0.0f : 0.5f;
                    _haveErrors |= !_logicChannel.CrcIsOk;
                    _averageBer = _averageBer * 0.5f + _lowerMac.Ber * 0.5f;

                    if (_logicChannel.CrcIsOk)
                    {
                        _parse.SyncPDU(_logicChannel, _syncInfo);

                        if (_syncInfo.Value(GlobalNames.SystemCode) < 8)
                        {
                            TetraMode = Mode.TMO;
                            _lowerMac.ScramblerCode = TetraUtils.CreateScramblerCode(_syncInfo.Value(GlobalNames.MCC), _syncInfo.Value(GlobalNames.MNC), _syncInfo.Value(GlobalNames.ColorCode));
                            _networkTime.Synchronize(_syncInfo.Value(GlobalNames.TimeSlot), _syncInfo.Value(GlobalNames.Frame), _syncInfo.Value(GlobalNames.MultiFrame));
                        }
                        else
                        {
                            TetraMode = Mode.DMO;
                            if (_syncInfo.Value(GlobalNames.SYNC_PDU_type) == 0)
                            {
                                if (_syncInfo.Value(GlobalNames.Master_slave_link_flag) == 1 || _syncInfo.Value(GlobalNames.Communication_type) == 0)
                                    _networkTime.SynchronizeMaster(_syncInfo.Value(GlobalNames.TimeSlot), _syncInfo.Value(GlobalNames.Frame));
                                else
                                    _networkTime.SynchronizeSlave(_syncInfo.Value(GlobalNames.TimeSlot), _syncInfo.Value(GlobalNames.Frame));
                            }
                        }
                    }

                    _phyLevel.ExtractPhyChannels(TetraMode, burst, _bbBufferPtr, _bkn1BufferPtr, _bkn2BufferPtr);

                    if (TetraMode == Mode.TMO)
                    {
                        _logicChannel = _lowerMac.ExtractLogicChannelFromBKN(_bkn2BufferPtr, _bkn2Buffer.Length);
                        _logicChannel.TimeSlot = _networkTime.TimeSlot;
                        _logicChannel.Frame = _networkTime.Frame;

                        _badBurstCounter += _logicChannel.CrcIsOk ? 0.0f : 0.5f;
                        _haveErrors |= !_logicChannel.CrcIsOk;
                        _averageBer = _averageBer * 0.5f + _lowerMac.Ber * 0.5f;
                        if (_logicChannel.CrcIsOk) _parse.TmoParseMacPDU(_logicChannel, _data);
                    }
                    else
                    {
                        _logicChannel = _lowerMac.ExtractLogicChannelFromBKN2(_bkn2BufferPtr, _bkn2Buffer.Length);
                        _logicChannel.TimeSlot = _networkTime.TimeSlot;
                        _logicChannel.Frame = _networkTime.Frame;

                        _badBurstCounter += _logicChannel.CrcIsOk ? 0.0f : 0.5f;
                        _haveErrors |= !_logicChannel.CrcIsOk;
                        _averageBer = _averageBer * 0.5f + _lowerMac.Ber * 0.5f;
                        
                        if (_logicChannel.CrcIsOk)
                        {
                            _parse.SyncPDUHalfSlot(_logicChannel, _syncInfo);
                            // DMO Scrambler update...
                             if (_syncInfo.Value(GlobalNames.Communication_type) == 0)
                            {
                                if (_syncInfo.Contains(GlobalNames.MNC) && _syncInfo.Contains(GlobalNames.Source_address))
                                    _lowerMac.ScramblerCode = TetraUtils.CreateScramblerCode(_syncInfo.Value(GlobalNames.MNC), _syncInfo.Value(GlobalNames.Source_address));
                            }
                            else if (_syncInfo.Value(GlobalNames.Communication_type) == 1)
                            {
                                if (_syncInfo.Contains(GlobalNames.Repeater_address) && _syncInfo.Contains(GlobalNames.Source_address))
                                    _lowerMac.ScramblerCode = TetraUtils.CreateScramblerCode(_syncInfo.Value(GlobalNames.Repeater_address), _syncInfo.Value(GlobalNames.Source_address));
                            }
                        }
                    }
                    UpdateSyncInfo(_syncInfo);
                }

                UpdatePublicProp();

                // OTHER BURSTS (NDB)
                if (burst.Type != BurstType.SYNC)
                {
                    _phyLevel.ExtractPhyChannels(TetraMode, burst, _bbBufferPtr, _bkn1BufferPtr, _bkn2BufferPtr);

                    if (TetraMode == Mode.TMO)
                    {
                        _logicChannel = _lowerMac.ExtractLogicChannelFromBB(_bbBufferPtr, _bbBuffer.Length);
                        _logicChannel.TimeSlot = _networkTime.TimeSlot;
                        _logicChannel.Frame = _networkTime.Frame;

                        _badBurstCounter += _logicChannel.CrcIsOk ? 0.0f : 0.2f;
                        _haveErrors |= !_logicChannel.CrcIsOk;
                        if (_logicChannel.CrcIsOk) _parse.AccessAsignPDU(_logicChannel);
                    }

                    switch (burst.Type)
                    {
                        case BurstType.NDB1:
                            if (IsTrafficChannel())
                            {
                                if (!mmOnly)
                                {
                                    _logicChannel = _lowerMac.ExtractVoiceDataFromBKN1BKN2(_bkn1BufferPtr, _bkn2BufferPtr, _bkn1Buffer.Length);
                                    var itsAudio = DecodeAudio(audioOut, _logicChannel.Ptr, _logicChannel.Length, false, _networkTime.TimeSlot);
                                    trafficChannel = itsAudio ? _networkTime.TimeSlot : 0;
                                }
                            }
                            else
                            {
                                _logicChannel = _lowerMac.ExtractLogicChannelFromBKN1BKN2(_bkn1BufferPtr, _bkn2BufferPtr, _bkn1Buffer.Length);
                                ProcessSignalingChannel(ref _logicChannel);
                            }
                            break;

                        case BurstType.NDB2:
                            // Slot 1
                            _logicChannel = _lowerMac.ExtractLogicChannelFromBKN(_bkn1BufferPtr, _bkn1Buffer.Length);
                            ProcessSignalingChannel(ref _logicChannel);

                            // Slot 2
                             if (IsTrafficChannel() && !_parse.HalfSlotStolen)
                            {
                                if (!mmOnly)
                                {
                                    _logicChannel = _lowerMac.ExtractVoiceDataFromBKN2(_bkn2BufferPtr, _bkn2Buffer.Length);
                                    var itsAudio = DecodeAudio(audioOut, _logicChannel.Ptr, _logicChannel.Length, true, _networkTime.TimeSlot);
                                    trafficChannel = itsAudio ? _networkTime.TimeSlot : 0;
                                }
                            }
                            else
                            {
                                _logicChannel = _lowerMac.ExtractLogicChannelFromBKN(_bkn2BufferPtr, _bkn2Buffer.Length);
                                ProcessSignalingChannel(ref _logicChannel);
                            }
                            break;
                    }
                }

                if (_data.Count > 0) UpdateData(_data);
                return trafficChannel;
            }
            finally
            {
                TetraRuntime.Current = prevRuntime;
            }
        }

        private bool IsTrafficChannel()
        {
             return ((TetraMode == Mode.TMO) && (_parse.DownLinkChannelType == ChannelType.Traffic))
                    || ((TetraMode == Mode.DMO) && (!_networkTime.Frame18) && (_networkTime.TimeSlot == 1))
                    || ((TetraMode == Mode.DMO) && (!_networkTime.Frame18Slave) && (_networkTime.TimeSlotSlave == 1));
        }

        private void ProcessSignalingChannel(ref LogicChannel ch)
        {
            ch.TimeSlot = _networkTime.TimeSlot;
            ch.Frame = _networkTime.Frame;
            _badBurstCounter += ch.CrcIsOk ? 0.0f : 0.4f;
            _haveErrors |= !ch.CrcIsOk;
            _averageBer = _averageBer * 0.5f + _lowerMac.Ber * 0.5f;

            if (ch.CrcIsOk)
            {
                if (TetraMode == Mode.TMO) _parse.TmoParseMacPDU(ch, _data);
                else _parse.DmoParseMacPDU(ch, _data);
            }
        }

        private void UpdateSyncInfo(ReceivedData syncInfo)
        {
            if (SyncInfoReady == null || _owner == null || _owner.IsDisposed || !_owner.IsHandleCreated) return;
            try { _owner.BeginInvoke(SyncInfoReady, syncInfo); } catch { }
        }

        public void UpdateData(List<ReceivedData> data)
        {
            if (DataReady == null || data == null || data.Count == 0) return;

            bool shouldPost = false;
            lock (_dataBatchLock)
            {
                _batchedData.AddRange(data);
                long nowTicks = Stopwatch.GetTimestamp();
                // OPTIMIZATION: Use precalculated ticks
                if (!_flushPosted && (nowTicks - _lastFlushTicks) >= _flushIntervalTicks)
                {
                    _flushPosted = true;
                    shouldPost = true;
                }
            }

            if (shouldPost)
            {
                if (_owner != null && !_owner.IsDisposed && _owner.IsHandleCreated)
                    try { _owner.BeginInvoke((Action)FlushBatchedData); } catch { }
            }
        }

        private void FlushBatchedData()
        {
            List<ReceivedData> snapshot = null;
            lock (_dataBatchLock)
            {
                if (_batchedData.Count == 0)
                {
                    _flushPosted = false;
                    _lastFlushTicks = Stopwatch.GetTimestamp();
                    return;
                }
                snapshot = new List<ReceivedData>(_batchedData.Count);
                snapshot.AddRange(_batchedData);
                _batchedData.Clear();
                _flushPosted = false;
                _lastFlushTicks = Stopwatch.GetTimestamp();
            }
            DataReady?.Invoke(snapshot);
        }

        private void UpdatePublicProp()
        {
            if (_networkTime != null)
            {
                NetworkTimeFN = _networkTime.Frame;
                NetworkTimeTN = _networkTime.TimeSlot;
                NetworkTimeMN = _networkTime.SuperFrame;
            }
        }

        private bool DecodeAudio(float* audioBuffer, byte* buf, int length, bool stolten, int ch)
        {
            var noErrors = true;
            fixed (short* cdcPtr = _cdc, sdcPtr = _sdc)
            {
                NativeMethods.tetra_cdec(_fpass, buf, cdcPtr, stolten ? 1 : 0);
                _fpass = 0;
                noErrors = (cdcPtr[0] == 0) || (cdcPtr[138] == 0);
                _badBurstCounter += (cdcPtr[0] == 0 || stolten) ? 0 : 0.4f;
                _badBurstCounter += cdcPtr[138] == 0 ? 0 : 0.4f;

                var initStruct = _ch1InitStruct;
                switch (ch)
                {
                    case 1: initStruct = _ch1InitStruct; break;
                    case 2: initStruct = _ch2InitStruct; break;
                    case 3: initStruct = _ch3InitStruct; break;
                    case 4: initStruct = _ch4InitStruct; break;
                }

                NativeMethods.tetra_sdec(cdcPtr, sdcPtr, initStruct);
                
                // Vectorizable gain application
                float gain = 0.0001f / short.MaxValue;
                for (int i = 0; i < 480; i++) // 480 is standard length
                {
                    audioBuffer[i] = sdcPtr[i] * gain;
                }
            }
            return noErrors;
        }
    }
}
