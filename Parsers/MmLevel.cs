using System;
using System.Text;

namespace SDRSharp.Tetra
{
    unsafe class MmLevel
    {
        private readonly Rules[] _locationUpdateAcceptRules = new Rules[]
        {
            new Rules(GlobalNames.Location_update_accept_type, 3, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Options_bit, 1, RulesType.Options_bit, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.MM_SSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 24, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 16, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 14, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 6, RulesType.Reserved, 0, 0, 0)
        };

        private readonly Rules[] _locationUpdateCommandRules = new Rules[]
        {
            new Rules(GlobalNames.Group_identity_report, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Cipher_control, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Ciphering_parameters, 32, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Group_identity_acknowledgement_request, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Location_update_type, 2, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.MM_SSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.MM_Address_extension, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Reserved, 2, RulesType.Reserved, 0, 0, 0)
        };

        private readonly Rules[] _locationUpdateRejectRules = new Rules[]
        {
            new Rules(GlobalNames.Reject_cause, 4, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Options_bit, 1, RulesType.Options_bit, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.MM_SSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 24, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 16, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 14, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 6, RulesType.Reserved, 0, 0, 0)
        };

        private readonly Rules[] _locationUpdateProceedingRules = new Rules[]
        {
            new Rules(GlobalNames.Location_update_type, 2, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Options_bit, 1, RulesType.Options_bit, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.MM_SSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 24, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 16, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 14, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.Presence_bit, 1, RulesType.Presence_bit, 1, 0, 0),
            new Rules(GlobalNames.Reserved, 6, RulesType.Reserved, 0, 0, 0)
        };

        private readonly Rules[] _attachDetachGroupIdentityRules = new Rules[]
        {
            new Rules(GlobalNames.Group_identity_accept_reject, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Group_identity_attach_detach_mode, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Group_identity_report, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.MM_vGSSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Cipher_control, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Ciphering_parameters, 32, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Group_identity_acknowledgement_request, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Reserved, 3, RulesType.Reserved, 0, 0, 0)
        };

        private readonly Rules[] _attachDetachGroupIdentityAckRules = new Rules[]
        {
            new Rules(GlobalNames.Group_identity_attach_detach_mode, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Group_identity_accept_reject, 1, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Reserved, 2, RulesType.Reserved, 0, 0, 0),
            new Rules(GlobalNames.MM_SSI, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.MM_Address_extension, 24, RulesType.Direct, 0, 0, 0),
            new Rules(GlobalNames.Reserved, 4, RulesType.Reserved, 0, 0, 0)
        };

