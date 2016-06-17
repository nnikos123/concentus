﻿using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Silk.Structs
{
    /// <summary>
    /// VAD state
    /// </summary>
    internal class SilkVADState
    {
        /// <summary>
        /// Analysis filterbank state: 0-8 kHz
        /// </summary>
        internal readonly Pointer<int> AnaState = Pointer.Malloc<int>(2);

        /// <summary>
        /// Analysis filterbank state: 0-4 kHz
        /// </summary>
        internal readonly Pointer<int> AnaState1 = Pointer.Malloc<int>(2);

        /// <summary>
        /// Analysis filterbank state: 0-2 kHz
        /// </summary>
        internal readonly Pointer<int> AnaState2 = Pointer.Malloc<int>(2);

        /// <summary>
        /// Subframe energies
        /// </summary>
        internal readonly Pointer<int> XnrgSubfr = Pointer.Malloc<int>(SilkConstants.VAD_N_BANDS);

        /// <summary>
        /// Smoothed energy level in each band
        /// </summary>
        internal readonly Pointer<int> NrgRatioSmth_Q8 = Pointer.Malloc<int>(SilkConstants.VAD_N_BANDS);

        /// <summary>
        /// State of differentiator in the lowest band
        /// </summary>
        internal short HPstate = 0;

        /// <summary>
        /// Noise energy level in each band
        /// </summary>
        internal readonly Pointer<int> NL = Pointer.Malloc<int>(SilkConstants.VAD_N_BANDS);

        /// <summary>
        /// Inverse noise energy level in each band
        /// </summary>
        internal readonly Pointer<int> inv_NL = Pointer.Malloc<int>(SilkConstants.VAD_N_BANDS);

        /// <summary>
        /// Noise level estimator bias/offset
        /// </summary>
        internal readonly Pointer<int> NoiseLevelBias = Pointer.Malloc<int>(SilkConstants.VAD_N_BANDS);

        /// <summary>
        /// Frame counter used in the initial phase
        /// </summary>
        internal int counter = 0;

        internal void Reset()
        {
            AnaState.MemSet(0, 2);
            AnaState1.MemSet(0, 2);
            AnaState2.MemSet(0, 2);
            XnrgSubfr.MemSet(0, SilkConstants.VAD_N_BANDS);
            NrgRatioSmth_Q8.MemSet(0, SilkConstants.VAD_N_BANDS);
            HPstate = 0;
            NL.MemSet(0, SilkConstants.VAD_N_BANDS);
            inv_NL.MemSet(0, SilkConstants.VAD_N_BANDS);
            NoiseLevelBias.MemSet(0, SilkConstants.VAD_N_BANDS);
            counter = 0;
        }
    }
}