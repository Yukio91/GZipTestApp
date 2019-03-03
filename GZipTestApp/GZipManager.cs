using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using GZipTestApp.Log;

namespace GZipTestApp
{
    public class GZipManager
    {
        private const int BufferSize = 1024 * 1024 * 10; //10MB

        private long _fileSize;

        //private int _readCount;
        private int _writeCount;
        private int _blockCount;

        private ConcurrentQueue<ByteBlock> _readerQueue;
        private ConcurrentQueue<ByteBlock> _writerQueue;

        private readonly Thread[] _workers;
        private readonly int _workersCount;

        //protected readonly object ReaderLockObject = new object();
        protected readonly object WriterLockObject = new object();

        private CompressionMode _compressionMode;
        private bool _completed;
        private bool _canceled;
        private int _progress;

        private readonly ILogger _logger;

        public int Progress => _progress;
        public bool IsCanceled => _canceled;
        public bool IsCompleted => _completed;

        public GZipManager(int threadsCount, ILogger logger = null)
        {
            if (threadsCount <= 0)
                throw new Exception(nameof(threadsCount));

            _workersCount = threadsCount;
            _workers = new Thread[threadsCount];

            _logger = logger;
        }

        public void Close()
        {
            if ((_workers?.Length ?? 0) == 0)
                return;

            for (int i = 0; i < _workers.Length; i++)
            {
                try
                {
                    if (_workers[i]?.ThreadState == ThreadState.Running)
                        _workers[i]?.Join();

                    _workers[i] = null;
                    //_logger?.Write($"Close thread {i}");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Close thread {i} failed", ex);
                }
            }
        }

        public bool Start(string sourceFile, string targetFile, CompressionMode mode)
        {
            _compressionMode = mode;

            try
            {
                sourceFile = sourceFile.Trim();
                if (String.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
                    throw new Exception(nameof(sourceFile));

                targetFile = targetFile.Trim();
                if (String.IsNullOrEmpty(targetFile))
                    throw new ArgumentNullException(nameof(targetFile));

                _readerQueue = new ConcurrentQueue<ByteBlock>(_workersCount);
                _writerQueue = new ConcurrentQueue<ByteBlock>(_workersCount);

                _logger?.Write($"Start {(mode == CompressionMode.Compress ? "compress" : "decompress")} file");
                _logger?.Write($"Buffer size: {BufferSize}");

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
                _logger?.Error("Start failed", ex);

                return false;
            }
        }

        private void Read(object sourceFile)
        {
            var path = sourceFile.ToString().Trim();
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                using (FileStream sourceStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read))
                {
                    _fileSize = sourceStream.Length;
                    int readCount = 0;
                    _progress = 0;

                    int read;
                    do
                    {
                        read = 1;
                        if (_readerQueue.Count >= _workersCount)
                            continue;

                        byte[] buffer;
                        var offset = 0;

                        long leftSize = _fileSize - sourceStream.Position;
                        if (leftSize <= 0)
                            return;

                        int bufferSize = leftSize <= BufferSize ? (int) leftSize : BufferSize;
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
                        _progress = (int) ((double) sourceStream.Position / _fileSize * 90);

                        _readerQueue.Enqueue(new ByteBlock(readCount, buffer));
                        readCount++;

                    } while (read > 0);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("Read File failed", ex);

                _canceled = true;
            }
            finally
            {
                _readerQueue.Close();
            }
        }

        private void Write(object targetFile)
        {
            var path = targetFile.ToString().Trim();
            if (String.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                using (var targetStream = new FileStream(path, FileMode.Append, FileAccess.Write))
                {
                    using (var bs = new BufferedStream(targetStream))
                    {
                        _writeCount = 0;
                        var list = new List<ByteBlock>();
                        while (!_canceled)
                        {
                            ByteBlock block;
                            if (list.Count > 0 && (block = list.OrderBy(b => b.Id).FirstOrDefault())?.Id ==
                                _writeCount)
                            {
                                list.Remove(block);
                            }
                            else if (!_writerQueue.TryDequeue(out block))
                                break;

                            if (block?.Id != _writeCount)
                            {
                                list.Add(block);
                                continue;
                            }

                            //_logger?.Write($"Write block {block.Id}, queue: {list.Count}");

                            bs.Write(block.Buffer, 0, block.Buffer.Length);
                            bs.Flush();
                            
                            lock (WriterLockObject)
                                _writeCount++;
                        }
                    }
                }

                _progress = 100;
            }
            catch (Exception ex)
            {
                _logger?.Error("Write file failed", ex);

                _canceled = true;
            }
            finally
            {
                _completed = true;
            }
        }

        private bool _closeWorkers;
        private void Process(object number)
        {
            if (_fileSize <= 0)
                return;

            try
            {
                int lastBlockId = -1;
                while (!_canceled && !_closeWorkers)
                {
                    lock (WriterLockObject)
                    {
                        if (lastBlockId > _writeCount)
                            continue;
                    }

                    ByteBlock block;
                    if (!_readerQueue.TryDequeue(out block))
                    {
                        _writerQueue.Close();
                        _closeWorkers = true;

                        break;
                    }

                    if (block == null)
                        continue;

                    var buffer = _compressionMode == CompressionMode.Compress
                        ? GZipHelper.CompressBlock(block.Buffer)
                        : GZipHelper.DecompressBlock(block.Buffer);

                    lastBlockId = block.Id;

                    //_logger?.Write($"{_compressionMode}, block {lastBlockId}, thread {number}");

                    _writerQueue.Enqueue(new ByteBlock(block.Id, buffer));
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error in thread {number}", ex);

                _canceled = true;
            }
            finally
            {
                _logger?.Write($"Close thread {number}");
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