        public void Parse(LogicChannel channelData, int offset, ReceivedData result)
        {
            int mmStart = offset;

            result.SetValue(GlobalNames.GSSI, -1);
            result.SetValue(GlobalNames.MM_vGSSI, -1);
            result.SetValue(GlobalNames.CCK_id, -1);
            // 0=none, 1=candidate/fallback, 2=marker-verified (trusted)
            result.SetValue(GlobalNames.GSSI_verified, 0);
            result.SetValue(GlobalNames.ITSI_attach, 0);

            if (offset + 4 > channelData.Length)
            {
                result.SetValue(GlobalNames.OutOfBuffer, 1);
                return;
            }

            MmPduType mmType = (MmPduType)TetraUtils.BitsToInt32(channelData.Ptr, offset, 4);
            result.SetValue(GlobalNames.MM_PDU_Type, (int)mmType);
            offset += 4;

            switch (mmType)
            {
                case MmPduType.D_LOCATION_UPDATE_ACCEPT:
                    offset = Global.ParseParams(channelData, offset, _locationUpdateAcceptRules, result);
                    offset = ParseLocationUpdateAcceptExtensions(channelData, offset, mmStart, result);
                    break;

                case MmPduType.D_LOCATION_UPDATE_COMMAND:
                    offset = Global.ParseParams(channelData, offset, _locationUpdateCommandRules, result);
                    break;

                case MmPduType.D_LOCATION_UPDATE_REJECT:
                    offset = Global.ParseParams(channelData, offset, _locationUpdateRejectRules, result);
                    break;

                case MmPduType.D_LOCATION_UPDATE_PROCEEDING:
                    offset = Global.ParseParams(channelData, offset, _locationUpdateProceedingRules, result);
                    break;

                case MmPduType.D_ATTACH_DETACH_GROUP_IDENTITY:
                    offset = Global.ParseParams(channelData, offset, _attachDetachGroupIdentityRules, result);
                    break;

                case MmPduType.D_ATTACH_DETACH_GROUP_IDENTITY_ACKNOWLEDGEMENT:
                    offset = Global.ParseParams(channelData, offset, _attachDetachGroupIdentityAckRules, result);
                    break;

                case MmPduType.D_MM_STATUS:
                    // Was: alleen Status_downlink (6 bits)
                    // Nu: Status_downlink (6 bits) + MM_SSI (24 bits) direct uit PDU
                    if (offset + 6 <= channelData.Length)
                    {
                        result.SetValue(GlobalNames.Status_downlink, TetraUtils.BitsToInt32(channelData.Ptr, offset, 6));
                        offset += 6;

                        // ISSI (MM_SSI) toevoegen uit PDU
                        if (offset + 24 <= channelData.Length)
                        {
                            result.SetValue(GlobalNames.MM_SSI, TetraUtils.BitsToInt32(channelData.Ptr, offset, 24));
                            offset += 24;
                        }
                        else
                        {
                            // Geen fallback; alleen aangeven dat buffer te klein is
                            result.SetValue(GlobalNames.OutOfBuffer, 1);
                        }
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                case MmPduType.MM_PDU_FUNCTION_NOT_SUPPORTED:
                    if (offset + 4 <= channelData.Length)
                    {
                        result.SetValue(GlobalNames.Not_supported_sub_PDU_type, TetraUtils.BitsToInt32(channelData.Ptr, offset, 4));
                        offset += 4;
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                case MmPduType.D_OTAR:
                    if (offset + 4 <= channelData.Length)
                    {
                        int sub = TetraUtils.BitsToInt32(channelData.Ptr, offset, 4);
                        result.SetValue(GlobalNames.Otar_sub_type, sub);
                        offset += 4;

                        if (offset + 8 <= channelData.Length)
                        {
                            result.SetValue(GlobalNames.CCK_id, TetraUtils.BitsToInt32(channelData.Ptr, offset, 8));
                            offset += 8;
                        }
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                case MmPduType.D_AUTHENTICATION:
                    if (offset + 2 <= channelData.Length)
                    {
                        int sub = TetraUtils.BitsToInt32(channelData.Ptr, offset, 2);
                        result.SetValue(GlobalNames.Authentication_sub_type, sub);
                        offset += 2;

                        if ((sub == (int)D_AuthenticationPduSubType.Result || sub == (int)D_AuthenticationPduSubType.Reject) &&
                            offset + 6 <= channelData.Length)
                        {
                            result.SetValue(GlobalNames.Authentication_status, TetraUtils.BitsToInt32(channelData.Ptr, offset, 6));
                            offset += 6;
                        }
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                case MmPduType.D_CK_CHANGE_DEMAND:
                    if (offset + 1 <= channelData.Length)
                    {
                        result.SetValue(GlobalNames.CK_provision_flag, TetraUtils.BitsToInt32(channelData.Ptr, offset, 1));
                        offset += 1;
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                // D_ENABLE bestaat niet in jouw snippet; toegevoegd: lees direct 24-bit MM_SSI uit PDU.
                // Als jouw D_ENABLE anders is opgebouwd: zet dit op de juiste bitpositie/velden.
                case MmPduType.D_ENABLE:
                    if (offset + 24 <= channelData.Length)
                    {
                        result.SetValue(GlobalNames.MM_SSI, TetraUtils.BitsToInt32(channelData.Ptr, offset, 24));
                        offset += 24;
                    }
                    else result.SetValue(GlobalNames.OutOfBuffer, 1);
                    break;

                default:
                    break;
            }

            MmLogger.LogMmPdu(channelData, mmStart, channelData.Length - mmStart, result);
        }

        private static int ParseLocationUpdateAcceptExtensions(LogicChannel channelData, int offset, int mmStart, ReceivedData result)
        {
            try
            {
                // Find actual alignment: try mmStart+align where first byte is 0x57 or 0x51
                int align = FindLuAcceptAlignment(channelData, mmStart);
                byte luFirst = ReadByteAtBit(channelData, mmStart + align);

                bool isItsi = (luFirst == 0x57);
                bool isRoam = (luFirst == 0x51);

                result.SetValue(GlobalNames.ITSI_attach, isItsi ? 1 : 0);

                if (offset + 10 > channelData.Length)
                    return offset;

                int groupIdentityLocAccept = TetraUtils.BitsToInt32(channelData.Ptr, offset, 4);
                offset += 4;

                int defaultLifetime = TetraUtils.BitsToInt32(channelData.Ptr, offset, 6);
                offset += 6;

                // Marker scan window from the aligned start
                const int MARKER_SCAN_WINDOW_BITS = 512;

                bool markerRecovered = false;

                // bit-aligned scan + STRICT marker + blacklist 164443 to avoid false positives
                if (TryRecoverNibbleShiftedGssiBefore848D40_BitWindow_Strict(channelData, mmStart + align, MARKER_SCAN_WINDOW_BITS, out int recoveredGssi))
                {
                    result.SetValue(GlobalNames.GSSI, recoveredGssi);
                    result.SetValue(GlobalNames.GSSI_verified, 2); // marker-verified
                    markerRecovered = true;
                }

                // Parse GI list only as candidate
                int giListCandidate = -1;

                if (groupIdentityLocAccept != 0)
                {
                    while (offset + 2 <= channelData.Length)
                    {
                        int t = TetraUtils.BitsToInt32(channelData.Ptr, offset, 2);
                        offset += 2;

                        if (t == 3) break;

                        if (t == 0)
                        {
                            if (offset + 24 > channelData.Length) break;
                            int g = TetraUtils.BitsToInt32(channelData.Ptr, offset, 24);
                            offset += 24;

                            if (giListCandidate < 0) giListCandidate = g;
                            if (result.Value(GlobalNames.MM_vGSSI) <= 0) result.SetValue(GlobalNames.MM_vGSSI, g);
                        }
                        else if (t == 1)
                        {
                            if (offset + 48 > channelData.Length) break;
                            int g = TetraUtils.BitsToInt32(channelData.Ptr, offset, 24);
                            offset += 24;

                            if (giListCandidate < 0) giListCandidate = g;
                            if (result.Value(GlobalNames.MM_vGSSI) <= 0) result.SetValue(GlobalNames.MM_vGSSI, g);

                            offset += 24;
                        }
                        else if (t == 2)
                        {
                            if (offset + 24 > channelData.Length) break;
                            int vg = TetraUtils.BitsToInt32(channelData.Ptr, offset, 24);
                            offset += 24;

                            result.SetValue(GlobalNames.MM_vGSSI, vg);
                            if (giListCandidate < 0) giListCandidate = vg;
                        }
                        else break;
                    }
                }

                // ITSI attach: ONLY show marker-verified GSSI. If no marker -> no GSSI.
                if (isItsi && !markerRecovered)
                {
                    result.SetValue(GlobalNames.GSSI, -1);
                    result.SetValue(GlobalNames.GSSI_verified, 0);
                }

                // Roaming: allow fallback candidate if marker not present
                if (isRoam && !markerRecovered && giListCandidate > 0 && result.Value(GlobalNames.GSSI_verified) == 0)
                {
                    result.SetValue(GlobalNames.GSSI, giListCandidate);
                    result.SetValue(GlobalNames.GSSI_verified, 1);
                }

                // Unknown subtype: allow fallback only if not ITSI
                if (!isItsi && !isRoam && !markerRecovered && giListCandidate > 0 && result.Value(GlobalNames.GSSI_verified) == 0)
                {
                    result.SetValue(GlobalNames.GSSI, giListCandidate);
                    result.SetValue(GlobalNames.GSSI_verified, 1);
                }

                return offset;
            }
            catch
            {
                return offset;
            }
        }

        // Try 0..7 alignment to find LU Accept first byte (0x57 or 0x51)
        private static int FindLuAcceptAlignment(LogicChannel channelData, int mmStart)
        {
            try
            {
                for (int a = 0; a < 8; a++)
                {
                    if (mmStart + a + 8 > channelData.Length) break;
                    byte b = ReadByteAtBit(channelData, mmStart + a);
                    if (b == 0x57 || b == 0x51) return a;
                }
            }
            catch { }
            return 0;
        }

        // Marker scan op ELKE bit-offset binnen window + STRICT marker (84 8D 40 10) + BLACKLIST (164443)
        private static bool TryRecoverNibbleShiftedGssiBefore848D40_BitWindow_Strict(
            LogicChannel channelData, int scanStartBit, int windowBits, out int gssi)
        {
            gssi = -1;

            const int BLACKLIST_GSSI = 164443;

            try
            {
                int scanStart = Math.Max(0, scanStartBit);
                int scanEnd = Math.Min(channelData.Length, scanStartBit + Math.Max(0, windowBits));

                // marker(4 bytes) + 4 bytes ervoor
                if (scanEnd - scanStart < (8 * 8))
                    return false;

                for (int bit = scanStart; bit + (8 * 4) <= scanEnd; bit++)
                {
                    byte b1 = ReadByteAtBit(channelData, bit + (8 * 0));
                    byte b2 = ReadByteAtBit(channelData, bit + (8 * 1));
                    byte b3 = ReadByteAtBit(channelData, bit + (8 * 2));
                    byte b4 = ReadByteAtBit(channelData, bit + (8 * 3));

                    if (b1 == 0x84 && b2 == 0x8D && b3 == 0x40 && b4 == 0x10)
                    {
                        int pBase = bit - (8 * 4);
                        if (pBase < 0) continue;

                        byte p3 = ReadByteAtBit(channelData, pBase + (8 * 0));
                        byte p2 = ReadByteAtBit(channelData, pBase + (8 * 1));
                        byte p1 = ReadByteAtBit(channelData, pBase + (8 * 2));
                        byte p0 = ReadByteAtBit(channelData, pBase + (8 * 3));

                        int value =
                            ((p3 & 0x0F) << 20) |
                            (p2 << 12) |
                            (p1 << 4) |
                            ((p0 >> 4) & 0x0F);

                        // BLACKLIST: bekende vals-positieve waarde -> negeren en doorscannen
                        if (value == BLACKLIST_GSSI)
                            continue;

                        gssi = value;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static byte ReadByteAtBit(LogicChannel channelData, int bitOffset)
        {
            byte v = 0;
            for (int i = 0; i < 8; i++)
            {
                int bit = channelData.Ptr[bitOffset + i] & 0x1;
                v |= (byte)(bit << (7 - i));
            }
            return v;
        }
    }

    internal static unsafe class MmLogger
    {
        [ThreadStatic]
        private static StringBuilder _sbCache;
        private const int SbCacheMaxCapacity = 8192;
        private static readonly char[] Hex = "0123456789ABCDEF".ToCharArray();

        private static StringBuilder AcquireStringBuilder(int capacity)
        {
            var sb = _sbCache;
            if (sb == null)
                return new StringBuilder(capacity);

            _sbCache = null;
            sb.Clear();
            if (sb.Capacity < capacity)
                sb.Capacity = capacity;
            return sb;
        }

        private static string GetStringAndRelease(StringBuilder sb)
        {
            if (sb == null) return string.Empty;
            var s = sb.ToString();
            ReleaseStringBuilder(sb);
            return s;
        }

        private static void ReleaseStringBuilder(StringBuilder sb)
        {
            if (sb == null) return;
            if (sb.Capacity <= SbCacheMaxCapacity)
                _sbCache = sb;
        }

        private const string DefaultPath = "mm_messages.log";

        private static int _lastAuthStatus = -1;
        private static int _lastAuthSsi = -1;
        private static DateTime _lastAuthTime = DateTime.MinValue;

        public static void LogMmPdu(LogicChannel channelData, int bitOffset, int bitLength, ReceivedData parsed)
        {
            try
            {
                var sb = AcquireStringBuilder(512);

                sb.Append(DateTime.Now.ToString("HH:mm:ss"));
                sb.Append("  ");

                int la = parsed.Value(GlobalNames.Location_Area);
                if (la <= 0) la = TetraRuntime.CurrentLocationArea;

                if (la > 0)
                {
                    sb.Append("[LA: ");
                    sb.Append(la.ToString().PadLeft(4));
                    sb.Append("]   ");
                }
                else
                {
                    sb.Append("[LA:    ]   ");
                }

                MmPduType mmType = (MmPduType)parsed.Value(GlobalNames.MM_PDU_Type);

                int ssi = parsed.Value(GlobalNames.SSI);
                if (ssi <= 0) ssi = parsed.Value(GlobalNames.MM_SSI);

                int gssi = parsed.Value(GlobalNames.GSSI);
                int gssiVerified = parsed.Value(GlobalNames.GSSI_verified);
                int cckId = parsed.Value(GlobalNames.CCK_id);

                int align = 0;
                try
                {
                    for (int a = 0; a < 8; a++)
                    {
                        if (bitOffset + a + 8 > channelData.Length) break;
                        byte b = ReadByteAtBit(channelData, bitOffset + a);
                        if (b == 0x57 || b == 0x51) { align = a; break; }
                    }
                }
                catch { align = 0; }

                byte luFirst = ReadByteAtBit(channelData, bitOffset + align);
                bool isItsi = (luFirst == 0x57);
                bool isRoam = (luFirst == 0x51);

                switch (mmType)
                {
                    case MmPduType.D_AUTHENTICATION:
                    {
                        int sub = parsed.Value(GlobalNames.Authentication_sub_type);
                        int status = parsed.Value(GlobalNames.Authentication_status);

                        if (sub == (int)D_AuthenticationPduSubType.Result || sub == (int)D_AuthenticationPduSubType.Reject)
                        {
                            _lastAuthStatus = status;
                            _lastAuthSsi = ssi;
                            _lastAuthTime = DateTime.Now;
                        }

                        if (sub == (int)D_AuthenticationPduSubType.Demand)
                        {
                            sb.Append("BS demands authentication");
                            if (ssi > 0) { sb.Append(": SSI: "); sb.Append(ssi); }
                        }
                        else if (sub == (int)D_AuthenticationPduSubType.Result)
                        {
                            sb.Append("BS result to MS authentication: ");
                            sb.Append(AuthenticationStatusToString(status));
                            if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                            sb.Append(" - ");
                            sb.Append(AuthenticationStatusToString(status));
                        }
                        else
                        {
                            sb.Append("MM D_AUTHENTICATION auth_sub=");
                            sb.Append(sub);
                            if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                        }
                        break;
                    }

                    case MmPduType.D_LOCATION_UPDATE_ACCEPT:
                    {
                        int acc = parsed.Value(GlobalNames.Location_update_accept_type);

                        sb.Append("MS request for registration");
                        bool recentAuth = (_lastAuthSsi > 0 && _lastAuthSsi == ssi && (DateTime.Now - _lastAuthTime).TotalSeconds <= 3.0);
                        if (acc == 0 || recentAuth) sb.Append("/authentication ACCEPTED");
                        else sb.Append(" ACCEPTED");

                        if (ssi > 0) { sb.Append(" for SSI: "); sb.Append(ssi); }

                        if (isItsi)
                        {
                            if (gssiVerified == 2 && gssi > 0)
                            {
                                sb.Append(" GSSI: ");
                                sb.Append(gssi);
                            }
                        }
                        else
                        {
                            if (gssiVerified > 0 && gssi > 0)
                            {
                                sb.Append(" GSSI: ");
                                sb.Append(gssi);
                            }
                        }

                        if (_lastAuthStatus >= 0 && (_lastAuthSsi <= 0 || _lastAuthSsi == ssi))
                        {
                            sb.Append(" - ");
                            sb.Append(AuthenticationStatusToString(_lastAuthStatus));
                        }

                        if (cckId > 0)
                        {
                            sb.Append(" - CCK_identifier: ");
                            sb.Append(cckId);
                        }

                        if (isItsi) sb.Append(" - ITSI attach");
                        else if (isRoam) sb.Append(" - Roaming location updating");

                        break;
                    }

                    case MmPduType.D_LOCATION_UPDATE_COMMAND:
                    {
                        sb.Append("MM D_LOCATION_UPDATE_COMMAND");
                        if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                        break;
                    }

                    case MmPduType.D_MM_STATUS:
                    {
                        int st = parsed.Value(GlobalNames.Status_downlink);
                        sb.Append("MM D_MM_STATUS status=");
                        sb.Append(st);
                        if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                        break;
                    }

                    case MmPduType.D_ENABLE:
                    {
                        sb.Append("MM D_ENABLE");
                        if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                        break;
                    }

                    case MmPduType.D_OTAR:
                    {
                        sb.Append("MM D_OTAR");
                        break;
                    }

                    default:
                    {
                        sb.Append("MM ");
                        sb.Append(mmType.ToString());
                        if (ssi > 0) { sb.Append(" SSI: "); sb.Append(ssi); }
                        break;
                    }
                }

                // RAW LOGGING:
                // - Niet meer bij ITSI attach (LU accept isItsi) -> verwijderd
                // - Wel bij D_MM_STATUS, D_LOCATION_UPDATE_COMMAND, D_ENABLE
                bool logRaw =
                    (mmType == MmPduType.D_MM_STATUS) ||
                    (mmType == MmPduType.D_LOCATION_UPDATE_COMMAND) ||
                    (mmType == MmPduType.D_ENABLE);

                if (logRaw)
                {
                    sb.Append("  raw=");
                    sb.Append(BitsToHex(channelData.Ptr, bitOffset, bitLength));
                }

                var msg = sb.ToString();

                ReleaseStringBuilder(sb);
                new TextFile().Write(msg, DefaultPath);
            }
            catch
            {
            }
        }

        private static string AuthenticationStatusToString(int status)
        {
            if (status >= 0)
                return "Authentication successful or no authentication currently in progress";
            return "Authentication status unknown";
        }

        private static byte ReadByteAtBit(LogicChannel channelData, int bitOffset)
        {
            byte v = 0;
            for (int i = 0; i < 8; i++)
            {
                int bit = channelData.Ptr[bitOffset + i] & 0x1;
                v |= (byte)(bit << (7 - i));
            }
            return v;
        }

        private static string BitsToHex(byte* ptr, int bitOffset, int bitLength)
        {
            if (bitLength <= 0) return string.Empty;

            int byteLen = (bitLength + 7) / 8;
            var sb = AcquireStringBuilder(byteLen * 2);

            for (int b = 0; b < byteLen; b++)
            {
                int v = 0;
                int baseBit = b * 8;

                for (int j = 0; j < 8; j++)
                {
                    int i = baseBit + j;
                    int bit = (i < bitLength) ? (ptr[bitOffset + i] & 0x1) : 0;
                    v |= bit << (7 - j);
                }

                sb.Append(Hex[(v >> 4) & 0xF]);
                sb.Append(Hex[v & 0xF]);
            }

            var s = sb.ToString();
            ReleaseStringBuilder(sb);
            return s;
        }
    }
}
