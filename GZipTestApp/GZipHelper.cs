using System;
using System.IO;
using System.IO.Compression;

namespace GZipTestApp
{
    public static class GZipHelper
    {
        public static byte[] CompressBlock(byte[] inputBlock)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gzipStream = new GZipStream(ms, CompressionMode.Compress))
                {
                    gzipStream.Write(inputBlock, 0, inputBlock.Length);
                    gzipStream.Flush();
                }

                byte[] output = ms.ToArray();
                BitConverter.GetBytes(output.Length).CopyTo(output, 4);

                return output;
            }
        }

        public static byte[] DecompressBlock(byte[] inputBlock)
        {
            using (MemoryStream ms = new MemoryStream(inputBlock))
            {
                using (GZipStream gzipStream = new GZipStream(ms, CompressionMode.Decompress))
                {
                    using (var resultStream = new MemoryStream())
                    {
                        gzipStream.CopyTo(resultStream);
                        return resultStream.ToArray();
                    }
                }
            }
        }
    }
}
