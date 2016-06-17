﻿using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Silk.Structs
{
    /// <summary>
    /// Decoder control
    /// </summary>
    internal class SilkDecoderControl
    {
        /* Prediction and coding parameters */
        internal readonly Pointer<int> pitchL = Pointer.Malloc<int>(SilkConstants.MAX_NB_SUBFR);
        internal readonly Pointer<int> Gains_Q16 = Pointer.Malloc<int>(SilkConstants.MAX_NB_SUBFR);

        /* Holds interpolated and final coefficients, 4-byte aligned */
        // FIXME check alignment
        internal /*silk_DWORD_ALIGN*/ readonly Pointer<Pointer<short>> PredCoef_Q12 = Arrays.InitTwoDimensionalArrayPointer<short>(2, SilkConstants.MAX_LPC_ORDER);
        internal readonly Pointer<short> LTPCoef_Q14 = Pointer.Malloc<short>(SilkConstants.LTP_ORDER * SilkConstants.MAX_NB_SUBFR);
        internal int LTP_scale_Q14 = 0;

        internal void Reset()
        {
            pitchL.MemSet(0, SilkConstants.MAX_NB_SUBFR);
            Gains_Q16.MemSet(0, SilkConstants.MAX_NB_SUBFR);
            PredCoef_Q12[0].MemSet(0, SilkConstants.MAX_LPC_ORDER);
            PredCoef_Q12[1].MemSet(0, SilkConstants.MAX_LPC_ORDER);
            LTPCoef_Q14.MemSet(0, SilkConstants.LTP_ORDER * SilkConstants.MAX_NB_SUBFR);
            LTP_scale_Q14 = 0;
        }
    }
}
