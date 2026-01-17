using System;
using System.IO;
using System.Threading.Tasks;

namespace Shared
{
    public static class FileTransferHelper
    {
        /// <summary>
        /// Chia file thành các chunks để gửi
        /// </summary>
        public static async IAsyncEnumerable<ChatPacket> CreateFileChunksAsync(
            string filePath, 
            string sender, 
            string target)
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > FileTransferConstants.MaxFileSize)
                throw new InvalidOperationException($"File vượt quá giới hạn {FileTransferConstants.MaxFileSize / (1024 * 1024 * 1024)}GB");

            string fileId = Guid.NewGuid().ToString();
            int totalChunks = (int)Math.Ceiling((double)fileInfo.Length / FileTransferConstants.ChunkSize);

            // Gửi packet bắt đầu
            yield return new ChatPacket
            {
                Type = PacketType.FileStart,
                Sender = sender,
                Target = target,
                FileName = fileInfo.Name,
                FileId = fileId,
                TotalChunks = totalChunks,
                TotalFileSize = fileInfo.Length,
                Time = DateTime.Now
            };

            // Gửi từng chunk
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
                bufferSize: FileTransferConstants.ChunkSize, useAsync: true);
            
            byte[] buffer = new byte[FileTransferConstants.ChunkSize];
            int chunkIndex = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                byte[] chunkData = bytesRead == buffer.Length 
                    ? buffer 
                    : buffer[..bytesRead];

                yield return new ChatPacket
                {
                    Type = PacketType.FileChunk,
                    Sender = sender,
                    Target = target,
                    FileId = fileId,
                    ChunkIndex = chunkIndex++,
                    TotalChunks = totalChunks,
                    FileData = chunkData,
                    Time = DateTime.Now
                };
            }

            // Gửi packet kết thúc
            yield return new ChatPacket
            {
                Type = PacketType.FileEnd,
                Sender = sender,
                Target = target,
                FileId = fileId,
                FileName = fileInfo.Name,
                TotalFileSize = fileInfo.Length,
                Time = DateTime.Now
            };
        }
    }

    /// <summary>
    /// Class để nhận và ghép các chunks thành file hoàn chỉnh
    /// </summary>
    public class FileReceiver : IDisposable
    {
        private readonly string _tempPath;
        private FileStream? _stream;
        private int _receivedChunks;

        public string FileId { get; }
        public string FileName { get; private set; } = string.Empty;
        public int TotalChunks { get; private set; }
        public long TotalFileSize { get; private set; }
        public double Progress => TotalChunks > 0 ? (double)_receivedChunks / TotalChunks * 100 : 0;

        public FileReceiver(string fileId, string tempDirectory)
        {
            FileId = fileId;
            _tempPath = Path.Combine(tempDirectory, $"{fileId}.tmp");
            Directory.CreateDirectory(tempDirectory);
        }

        public void Initialize(ChatPacket startPacket)
        {
            FileName = startPacket.FileName;
            TotalChunks = startPacket.TotalChunks;
            TotalFileSize = startPacket.TotalFileSize;
            _stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: FileTransferConstants.ChunkSize, useAsync: true);
        }

        public async Task WriteChunkAsync(ChatPacket chunkPacket)
        {
            if (_stream == null)
                throw new InvalidOperationException("FileReceiver chưa được khởi tạo");

            if (chunkPacket.FileData != null)
            {
                await _stream.WriteAsync(chunkPacket.FileData);
                _receivedChunks++;
            }
        }

        public async Task<string> FinalizeAsync(string outputDirectory)
        {
            if (_stream != null)
            {
                await _stream.FlushAsync();
                await _stream.DisposeAsync();
                _stream = null;
            }

            string outputPath = Path.Combine(outputDirectory, FileName);
            
            // Đổi tên file unique nếu đã tồn tại
            int counter = 1;
            string baseName = Path.GetFileNameWithoutExtension(FileName);
            string extension = Path.GetExtension(FileName);
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(outputDirectory, $"{baseName}_{counter++}{extension}");
            }

            File.Move(_tempPath, outputPath);
            return outputPath;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }
    }

    internal static class FileTransferConstants
    {
        public const long MaxFileSize = 2L * 1024 * 1024 * 1024; // 2GB, adjust as needed
        public const int ChunkSize = 1024 * 1024; // 1MB, adjust as needed
    }
}