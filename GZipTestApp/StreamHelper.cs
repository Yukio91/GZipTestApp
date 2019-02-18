using System;
using System.IO;

namespace GZipTestApp
{
    public static class StreamHelper
    {
        public static byte[] CopyTo(this Stream src, int bufferSize)
        {
            int size = (src.CanSeek) ? Math.Min((int)(src.Length - src.Position), bufferSize) : bufferSize;
            byte[] buffer = new byte[bufferSize];
            
           src.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public static void CopyTo(this Stream src, Stream dest)
        {
            int size = (src.CanSeek) ? Math.Min((int)(src.Length - src.Position), 0x2000) : 0x2000;
            byte[] buffer = new byte[size];
            int n;
            do
            {
                n = src.Read(buffer, 0, buffer.Length);
                dest.Write(buffer, 0, n);
            } while (n != 0);
        }

        public static void CopyTo(this MemoryStream src, Stream dest)
        {
            dest.Write(src.GetBuffer(), (int)src.Position, (int)(src.Length - src.Position));
        }

        public static void CopyTo(this Stream src, MemoryStream dest)
        {
            if (src.CanSeek)
            {
                int pos = (int)dest.Position;
                int length = (int)(src.Length - src.Position) + pos;
                dest.SetLength(length);

                while (pos < length)
                    pos += src.Read(dest.GetBuffer(), pos, length - pos);
            }
            else
                src.CopyTo((Stream)dest);
        }
    }
}
