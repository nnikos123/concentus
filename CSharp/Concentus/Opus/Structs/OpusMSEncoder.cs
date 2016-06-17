﻿using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Structs
{
    public class OpusMSEncoder
    {
        internal readonly ChannelLayout layout = new ChannelLayout();
        internal int lfe_stream = 0;
        internal OpusApplication application = OpusApplication.OPUS_APPLICATION_AUDIO;
        internal OpusFramesize variable_duration = 0;
        internal int surround = 0;
        internal int bitrate_bps = 0;
        internal float[] subframe_mem = new float[3];
        internal OpusEncoder[] encoders = null;
        internal int[] window_mem = null;
        internal int[] preemph_mem = null;

        public OpusMSEncoder(int nb_streams, int nb_coupled_streams)
        {
            if (nb_streams < 1 || nb_coupled_streams > nb_streams || nb_coupled_streams < 0)
                throw new ArgumentException("Invalid channel count in MS encoder");

            encoders = new OpusEncoder[nb_streams];
            for (int c = 0; c < nb_streams; c++)
                encoders[c] = new OpusEncoder();
            // fixme is this nb_streams or nb_channels?
            window_mem = new int[nb_streams * 120];
            preemph_mem = new int[nb_streams];
        }

        // fixme: don't think this is used
        public void Reset()
        {
            layout.Reset();
            lfe_stream = 0;
            application = 0;
            variable_duration = 0;
            surround = 0;
            bitrate_bps = 0;
            Arrays.MemSet(subframe_mem, 0);
            encoders = null;
            window_mem = null;
            preemph_mem = null;
        }

        public void ResetState()
        {
            int s;
            subframe_mem[0] = subframe_mem[1] = subframe_mem[2] = 0;
            if (surround != 0)
            {
                Arrays.MemSet(preemph_mem, 0, layout.nb_channels);
                Arrays.MemSet(window_mem, 0, layout.nb_channels * 120);
            }
            int encoder_ptr = 0;
            for (s = 0; s < layout.nb_streams; s++)
            {
                OpusEncoder enc = encoders[encoder_ptr++];
                enc.ResetState();
            }
        }

        public void SetBitrate(int value)
        {
            if (value < 0 && value != OpusConstants.OPUS_AUTO && value != OpusConstants.OPUS_BITRATE_MAX)
            {
                throw new ArgumentException("Invalid bitrate");
            }
            bitrate_bps = value;
        }

        public int GetBitrate()
        {
            int s;
            int value = 0;
            int encoder_ptr = 0;
            for (s = 0; s < layout.nb_streams; s++)
            {
                OpusEncoder enc = encoders[encoder_ptr++];
                value += enc.GetBitrate();
            }
            return value;
        }

        public OpusApplication GetApplication()
        {
            return encoders[0].GetApplication();
        }
        
        public int GetForceChannels()
        {
            return encoders[0].GetForceChannels();
        }

        public int GetMaxBandwidth()
        {
            return encoders[0].GetMaxBandwidth();
        }

        public int GetBandwidth()
        {
            return encoders[0].GetBandwidth();
        }

        public bool GetUseDTX()
        {
            return encoders[0].GetUseDTX();
        }

        public int GetComplexity()
        {
            return encoders[0].GetComplexity();
        }

        public bool GetUseInbandFEC()
        {
            return encoders[0].GetUseInbandFEC();
        }
        
        public int GetPacketLossPercent()
        {
            return encoders[0].GetPacketLossPercent();
        }

        public bool GetVBR()
        {
            return encoders[0].GetVBR();
        }

        public int GetVoiceRatio()
        {
            return encoders[0].GetVoiceRatio();
        }

        public bool GetVBRConstraint()
        {
            return encoders[0].GetVBRConstraint();
        }

        public OpusSignal GetSignalType()
        {
            return encoders[0].GetSignalType();
        }

        public int GetLookahead()
        {
            return encoders[0].GetLookahead();
        }

        public int GetSampleRate()
        {
            return encoders[0].GetSampleRate();
        }

        public uint GetFinalRange()
        {
            int s;
            uint value = 0;
            int encoder_ptr = 0;
            for (s = 0; s < layout.nb_streams; s++)
            {
                value ^= encoders[encoder_ptr++].GetFinalRange();
            }
            return value;
        }

        public int GetLSBDepth()
        {
            return encoders[0].GetLSBDepth();
        }

        public bool GetPredictionDisabled()
        {
            return encoders[0].GetPredictionDisabled();
        }

        public void SetLSBDepth(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetLSBDepth(value);
            }
        }

        public void SetComplexity(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetComplexity(value);
            }
        }

        public void SetVBR(bool value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetVBR(value);
            }
        }

        public void SetVBRConstraint(bool value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetVBRConstraint(value);
            }
        }

        public void SetMaxBandwidth(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetMaxBandwidth(value);
            }
        }

        public void SetBandwidth(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetBandwidth(value);
            }
        }

        public void SetSignalType(OpusSignal value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetSignalType(value);
            }
        }

        public void SetApplication(OpusApplication value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetApplication(value);
            }
        }

        public void SetUseInbandFEC(bool value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetUseInbandFEC(value);
            }
        }

        public void SetPacketLossPercent(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetPacketLossPercent(value);
            }
        }

        public void SetUseDTX(bool value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetUseDTX(value);
            }
        }

        public void SetForceMode(OpusMode value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetForceMode(value);
            }
        }

        public void SetForceChannels(int value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetForceChannels(value);
            }
        }

        public void SetPredictionDisabled(bool value)
        {
            for (int encoder_ptr = 0; encoder_ptr < layout.nb_streams; encoder_ptr++)
            {
                encoders[encoder_ptr].SetPredictionDisabled(value);
            }
        }
        
        public OpusEncoder GetMultistreamEncoderState(int streamId)
        {
            if (streamId >= layout.nb_streams)
                throw new ArgumentException("Requested stream doesn't exist");
            return encoders[streamId];
        }

        public void SetExpertFrameDuration(OpusFramesize value)
        {
            variable_duration = value;
        }

        public OpusFramesize GetExpertFrameDuration()
        {
            return variable_duration;
        }
    }
}
