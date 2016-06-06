﻿using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;
using Concentus.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Concentus.Silk.Enums;

namespace Concentus.Silk
{
    /*
     * Matrix of resampling methods used:
     *                                 Fs_out (kHz)
     *                        8      12     16     24     48
     *
     *               8        C      UF     U      UF     UF
     *              12        AF     C      UF     U      UF
     * Fs_in (kHz)  16        D      AF     C      UF     UF
     *              24        AF     D      AF     C      U
     *              48        AF     AF     AF     D      C
     *
     * C   . Copy (no resampling)
     * D   . Allpass-based 2x downsampling
     * U   . Allpass-based 2x upsampling
     * UF  . Allpass-based 2x upsampling followed by FIR interpolation
     * AF  . AR2 filter followed by FIR interpolation
     */

    public static class Resampler
    {
        private const int USE_silk_resampler_copy = 0;
        private const int USE_silk_resampler_private_up2_HQ_wrapper = 1;
        private const int USE_silk_resampler_private_IIR_FIR = 2;
        private const int USE_silk_resampler_private_down_FIR = 3;

        private const int ORDER_FIR = 4;

        /// <summary>
        /// Simple way to make [8000, 12000, 16000, 24000, 48000] to [0, 1, 2, 3, 4]
        /// </summary>
        /// <param name="R"></param>
        /// <returns></returns>
        private static int rateID(int R)
        {
            return (((((R) >> 12) - ((R > 16000) ? 1 : 0)) >> ((R > 24000) ? 1 : 0)) - 1);
        }
        
