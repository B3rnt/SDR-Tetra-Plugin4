using SDRSharp.Radio;
using System;
using System.Runtime.CompilerServices;

namespace SDRSharp.Tetra
{
    internal class Demodulator : IDisposable
    {
        private const float Pi = (float)Math.PI;
        private const float TwoPi = (float)(Math.PI * 2.0);
        private const float PiDivTwo = (float)(Math.PI / 2.0);
        
        // Veilige maximale buffer grootte voor high-speed devices (Airspy/SDRPlay)
        // 65536 samples * max interpolation (normaal 4-8x) is ruim voldoende.
        private const int MaxInputBuffer = 65536; 

        // Training sequences
        private static readonly byte[] NormalTrainingSequence1 = { 1, 1, 0, 1, 0, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 1, 0, 0 };
        private static readonly byte[] NormalTrainingSequence2 = { 0, 1, 1, 1, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0 };
        private static readonly byte[] SynchronizationTrainingSequence = { 1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1 };

        private UnsafeBuffer _buffer;
        private unsafe Complex* _bufferPtr;
        private UnsafeBuffer _tempBuffer;
        private unsafe float* _tempBufferPtr;
        
        private IQFirFilter _matchedFilter;
        private FirFilter _fsFilter;

        private double _samplerateIn;
        private double _samplerate;
        private int _interpolation;
        private int _filterLength;
        
        // Training buffers
        private UnsafeBuffer _nts1Buffer;
        private unsafe float* _nts1BufferPtr;
        private UnsafeBuffer _nts2Buffer;
        private unsafe float* _nts2BufferPtr;
        private UnsafeBuffer _stsBuffer;
        private unsafe float* _stsBufferPtr;

        private int[] _symbolIndexMap; 
        
        private int _writeAddress;
        private double _symbolLength;
        private int _windowLength;
        private int _tailBufferLength;
        private int _syncCounter;
        
        // Offsets
        private int _ntsOffsetTMO;
        private int _ntsOffsetDMO;
        private int _stsOffset;

