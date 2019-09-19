using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace E2EEClientCommandLine
{
    public static class Utils
    {
        /// <summary>Converts the given hex string to a byte array.</summary>
        public static byte[] HexStringToByteArray (string hex)
        {
            if (hex.Length % 2 == 1)
                throw new Exception("The binary key cannot have an odd number of digits");

            byte[] arr = new byte[hex.Length >> 1];

            for (int i = 0; i < hex.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
            }

            return arr;
        }

        /// <summary>Gets the hex value of the char.</summary>
        public static int GetHexVal (char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            //return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        // https://stackoverflow.com/questions/6111960/rngcryptoserviceprovider-generate-random-numbers-in-the-range-0-randommax
        /*public static BigInteger GetNextBigInt (this RNGCryptoServiceProvider rng, BigInteger maxval)
        {
            if (maxval < 1)
            {
                throw new ArgumentOutOfRangeException("maxval", maxval, "Must be positive value.");
            }

            var maxvalbytes = maxval.ToByteArray();
            var buffer = new byte[maxvalbytes.Length];
            BigInteger bits, val;

            // Is maxvalue an exact power of 2.
            if ((maxval & -maxval) == maxval)
            {
                rng.GetBytes(buffer);
                bits = new BigInteger(buffer);
                return bits & (maxval - 1);
            }

            // The Int32 implementation uses 0x7FFFFFFF
            byte[] maskBytes = new byte[maxvalbytes.Length];
            for (int i = 0; i < maxvalbytes.Length; ++i)
            {
                maskBytes[i] = 255;
            }
            // Flip last bit to take into account signage.
            maskBytes[maxvalbytes.Length - 1] = 0;
            BigInteger mask = new BigInteger(maskBytes);

            do
            {
                rng.GetBytes(buffer);
                bits = new BigInteger(buffer) & mask;
                val = bits % maxval;
            } while (bits - val + (maxval - 1) < 0);

            return val;
        }*/
    }
}
