using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTestApp
{
    //
    public class GZipManager
    {
        private const int BufferSize = 1024 * 1024; //1MB

        private long _fileSize;
        private int _readCount;
        private int _writeCount;
        private int _processedCount;

        private readonly Queue<ByteBlock> _readerQueue = new Queue<ByteBlock>();
        private readonly Queue<ByteBlock> _writerQueue = new Queue<ByteBlock>();

        private readonly Thread[] _workers;
        private readonly int _workersCount;

        protected readonly object LockObject = new object();

        private CompressionMode _compressionMode;
        private bool _completed;
        private bool _canceled;

        //public long ReadProgress
        //{
        //    get
        //    {
        //        if (_readCount == 0)
        //            return 0;

        //        return _readCount / _fileSize * 100;
        //    }
        //}

        //public long ProcessedProgress
        //{
        //    get
        //    {
        //        if (_processedCount == 0)
        //            return 0;

        //        return _processedCount / _fileSize * 100;
        //    }
        //}

        //public long WriteProgress
        //{
        //    get
        //    {
        //        if (_writeCount == 0)
        //            return 0;

        //        return _writeCount / _fileSize * 100;
        //    }
        //}

        public GZipManager(int threadsCount)
        {
            if (threadsCount <= 0)
                throw new Exception(nameof(threadsCount));

            _workersCount = threadsCount;
            _workers = new Thread[threadsCount];
        }

        public bool Stop()
        {
            if ((_workers?.Length ?? 0) == 0)
                return false;

            for (int i = 0; i < _workers.Length; i++)
            {
                try
                {
                    if (_workers[i]?.ThreadState == ThreadState.Running)
                        _workers[i]?.Join();

                    _workers[i] = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return true;
        }

        public bool Start(string sourceFile, string targetFile, CompressionMode mode)
        {
            _compressionMode = mode;

            try
            {
                _readerQueue.Clear();
                _writerQueue.Clear();

                Console.WriteLine($"Start {(mode == CompressionMode.Compress ? "compress" : "decompress")} file");

                var readFileThread = new Thread(Read);
                readFileThread.Start(sourceFile);

                for (int i = 0; i < _workers.Length; i++)
                {
                    _workers[i] = new Thread(Process);
                    _workers[i].Start(i);
                }

                var writeFileThread = new Thread(Write);
                writeFileThread.Start(targetFile);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start failed. Error: {ex.Message}");

                return false;
            }
        }

        private void Read(object sourceFile)
        {
            var path = sourceFile.ToString().Trim();
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            //byte[] buffer = new byte[BufferSize];
            int read = -1;
            // поток для чтения исходного файла
            using (FileStream sourceStream = new FileStream(path, FileMode.OpenOrCreate))
            {
                _fileSize = sourceStream.Length;
                _readCount = 0;

                do
                {
                    lock (LockObject)
                    {
                        if (_readerQueue.Count > _workersCount - 1)
                            continue;

                        byte[] buffer;
                        var bufferSize = BufferSize;
                        var offset = 0;
                        if (_compressionMode == CompressionMode.Decompress)
                        {
                            byte[] lengthBuffer = new byte[8];
                            sourceStream.Read(lengthBuffer, 0, lengthBuffer.Length);
                            bufferSize = BitConverter.ToInt32(lengthBuffer, 4);
                            if (bufferSize <= 0)
                                break;

                            Array.Clear(lengthBuffer, 4, 4);

                            buffer = new byte[bufferSize];
                            lengthBuffer.CopyTo(buffer, 0);

                            bufferSize -= 8;
                            offset = 8;
                        }
                        else buffer = new byte[bufferSize];

                        read = sourceStream.Read(buffer, offset, bufferSize);

                        _readerQueue.Enqueue(new ByteBlock(_readCount, buffer));
                        _readCount++;
                    }
                } while (read > 0);

                _completed = true;
            }
        }

        private void Write(object targetFile)
        {
            var path = targetFile.ToString().Trim();
            if (String.IsNullOrEmpty(path))
                return;

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (FileStream targetStream = new FileStream(path, FileMode.OpenOrCreate))
            {
                _writeCount = 0;
                var list = new List<ByteBlock>();

                while (true)
                {
                    lock (LockObject)
                    {
                        if (_writerQueue.Count == 0)
                        {
                            if (_completed)
                                break;

                            continue;
                        }

                        var block = _writerQueue.Dequeue();

                        if (block.Id > _writeCount && list.Count > 0)
                        {
                            var sorted = list.OrderBy(b => b.Id).ToList();
                            for (int i = 0; i < sorted.Count; i++)
                            {
                                if (block.Id == _writeCount)
                                    break;

                                targetStream.Write(sorted[i].Buffer, 0, sorted[i].Buffer.Length);
                                targetStream.Flush();

                                _writeCount++;

                                list.Remove(sorted[i]);
                            }
                        }

                        if (block.Id == _writeCount)
                        {
                            targetStream.Write(block.Buffer, 0, block.Buffer.Length);
                            targetStream.Flush();

                            _writeCount++;
                        }
                        else list.Add(block);
                    }
                }
            }
        }

        private void Process(object number)
        {
            if (_fileSize <= 0)
                return;

            try
            {
                while (true)
                {

                    lock (LockObject)
                    {
                        if (_readerQueue.Count == 0)
                        {
                            if (_completed)
                                break;

                            continue;
                        }

                        var block = _readerQueue.Dequeue();
                        if (block == null)
                            return;

                        byte[] buffer = new byte[BufferSize];
                        if (_compressionMode == CompressionMode.Compress)
                        {
                            buffer = CompressBlock(block.Buffer);
                        }
                        else
                        {
                            DecompressBlock(block.Buffer, buffer);
                        }

                        _writerQueue.Enqueue(new ByteBlock(block.Id, buffer));
                        _processedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in thread {number}. Message: {ex.Message}");

                _canceled = true;
            }
        }

        private byte[] CompressBlock(byte[] inputBlock)
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

        private void DecompressBlock(byte[] inputBlock, byte[] decompressed)
        {
            using (MemoryStream ms = new MemoryStream(inputBlock))
            {
                using (GZipStream gzipStream = new GZipStream(ms, CompressionMode.Decompress))
                {
                    try
                    {
                        gzipStream.Read(decompressed, 0, decompressed.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }
    }

    class ByteBlock
    {
        private readonly byte[] _buffer;
        private readonly int _id;

        public byte[] Buffer => _buffer;
        public int Id => _id;

        public ByteBlock(int id, byte[] buffer)
        {
            _id = id;
            _buffer = buffer;
        }
    }
}
