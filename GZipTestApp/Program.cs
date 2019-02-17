using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace GZipTestApp
{
    class Program
    {
        private const int BufferSize = 1024*1024;
        static void Main(string[] args)
        {
            CompressionMode compressionMode = CompressionMode.Compress;
            string sourceFile = String.Empty, targetFile = String.Empty;
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Select gzip compression mode. 1 - compress, 2 - decompress:");
                var selectedMode = Console.ReadLine();
                compressionMode = selectedMode == "1" ? CompressionMode.Compress : CompressionMode.Decompress;

                Console.WriteLine($"Input {(compressionMode == CompressionMode.Compress ? "source" : "compressed")} file path:");
                sourceFile = Console.ReadLine();

                Console.WriteLine("Input target file path:");
                targetFile = Console.ReadLine();
            }
            else
            {
                if (args.Length != 3)
                    return;

                string mode = args[0].Trim().ToLower();
                switch (mode)
                {
                    case "compress":
                        compressionMode = CompressionMode.Compress;
                        break;
                    case "decompress":
                        compressionMode = CompressionMode.Decompress;
                        break;
                    default:
                        return;
                }

                sourceFile = args[1];
                targetFile = args[2];
            }

            var manager = new GZipManager(Environment.ProcessorCount);
            manager.Start(sourceFile, targetFile, compressionMode);

            //Compress(sourceFile, targetFile);
            //Decompress(sourceFile, targetFile);
        }

        public static void Compress(string sourceFile, string compressedFile)
        {
            byte[] buffer = new byte[BufferSize];
            // поток для чтения исходного файла
            using (FileStream sourceStream = new FileStream(sourceFile, FileMode.OpenOrCreate))
            {
                if (SourceQueue.Count <= 1024)
                {
                    sourceStream.Read(buffer, 0, buffer.Length);
                    SourceQueue.Enqueue(buffer);
                }
            }
            // поток для записи сжатого файла
            using (FileStream targetStream = File.Create(compressedFile))
            {
                // поток архивации
                using (GZipStream compressionStream = new GZipStream(targetStream, CompressionMode.Compress))
                {
                    //sourceStream.CopyTo(compressionStream); // копируем байты из одного потока в другой
                    //Console.WriteLine("Сжатие файла {0} завершено. Исходный размер: {1}  сжатый размер: {2}.",
                    //    sourceFile, sourceStream.Length.ToString(), targetStream.Length.ToString());

                }
            }
        }

        public static void Decompress(string compressedFile, string targetFile)
        {
            // поток для чтения из сжатого файла
            using (FileStream sourceStream = new FileStream(compressedFile, FileMode.OpenOrCreate))
            {
                // поток для записи восстановленного файла
                using (FileStream targetStream = File.Create(targetFile))
                {
                    // поток разархивации
                    using (GZipStream decompressionStream = new GZipStream(sourceStream, CompressionMode.Decompress))
                    {
                        while (sourceStream.Length > 0)
                        {
                            byte[] buffer = new byte[BufferSize];

                            decompressionStream.Read(buffer, 0, buffer.Length);

                            //decompressionStream.CopyTo(targetStream);
                            Console.WriteLine("Восстановлен файл: {0}", targetFile);
                        }
                    }
                }
            }
        }

        private static Queue<byte[]> SourceQueue = new Queue<byte[]>();
    }
}