        /// <summary>
        /// Initialize/reset the resampler state for a given pair of input/output sampling rates
        /// </summary>
        /// <param name="S">I/O  Resampler state</param>
        /// <param name="Fs_Hz_in">I    Input sampling rate (Hz)</param>
        /// <param name="Fs_Hz_out">I    Output sampling rate (Hz)</param>
        /// <param name="forEnc">I    If 1: encoder; if 0: decoder</param>
        /// <returns></returns>
        public static int silk_resampler_init(
            silk_resampler_state_struct S,
            int Fs_Hz_in,
            int Fs_Hz_out,
            int forEnc)
        {
            int up2x;

            /* Clear state */
            S.Reset();

            /* Input checking */
            if (forEnc != 0)
            {
                if ((Fs_Hz_in != 8000 && Fs_Hz_in != 12000 && Fs_Hz_in != 16000 && Fs_Hz_in != 24000 && Fs_Hz_in != 48000) ||
                    (Fs_Hz_out != 8000 && Fs_Hz_out != 12000 && Fs_Hz_out != 16000))
                {
                    Debug.Assert(false);
                    return -1;
                }
                S.inputDelay = Tables.delay_matrix_enc[rateID(Fs_Hz_in),rateID(Fs_Hz_out)];
            }
            else {
                if ((Fs_Hz_in != 8000 && Fs_Hz_in != 12000 && Fs_Hz_in != 16000) ||
                    (Fs_Hz_out != 8000 && Fs_Hz_out != 12000 && Fs_Hz_out != 16000 && Fs_Hz_out != 24000 && Fs_Hz_out != 48000))
                {
                    Debug.Assert(false);
                    return -1;
                }
                S.inputDelay = Tables.delay_matrix_dec[rateID(Fs_Hz_in),rateID(Fs_Hz_out)];
            }

            S.Fs_in_kHz = Inlines.silk_DIV32_16(Fs_Hz_in, 1000);
            S.Fs_out_kHz = Inlines.silk_DIV32_16(Fs_Hz_out, 1000);

            /* Number of samples processed per batch */
            S.batchSize = S.Fs_in_kHz * SilkConstants.RESAMPLER_MAX_BATCH_SIZE_MS;

            /* Find resampler with the right sampling ratio */
            up2x = 0;
            if (Fs_Hz_out > Fs_Hz_in)
            {
                /* Upsample */
                if (Fs_Hz_out == Inlines.silk_MUL(Fs_Hz_in, 2))
                {                            /* Fs_out : Fs_in = 2 : 1 */
                                             /* Special case: directly use 2x upsampler */
                    S.resampler_function = USE_silk_resampler_private_up2_HQ_wrapper;
                }
                else {
                    /* Default resampler */
                    S.resampler_function = USE_silk_resampler_private_IIR_FIR;
                    up2x = 1;
                }
            }
            else if (Fs_Hz_out < Fs_Hz_in)
            {
                /* Downsample */
                S.resampler_function = USE_silk_resampler_private_down_FIR;
                if (Inlines.silk_MUL(Fs_Hz_out, 4) == Inlines.silk_MUL(Fs_Hz_in, 3))
                {             /* Fs_out : Fs_in = 3 : 4 */
                    S.FIR_Fracs = 3;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR0;
                    S.Coefs = Tables.silk_Resampler_3_4_COEFS.GetPointer();
                }
                else if (Inlines.silk_MUL(Fs_Hz_out, 3) == Inlines.silk_MUL(Fs_Hz_in, 2))
                {      /* Fs_out : Fs_in = 2 : 3 */
                    S.FIR_Fracs = 2;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR0;
                    S.Coefs = Tables.silk_Resampler_2_3_COEFS.GetPointer();
                }
                else if (Inlines.silk_MUL(Fs_Hz_out, 2) == Fs_Hz_in)
                {                     /* Fs_out : Fs_in = 1 : 2 */
                    S.FIR_Fracs = 1;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR1;
                    S.Coefs = Tables.silk_Resampler_1_2_COEFS.GetPointer();
                }
                else if (Inlines.silk_MUL(Fs_Hz_out, 3) == Fs_Hz_in)
                {                     /* Fs_out : Fs_in = 1 : 3 */
                    S.FIR_Fracs = 1;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR2;
                    S.Coefs = Tables.silk_Resampler_1_3_COEFS.GetPointer();
                }
                else if (Inlines.silk_MUL(Fs_Hz_out, 4) == Fs_Hz_in)
                {                     /* Fs_out : Fs_in = 1 : 4 */
                    S.FIR_Fracs = 1;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR2;
                    S.Coefs = Tables.silk_Resampler_1_4_COEFS.GetPointer();
                }
                else if (Inlines.silk_MUL(Fs_Hz_out, 6) == Fs_Hz_in)
                {                     /* Fs_out : Fs_in = 1 : 6 */
                    S.FIR_Fracs = 1;
                    S.FIR_Order = SilkConstants.RESAMPLER_DOWN_ORDER_FIR2;
                    S.Coefs = Tables.silk_Resampler_1_6_COEFS.GetPointer();
                }
                else
                {
                    /* None available */
                    Debug.Assert(false);
                    return -1;
                }
            }
            else
            {
                /* Input and output sampling rates are equal: copy */
                S.resampler_function = USE_silk_resampler_copy;
            }

            /* Ratio of input/output samples */
            S.invRatio_Q16 = Inlines.silk_LSHIFT32(Inlines.silk_DIV32(Inlines.silk_LSHIFT32(Fs_Hz_in, 14 + up2x), Fs_Hz_out), 2);

            /* Make sure the ratio is rounded up */
            while (Inlines.silk_SMULWW(S.invRatio_Q16, Fs_Hz_out) < Inlines.silk_LSHIFT32(Fs_Hz_in, up2x))
            {
                S.invRatio_Q16++;
            }

            return 0;
        }

        /// <summary>
        /// Resampler: convert from one sampling rate to another
        /// Input and output sampling rate are at most 48000 Hz
        /// </summary>
        /// <param name="S">I/O  Resampler state</param>
        /// <param name="output">O    Output signal</param>
        /// <param name="input">I    Input signal</param>
        /// <param name="inLen">I    Number of input samples</param>
        /// <returns></returns>
        public static int silk_resampler(
            silk_resampler_state_struct S,
            Pointer<short> output,
            Pointer<short> input,
            int inLen)
        {
            int nSamples;

            /* Need at least 1 ms of input data */
            Debug.Assert(inLen >= S.Fs_in_kHz);
            /* Delay can't exceed the 1 ms of buffering */
            Debug.Assert(S.inputDelay <= S.Fs_in_kHz);

            nSamples = S.Fs_in_kHz - S.inputDelay;

            Pointer<short> delayBufPtr = S.delayBuf;

            /* Copy to delay buffer */
            input.MemCopyTo(delayBufPtr.Point(S.inputDelay), nSamples);

            switch (S.resampler_function)
            {
                case USE_silk_resampler_private_up2_HQ_wrapper:
                    silk_resampler_private_up2_HQ(S.sIIR, output, delayBufPtr, S.Fs_in_kHz);
                    silk_resampler_private_up2_HQ(S.sIIR, output.Point(S.Fs_out_kHz), input.Point(nSamples), inLen - S.Fs_in_kHz);
                    break;
                case USE_silk_resampler_private_IIR_FIR:
                    silk_resampler_private_IIR_FIR(S, output, delayBufPtr, S.Fs_in_kHz);
                    silk_resampler_private_IIR_FIR(S, output.Point(S.Fs_out_kHz), input.Point(nSamples), inLen - S.Fs_in_kHz);
                    break;
                case USE_silk_resampler_private_down_FIR:
                    silk_resampler_private_down_FIR(S, output, delayBufPtr, S.Fs_in_kHz);
                    silk_resampler_private_down_FIR(S, output.Point(S.Fs_out_kHz), input.Point(nSamples), inLen - S.Fs_in_kHz);
                    break;
                default:
                    delayBufPtr.MemCopyTo(output, S.Fs_in_kHz);
                    input.Point(nSamples).MemCopyTo(output.Point(S.Fs_out_kHz), (inLen - S.Fs_in_kHz));
                    break;
            }

            /* Copy to delay buffer */
            input.Point(inLen - S.inputDelay).MemCopyTo(delayBufPtr, S.inputDelay);

            return SilkError.SILK_NO_ERROR;
        }

