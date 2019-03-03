using System;
using System.IO.Compression;
using GZipTestApp.Log;

namespace GZipTestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            CompressionMode compressionMode = CompressionMode.Compress;
            string sourceFile, targetFile, mode;
            if (args == null || args.Length != 3)
            {
                Console.WriteLine("Input gzip compression mode (compress or decompress):");
                mode = Console.ReadLine();

                Console.WriteLine($"Input {(compressionMode == CompressionMode.Compress ? "source" : "compressed")} " +
                                  "file path:");
                sourceFile = Console.ReadLine();

                Console.WriteLine("Input target file path:");
                targetFile = Console.ReadLine();
            }
            else
            {
                mode = args[0];

                sourceFile = args[1];
                targetFile = args[2];
            }

            switch (mode?.Trim().ToLower())
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

            var logger = new ConsoleLogger();
            var manager = new GZipManager(Environment.ProcessorCount, logger);
            if (manager.Start(sourceFile, targetFile, compressionMode))
            {
                int progress = -1;
                while (!manager.IsCanceled && !manager.IsCompleted)
                {
                    if (manager.Progress > progress)
                    {
                        progress = manager.Progress;
                        logger.Write($"Progress {progress}%");
                    }
                }

                //manager.Close();

                GC.Collect();
                logger.Write($"File process " +
                                  $"{(manager.IsCanceled ? "canceled" : manager.IsCompleted ? "completed" : "")}");
            }

            while (true)
            {
                Console.WriteLine("Press Ctrl+C to exit");
                var keyInfo = Console.ReadKey();
                if (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers == ConsoleModifiers.Control)
                    break;
            }
        }
    }
}