        public unsafe void ProcessBuffer(Burst burst, Complex* iqBuffer, double iqSamplerate, int iqBufferLength, float* digitalBuffer)
        {
            burst.Type = BurstType.WaitBurst;
            burst.Length = 0;

            // Alleen re-initialiseren als de sample rate daadwerkelijk verandert.
            // We negeren veranderingen in iqBufferLength, zolang het maar onder MaxInputBuffer blijft.
            if (_buffer == null || Math.Abs(iqSamplerate - _samplerateIn) > 1.0)
            {
                Initialize(iqSamplerate);
            }
            
            // Veiligheidscheck: als de buffer groter is dan onze interne allocatie, kappen we het af
            // om crashes te voorkomen. (Dit zou met 65536 zelden moeten gebeuren).
            int processLength = (iqBufferLength > MaxInputBuffer) ? MaxInputBuffer : iqBufferLength;
            int interpolatedLength = processLength * _interpolation;

            // 1. Interpolation / Copy
            if (_interpolation > 1)
            {
                int srcIdx = 0;
                // Zero-stuffing for interpolation
                for (int i = 0; i < interpolatedLength; i++)
                {
                    _bufferPtr[i + _tailBufferLength] = (i % _interpolation == 0) ? iqBuffer[srcIdx++] : default;
                }
            }
            else
            {
                Utils.Memcpy((void*)(_bufferPtr + _tailBufferLength), (void*)iqBuffer, interpolatedLength * sizeof(Complex));
            }

            // 2. Filter & Differential Phase
            _matchedFilter.Process(_bufferPtr + _tailBufferLength, interpolatedLength);

            // Check boundaries
            int maxWrite = _tempBuffer.Length;
            if (_writeAddress + interpolatedLength < maxWrite)
            {
                // Calculate differential phase: Arg(Current * Conjugate(Prev))
                for (int i = 0; i < interpolatedLength; i++)
                {
                    _tempBufferPtr[_writeAddress++] = (_bufferPtr[i + _tailBufferLength] * _bufferPtr[i].Conjugate()).ArgumentFast();
                }
            }
            else 
            {
                // Buffer overflow protection: reset pointers if we drift too far
                _writeAddress = 0;
                burst.Type = BurstType.WaitBurst;
                return;
            }

            // Move tail for next burst (overlap)
            for (int i = 0; i < _tailBufferLength; i++)
            {
                _bufferPtr[i] = _bufferPtr[interpolatedLength + i];
            }

            // 3. Synchronization Search
            if (_writeAddress < _windowLength * 2) return;

            int ntsOffset = (burst.Mode == Mode.TMO) ? _ntsOffsetTMO : _ntsOffsetDMO;
            
            int trainingWindow = (_syncCounter > 0) ? (6 * _tailBufferLength) : _windowLength;
            if (_syncCounter > 0) _syncCounter--;

            // Zorg dat we niet buiten de buffer lezen tijdens search
            if (trainingWindow + ntsOffset + _nts1Buffer.Length >= _writeAddress) 
            {
                 // Nog niet genoeg data voor een volledige search
                 return; 
            }

            float minNdb1 = float.MaxValue;
            float minNdb2 = float.MaxValue;
            float minSts = float.MaxValue;
            
            int idxNdb1 = 0;
            int idxNdb2 = 0;
            int idxSts = 0;

            // Hot loop: Correlation against training sequences
            for (int i = 0; i < trainingWindow; i++)
            {
                float sum1 = 0, sum2 = 0, sumS = 0;

                // NTS1
                for (int k = 0; k < _nts1Buffer.Length; k++)
                {
                    float diff = _nts1BufferPtr[k] - _tempBufferPtr[i + k + ntsOffset];
                    if (diff > Pi) diff -= TwoPi;
                    else if (diff < -Pi) diff += TwoPi;
                    sum1 += diff * diff;
                }
                if (sum1 < minNdb1) { minNdb1 = sum1; idxNdb1 = i; }

                // NTS2
                for (int k = 0; k < _nts2Buffer.Length; k++)
                {
                    float diff = _nts2BufferPtr[k] - _tempBufferPtr[i + k + ntsOffset];
                    if (diff > Pi) diff -= TwoPi;
                    else if (diff < -Pi) diff += TwoPi;
                    sum2 += diff * diff;
                }
                if (sum2 < minNdb2) { minNdb2 = sum2; idxNdb2 = i; }

                // STS
                for (int k = 0; k < _stsBuffer.Length; k++)
                {
                    float diff = _stsBufferPtr[k] - _tempBufferPtr[i + k + _stsOffset];
                    if (diff > Pi) diff -= TwoPi;
                    else if (diff < -Pi) diff += TwoPi;
                    sumS += diff * diff;
                }
                if (sumS < minSts) { minSts = sumS; idxSts = i; }
            }

            // Normalize errors
            minNdb1 /= _nts1Buffer.Length;
            minNdb2 /= _nts2Buffer.Length;
            minSts /= _stsBuffer.Length;

            // 4. Decision
            int offset = 0;
            if (minNdb1 < 1.0f || minNdb2 < 1.0f || minSts < 1.0f)
            {
                if (minNdb1 < minNdb2 && minNdb1 < minSts)
                {
                    offset = idxNdb1;
                    burst.Type = BurstType.NDB1;
                }
                else if (minNdb2 < minNdb1 && minNdb2 < minSts)
                {
                    offset = idxNdb2;
                    burst.Type = BurstType.NDB2;
                }
                else
                {
                    offset = idxSts;
                    burst.Type = BurstType.SYNC;
                }
                _syncCounter = 8; 
            }
            else
            {
                burst.Type = BurstType.None;
                offset = _tailBufferLength * 2;
            }

            // 5. Symbol Extraction
            int samplesUsed = 0;
            for (int i = 0; i < 256; i++)
            {
                int sampleIdx = _symbolIndexMap[i]; 
                
                // Bounds check voor extractie
                if ((offset + sampleIdx) < _writeAddress)
                {
                    digitalBuffer[i] = _tempBufferPtr[offset + sampleIdx];
                }
                else
                {
                    digitalBuffer[i] = 0; // Padding als we aan het eind lopen
                }
                samplesUsed = sampleIdx;
            }
            
            // Advance buffer
            offset += samplesUsed;
            offset -= _tailBufferLength * 2;
            if (offset < 0) offset = 0;

            _writeAddress -= offset;
            if (_writeAddress < 0) _writeAddress = 0;

            // Move remaining data to front
            Utils.Memcpy((void*)_tempBufferPtr, (void*)(_tempBufferPtr + offset), _writeAddress * sizeof(float));

            AngleToSymbol(burst.Ptr, digitalBuffer, 255);
            burst.Length = 510;
        }