        /// <summary>
        /// Downsample by a factor 2
        /// </summary>
        /// <param name="S">I/O  State vector [ 2 ]</param>
        /// <param name="output">O    Output signal [ floor(len/2) ]</param>
        /// <param name="input">I    Input signal [ len ]</param>
        /// <param name="inLen">I    Number of input samples</param>
        public static void silk_resampler_down2(
            Pointer<int> S,
            Pointer<short> output,
            Pointer<short> input,
            int inLen)
        {
            int k, len2 = Inlines.silk_RSHIFT32(inLen, 1);
            int in32, out32, Y, X;

            Debug.Assert(Tables.silk_resampler_down2_0 > 0);
            Debug.Assert(Tables.silk_resampler_down2_1 < 0);

            /* Internal variables and state are in Q10 format */
            for (k = 0; k < len2; k++)
            {
                /* Convert to Q10 */
                in32 = Inlines.silk_LSHIFT((int)input[2 * k], 10);

                /* All-pass section for even input sample */
                Y = Inlines.silk_SUB32(in32, S[0]);
                X = Inlines.silk_SMLAWB(Y, Y, Tables.silk_resampler_down2_1);
                out32 = Inlines.silk_ADD32(S[0], X);
                S[0] = Inlines.silk_ADD32(in32, X);

                /* Convert to Q10 */
                in32 = Inlines.silk_LSHIFT((int)input[2 * k + 1], 10);

                /* All-pass section for odd input sample, and add to output of previous section */
                Y = Inlines.silk_SUB32(in32, S[1]);
                X = Inlines.silk_SMULWB(Y, Tables.silk_resampler_down2_0);
                out32 = Inlines.silk_ADD32(out32, S[1]);
                out32 = Inlines.silk_ADD32(out32, X);
                S[1] = Inlines.silk_ADD32(in32, X);

                /* Add, convert back to int16 and store to output */
                output[k] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(out32, 11));
            }
        }

        /// <summary>
        /// Downsample by a factor 2/3, low quality
        /// </summary>
        /// <param name="S">I/O  State vector [ 6 ]</param>
        /// <param name="output">O    Output signal [ floor(2*inLen/3) ]</param>
        /// <param name="input">I    Input signal [ inLen ]</param>
        /// <param name="inLen">I    Number of input samples</param>
        public static void silk_resampler_down2_3(
            Pointer<int> S,
            Pointer<short> output,
            Pointer<short> input,
            int inLen)
        {
            int nSamplesIn, counter, res_Q6;
            Pointer<int> buf = Pointer.Malloc<int>(SilkConstants.RESAMPLER_MAX_BATCH_SIZE_IN + ORDER_FIR);
            Pointer<int> buf_ptr;

            /* Copy buffered samples to start of buffer */
            S.MemCopyTo(buf, ORDER_FIR);

            /* Iterate over blocks of frameSizeIn input samples */
            while (true)
            {
                nSamplesIn = Inlines.silk_min(inLen, SilkConstants.RESAMPLER_MAX_BATCH_SIZE_IN);

                /* Second-order AR filter (output in Q8) */
                silk_resampler_private_AR2(S.Point(ORDER_FIR), buf.Point(ORDER_FIR), input,
                    Tables.silk_Resampler_2_3_COEFS_LQ.GetPointer(), nSamplesIn);

                /* Interpolate filtered signal */
                buf_ptr = buf;
                counter = nSamplesIn;
                while (counter > 2)
                {
                    /* Inner product */
                    res_Q6 = Inlines.silk_SMULWB(buf_ptr[0], Tables.silk_Resampler_2_3_COEFS_LQ[2]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[1], Tables.silk_Resampler_2_3_COEFS_LQ[3]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[2], Tables.silk_Resampler_2_3_COEFS_LQ[5]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[3], Tables.silk_Resampler_2_3_COEFS_LQ[4]);

                    /* Scale down, saturate and store in output array */
                    output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q6, 6));
                    output = output.Point(1);

                    res_Q6 = Inlines.silk_SMULWB(buf_ptr[1], Tables.silk_Resampler_2_3_COEFS_LQ[4]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[2], Tables.silk_Resampler_2_3_COEFS_LQ[5]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[3], Tables.silk_Resampler_2_3_COEFS_LQ[3]);
                    res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[4], Tables.silk_Resampler_2_3_COEFS_LQ[2]);