        private unsafe void Initialize(double iqSamplerate)
        {
            FreeBuffers();

            _samplerateIn = iqSamplerate;
            _interpolation = 1;
            while (_samplerateIn * _interpolation < 90000.0)
                _interpolation++;
            
            _samplerate = _samplerateIn * _interpolation;
            
            // We alloceren nu ALTIJD de maximale buffer, ongeacht de huidige input
            int maxInternalLen = MaxInputBuffer * _interpolation;

            _symbolLength = _samplerate / 18000.0;
            _windowLength = (int)(_symbolLength * 255.0 + 0.5);
            _tailBufferLength = (int)(_symbolLength + 0.5);

            _buffer = UnsafeBuffer.Create(maxInternalLen + _tailBufferLength, sizeof(Complex));
            _bufferPtr = (Complex*)(void*)_buffer;
            
            // Temp buffer moet groot genoeg zijn voor continue stroom
            _tempBuffer = UnsafeBuffer.Create(maxInternalLen * 2, sizeof(float));
            _tempBufferPtr = (float*)(void*)_tempBuffer;

            // Filters
            _filterLength = Math.Max((int)(_samplerate / 18000.0) | 1, 5);
            float[] coeffs = FilterBuilder.MakeSinc((float)_samplerate, 13500f, _filterLength);
            _matchedFilter = new IQFirFilter(coeffs);
            _fsFilter = new FirFilter(coeffs);

            // Pre-calculate Offsets
            _ntsOffsetTMO = (int)(122.0 * _symbolLength + 0.5);
            _ntsOffsetDMO = (int)(115.0 * _symbolLength + 0.5);
            _stsOffset = (int)(107.0 * _symbolLength + 0.5);

            // Pre-calculate Resampling Map
            _symbolIndexMap = new int[256];
            for (int i = 0; i < 256; i++)
            {
                _symbolIndexMap[i] = (int)((i * _symbolLength) + 0.5);
            }

            CreateFrameSynchronization();
            _writeAddress = 0;
            _syncCounter = 0;
        }

        private unsafe void CreateFrameSynchronization()
        {
            double symLen = _samplerate / 18000.0;
            CreateFsBuffers(symLen);

            PopulateTrainingBuffer(NormalTrainingSequence1, _nts1BufferPtr, _nts1Buffer.Length, symLen);
            PopulateTrainingBuffer(NormalTrainingSequence2, _nts2BufferPtr, _nts2Buffer.Length, symLen);
            PopulateTrainingBuffer(SynchronizationTrainingSequence, _stsBufferPtr, _stsBuffer.Length, symLen);

            _fsFilter.Process(_nts1BufferPtr, _nts1Buffer.Length);
            _fsFilter.Process(_nts2BufferPtr, _nts2Buffer.Length);
            _fsFilter.Process(_stsBufferPtr, _stsBuffer.Length);
        }

        private unsafe void PopulateTrainingBuffer(byte[] seq, float* buffer, int len, double symLen)
        {
            int symIdx = 0;
            for (int i = 0; i < len; i++)
            {
                if (((i + symLen * 0.5) % symLen) < 1.0)
                {
                    buffer[i] = SymbolToAngle(seq, symIdx++);
                }
                else
                {
                    buffer[i] = 0.0f;
                }
            }
        }

        private unsafe void CreateFsBuffers(double symLen)
        {
            _nts1Buffer?.Dispose();
            _nts2Buffer?.Dispose();
            _stsBuffer?.Dispose();

            int lenNts = (int)(symLen * NormalTrainingSequence1.Length * 0.5) | 1;
            int lenSts = (int)(symLen * SynchronizationTrainingSequence.Length * 0.5) | 1;

            _nts1Buffer = UnsafeBuffer.Create(lenNts, sizeof(float));
            _nts1BufferPtr = (float*)(void*)_nts1Buffer;
            _nts2Buffer = UnsafeBuffer.Create(lenNts, sizeof(float));
            _nts2BufferPtr = (float*)(void*)_nts2Buffer;
            _stsBuffer = UnsafeBuffer.Create(lenSts, sizeof(float));
            _stsBufferPtr = (float*)(void*)_stsBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float SymbolToAngle(byte[] seq, int idx)
        {
            if (idx >= seq.Length) return 0.0f;
            float val = (seq[idx * 2 + 1] == 1) ? 2.356194f : 0.7853982f; 
            return (seq[idx * 2] == 1) ? -val : val;
        }

        private unsafe void AngleToSymbol(byte* bitsBuffer, float* angles, int count)
        {
            while (count-- > 0)
            {
                float a = *angles++;
                *bitsBuffer++ = (a < 0) ? (byte)1 : (byte)0;
                *bitsBuffer++ = (Math.Abs(a) > PiDivTwo) ? (byte)1 : (byte)0;
            }
        }

        private unsafe void FreeBuffers()
        {
            _buffer?.Dispose(); _buffer = null;
            _tempBuffer?.Dispose(); _tempBuffer = null;
            _nts1Buffer?.Dispose(); _nts1Buffer = null;
            _nts2Buffer?.Dispose(); _nts2Buffer = null;
            _stsBuffer?.Dispose(); _stsBuffer = null;
        }

        public void Dispose()
        {
            FreeBuffers();
            _matchedFilter = null;
            _fsFilter = null;
            GC.SuppressFinalize(this);
        }
    }
}