                    /* Scale down, saturate and store in output array */
                    output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q6, 6));
                    output = output.Point(1);

                    buf_ptr = buf_ptr.Point(3);
                    counter -= 3;
                }

                input = input.Point(nSamplesIn);
                inLen -= nSamplesIn;

                if (inLen > 0)
                {
                    /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                    buf.Point(nSamplesIn).MemCopyTo(buf, ORDER_FIR);
                }
                else
                {
                    break;
                }
            }

            /* Copy last part of filtered signal to the state for the next call */
            buf.Point(nSamplesIn).MemCopyTo(S, ORDER_FIR);
        }

        /// <summary>
        /// Second order AR filter with single delay elements
        /// </summary>
        /// <param name="S">I/O  State vector [ 2 ]</param>
        /// <param name="out_Q8">O    Output signal</param>
        /// <param name="input">I    Input signal</param>
        /// <param name="A_Q14">I    AR coefficients, Q14</param>
        /// <param name="len">I    Signal length</param>
        public static void silk_resampler_private_AR2(
            Pointer<int> S,
            Pointer<int> out_Q8,
            Pointer<short> input,
            Pointer<short> A_Q14,
            int len)
        {
            int k, out32;

            for (k = 0; k < len; k++)
            {
                out32 = Inlines.silk_ADD_LSHIFT32(S[0], (int)input[k], 8);
                out_Q8[k] = out32;
                out32 = Inlines.silk_LSHIFT(out32, 2);
                S[0] = Inlines.silk_SMLAWB(S[1], out32, A_Q14[0]);
                S[1] = Inlines.silk_SMULWB(out32, A_Q14[1]);
            }
        }

        public static Pointer<short> silk_resampler_private_down_FIR_INTERPOL(
            Pointer<short> output,
            Pointer<int> buf,
            Pointer<short> FIR_Coefs,
            int FIR_Order,
            int FIR_Fracs,
            int max_index_Q16,
            int index_increment_Q16)
        {
            int index_Q16, res_Q6;
            Pointer<int> buf_ptr;
            int interpol_ind;
            Pointer<short> interpol_ptr;

            switch (FIR_Order)
            {
                case SilkConstants.RESAMPLER_DOWN_ORDER_FIR0:
                    for (index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16)
                    {
                        /* Integer part gives pointer to buffered input */
                        buf_ptr = buf.Point(Inlines.silk_RSHIFT(index_Q16, 16));

                        /* Fractional part gives interpolation coefficients */
                        interpol_ind = Inlines.silk_SMULWB(index_Q16 & 0xFFFF, FIR_Fracs);

                        /* Inner product */
                        interpol_ptr = FIR_Coefs.Point(SilkConstants.RESAMPLER_DOWN_ORDER_FIR0 / 2 * interpol_ind);
                        res_Q6 = Inlines.silk_SMULWB(buf_ptr[0], interpol_ptr[0]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[1], interpol_ptr[1]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[2], interpol_ptr[2]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[3], interpol_ptr[3]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[4], interpol_ptr[4]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[5], interpol_ptr[5]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[6], interpol_ptr[6]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[7], interpol_ptr[7]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[8], interpol_ptr[8]);
                        interpol_ptr = FIR_Coefs.Point(SilkConstants.RESAMPLER_DOWN_ORDER_FIR0 / 2 * (FIR_Fracs - 1 - interpol_ind));
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[17], interpol_ptr[0]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[16], interpol_ptr[1]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[15], interpol_ptr[2]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[14], interpol_ptr[3]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[13], interpol_ptr[4]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[12], interpol_ptr[5]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[11], interpol_ptr[6]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[10], interpol_ptr[7]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, buf_ptr[9], interpol_ptr[8]);

                        /* Scale down, saturate and store in output array */
                        output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q6, 6));
                        output = output.Point(1);
                    }
                    break;
                case SilkConstants.RESAMPLER_DOWN_ORDER_FIR1:
                    for (index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16)
                    {
                        /* Integer part gives pointer to buffered input */
                        buf_ptr = buf.Point(Inlines.silk_RSHIFT(index_Q16, 16));

                        /* Inner product */
                        res_Q6 = Inlines.silk_SMULWB(Inlines.silk_ADD32(buf_ptr[0], buf_ptr[23]), FIR_Coefs[0]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[1], buf_ptr[22]), FIR_Coefs[1]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[2], buf_ptr[21]), FIR_Coefs[2]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[3], buf_ptr[20]), FIR_Coefs[3]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[4], buf_ptr[19]), FIR_Coefs[4]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[5], buf_ptr[18]), FIR_Coefs[5]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[6], buf_ptr[17]), FIR_Coefs[6]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[7], buf_ptr[16]), FIR_Coefs[7]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[8], buf_ptr[15]), FIR_Coefs[8]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[9], buf_ptr[14]), FIR_Coefs[9]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[10], buf_ptr[13]), FIR_Coefs[10]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[11], buf_ptr[12]), FIR_Coefs[11]);

                        /* Scale down, saturate and store in output array */
                        output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q6, 6));
                        output = output.Point(1);
                    }
                    break;
                case SilkConstants.RESAMPLER_DOWN_ORDER_FIR2:
                    for (index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16)
                    {
                        /* Integer part gives pointer to buffered input */
                        buf_ptr = buf.Point(Inlines.silk_RSHIFT(index_Q16, 16));

                        /* Inner product */
                        res_Q6 = Inlines.silk_SMULWB(Inlines.silk_ADD32(buf_ptr[0], buf_ptr[35]), FIR_Coefs[0]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[1], buf_ptr[34]), FIR_Coefs[1]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[2], buf_ptr[33]), FIR_Coefs[2]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[3], buf_ptr[32]), FIR_Coefs[3]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[4], buf_ptr[31]), FIR_Coefs[4]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[5], buf_ptr[30]), FIR_Coefs[5]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[6], buf_ptr[29]), FIR_Coefs[6]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[7], buf_ptr[28]), FIR_Coefs[7]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[8], buf_ptr[27]), FIR_Coefs[8]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[9], buf_ptr[26]), FIR_Coefs[9]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[10], buf_ptr[25]), FIR_Coefs[10]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[11], buf_ptr[24]), FIR_Coefs[11]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[12], buf_ptr[23]), FIR_Coefs[12]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[13], buf_ptr[22]), FIR_Coefs[13]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[14], buf_ptr[21]), FIR_Coefs[14]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[15], buf_ptr[20]), FIR_Coefs[15]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[16], buf_ptr[19]), FIR_Coefs[16]);
                        res_Q6 = Inlines.silk_SMLAWB(res_Q6, Inlines.silk_ADD32(buf_ptr[17], buf_ptr[18]), FIR_Coefs[17]);

                        /* Scale down, saturate and store in output array */
                        output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q6, 6));
                        output = output.Point(1);
                    }
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }

            return output;
        }

        /// <summary>
        /// Resample with a 2nd order AR filter followed by FIR interpolation
        /// </summary>
        /// <param name="S">I/O  Resampler state</param>
        /// <param name="output">O    Output signal</param>
        /// <param name="input">I    Input signal</param>
        /// <param name="inLen">I    Number of input samples</param>
        public static void silk_resampler_private_down_FIR(
            silk_resampler_state_struct S,
            Pointer<short> output,
            Pointer<short> input,
            int inLen)
        {
            int nSamplesIn;
            int max_index_Q16, index_increment_Q16;
            Pointer<int> buf = Pointer.Malloc<int>(S.batchSize + S.FIR_Order);
            Pointer<short> FIR_Coefs;

            /* Copy buffered samples to start of buffer */
            S.sFIR_i32.MemCopyTo(buf, S.FIR_Order);

            FIR_Coefs = S.Coefs.Point(2);

            /* Iterate over blocks of frameSizeIn input samples */
            index_increment_Q16 = S.invRatio_Q16;
            while (true)
            {
                nSamplesIn = Inlines.silk_min(inLen, S.batchSize);

                /* Second-order AR filter (output in Q8) */
                silk_resampler_private_AR2(S.sIIR, buf.Point(S.FIR_Order), input, S.Coefs, nSamplesIn);

                max_index_Q16 = Inlines.silk_LSHIFT32(nSamplesIn, 16);

                /* Interpolate filtered signal */
                output = silk_resampler_private_down_FIR_INTERPOL(output, buf, FIR_Coefs, S.FIR_Order,
                    S.FIR_Fracs, max_index_Q16, index_increment_Q16);

                input = input.Point(nSamplesIn);
                inLen -= nSamplesIn;

                if (inLen > 1)
                {
                    /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                    buf.Point(nSamplesIn).MemCopyTo(buf, S.FIR_Order);
                }
                else
                {
                    break;
                }
            }

            /* Copy last part of filtered signal to the state for the next call */
            buf.Point(nSamplesIn).MemCopyTo(S.sFIR_i32, S.FIR_Order);
        }

        public static Pointer<short> silk_resampler_private_IIR_FIR_INTERPOL(
            Pointer<short> output,
            Pointer<short> buf,
            int max_index_Q16,
            int index_increment_Q16)
        {
            int index_Q16, res_Q15;
            Pointer<short> buf_ptr;
            int table_index;

            /* Interpolate upsampled signal and store in output array */
            for (index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16)
            {
                table_index = Inlines.silk_SMULWB(index_Q16 & 0xFFFF, 12);
                buf_ptr = buf.Point(index_Q16 >> 16);

                res_Q15 = Inlines.silk_SMULBB(buf_ptr[0], Tables.silk_resampler_frac_FIR_12[table_index, 0]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[1], Tables.silk_resampler_frac_FIR_12[table_index, 1]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[2], Tables.silk_resampler_frac_FIR_12[table_index, 2]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[3], Tables.silk_resampler_frac_FIR_12[table_index, 3]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[4], Tables.silk_resampler_frac_FIR_12[11 - table_index, 3]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[5], Tables.silk_resampler_frac_FIR_12[11 - table_index, 2]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[6], Tables.silk_resampler_frac_FIR_12[11 - table_index, 1]);
                res_Q15 = Inlines.silk_SMLABB(res_Q15, buf_ptr[7], Tables.silk_resampler_frac_FIR_12[11 - table_index, 0]);
                output[0] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(res_Q15, 15));
                output = output.Point(1); // FIXME inefficient; should use an offset counter instead. See also other uses of this pattern in this file
            }
            return output;
        }

        /// <summary>
        /// Upsample using a combination of allpass-based 2x upsampling and FIR interpolation
        /// </summary>
        /// <param name="S">I/O  Resampler state</param>
        /// <param name="output">O    Output signal</param>
        /// <param name="input">I    Input signal</param>
        /// <param name="inLen">I    Number of input samples</param>
        public static void silk_resampler_private_IIR_FIR(
            silk_resampler_state_struct S,
            Pointer<short> output,
            Pointer<short> input,
            int inLen)
        {
            int nSamplesIn;
            int max_index_Q16, index_increment_Q16;

            Pointer<short> buf = Pointer.Malloc<short>(2 * S.batchSize + SilkConstants.RESAMPLER_ORDER_FIR_12);

            /* Copy buffered samples to start of buffer */
            S.sFIR_i16.MemCopyTo(buf, SilkConstants.RESAMPLER_ORDER_FIR_12);

            /* Iterate over blocks of frameSizeIn input samples */
            index_increment_Q16 = S.invRatio_Q16;
            while (true)
            {
                nSamplesIn = Inlines.silk_min(inLen, S.batchSize);

                /* Upsample 2x */
                silk_resampler_private_up2_HQ(S.sIIR, buf.Point(SilkConstants.RESAMPLER_ORDER_FIR_12), input, nSamplesIn);

                max_index_Q16 = Inlines.silk_LSHIFT32(nSamplesIn, 16 + 1);         /* + 1 because 2x upsampling */
                output = silk_resampler_private_IIR_FIR_INTERPOL(output, buf, max_index_Q16, index_increment_Q16);
                input = input.Point(nSamplesIn);
                inLen -= nSamplesIn;

                if (inLen > 0)
                {
                    /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                    buf.Point(nSamplesIn << 1).MemCopyTo(buf, SilkConstants.RESAMPLER_ORDER_FIR_12);
                }
                else
                {
                    break;
                }
            }

            /* Copy last part of filtered signal to the state for the next call */
            buf.Point(nSamplesIn << 1).MemCopyTo(S.sFIR_i16, SilkConstants.RESAMPLER_ORDER_FIR_12);
        }

        /// <summary>
        /// Upsample by a factor 2, high quality
        /// Uses 2nd order allpass filters for the 2x upsampling, followed by a
        /// notch filter just above Nyquist.
        /// </summary>
        /// <param name="S">I/O  Resampler state [ 6 ]</param>
        /// <param name="output">O    Output signal [ 2 * len ]</param>
        /// <param name="input">I    Input signal [ len ]</param>
        /// <param name="len">I    Number of input samples</param>
        public static void silk_resampler_private_up2_HQ(
            Pointer<int> S,
            Pointer<short> output,
            Pointer<short> input,
            int len)
        {
            int k;
            int in32, out32_1, out32_2, Y, X;

            Debug.Assert(Tables.silk_resampler_up2_hq_0[0] > 0);
            Debug.Assert(Tables.silk_resampler_up2_hq_0[1] > 0);
            Debug.Assert(Tables.silk_resampler_up2_hq_0[2] < 0);
            Debug.Assert(Tables.silk_resampler_up2_hq_1[0] > 0);
            Debug.Assert(Tables.silk_resampler_up2_hq_1[1] > 0);
            Debug.Assert(Tables.silk_resampler_up2_hq_1[2] < 0);

            /* Internal variables and state are in Q10 format */
            for (k = 0; k < len; k++)
            {
                /* Convert to Q10 */
                in32 = Inlines.silk_LSHIFT((int)input[k], 10);

                /* First all-pass section for even output sample */
                Y = Inlines.silk_SUB32(in32, S[0]);
                X = Inlines.silk_SMULWB(Y, Tables.silk_resampler_up2_hq_0[0]);
                out32_1 = Inlines.silk_ADD32(S[0], X);
                S[0] = Inlines.silk_ADD32(in32, X);

                /* Second all-pass section for even output sample */
                Y = Inlines.silk_SUB32(out32_1, S[1]);
                X = Inlines.silk_SMULWB(Y, Tables.silk_resampler_up2_hq_0[1]);
                out32_2 = Inlines.silk_ADD32(S[1], X);
                S[1] = Inlines.silk_ADD32(out32_1, X);

                /* Third all-pass section for even output sample */
                Y = Inlines.silk_SUB32(out32_2, S[2]);
                X = Inlines.silk_SMLAWB(Y, Y, Tables.silk_resampler_up2_hq_0[2]);
                out32_1 = Inlines.silk_ADD32(S[2], X);
                S[2] = Inlines.silk_ADD32(out32_2, X);

                /* Apply gain in Q15, convert back to int16 and store to output */
                output[2 * k] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(out32_1, 10));

                /* First all-pass section for odd output sample */
                Y = Inlines.silk_SUB32(in32, S[3]);
                X = Inlines.silk_SMULWB(Y, Tables.silk_resampler_up2_hq_1[0]);
                out32_1 = Inlines.silk_ADD32(S[3], X);
                S[3] = Inlines.silk_ADD32(in32, X);

                /* Second all-pass section for odd output sample */
                Y = Inlines.silk_SUB32(out32_1, S[4]);
                X = Inlines.silk_SMULWB(Y, Tables.silk_resampler_up2_hq_1[1]);
                out32_2 = Inlines.silk_ADD32(S[4], X);
                S[4] = Inlines.silk_ADD32(out32_1, X);

                /* Third all-pass section for odd output sample */
                Y = Inlines.silk_SUB32(out32_2, S[5]);
                X = Inlines.silk_SMLAWB(Y, Y, Tables.silk_resampler_up2_hq_1[2]);
                out32_1 = Inlines.silk_ADD32(S[5], X);
                S[5] = Inlines.silk_ADD32(out32_2, X);

                /* Apply gain in Q15, convert back to int16 and store to output */
                output[2 * k + 1] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(out32_1, 10));
            }
        }
    }
}