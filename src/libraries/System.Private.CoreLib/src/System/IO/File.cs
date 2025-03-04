// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace System.IO
{
    // Class for creating FileStream objects, and some basic file management
    // routines such as Delete, etc.
    public static partial class File
    {
        // Don't use Array.MaxLength. MS.IO.Redist targets .NET Framework.
        private const int MaxByteArrayLength = 0x7FFFFFC7;
        private static Encoding? s_UTF8NoBOM;

        // UTF-8 without BOM and with error detection. Same as the default encoding for StreamWriter.
        private static Encoding UTF8NoBOM => s_UTF8NoBOM ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        internal const int DefaultBufferSize = 4096;

        public static StreamReader OpenText(string path)
            => new StreamReader(path ?? throw new ArgumentNullException(nameof(path)));

        public static StreamWriter CreateText(string path)
            => new StreamWriter(path ?? throw new ArgumentNullException(nameof(path)), append: false);

        public static StreamWriter AppendText(string path)
            => new StreamWriter(path ?? throw new ArgumentNullException(nameof(path)), append: true);

        /// <summary>
        /// Copies an existing file to a new file.
        /// An exception is raised if the destination file already exists.
        /// </summary>
        public static void Copy(string sourceFileName, string destFileName)
            => Copy(sourceFileName, destFileName, overwrite: false);

        /// <summary>
        /// Copies an existing file to a new file.
        /// If <paramref name="overwrite"/> is false, an exception will be
        /// raised if the destination exists. Otherwise it will be overwritten.
        /// </summary>
        public static void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName), SR.ArgumentNull_FileName);
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName), SR.ArgumentNull_FileName);
            if (sourceFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(sourceFileName));
            if (destFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(destFileName));

            FileSystem.CopyFile(Path.GetFullPath(sourceFileName), Path.GetFullPath(destFileName), overwrite);
        }

        // Creates a file in a particular path.  If the file exists, it is replaced.
        // The file is opened with ReadWrite access and cannot be opened by another
        // application until it has been closed.  An IOException is thrown if the
        // directory specified doesn't exist.
        public static FileStream Create(string path)
            => Create(path, DefaultBufferSize);

        // Creates a file in a particular path.  If the file exists, it is replaced.
        // The file is opened with ReadWrite access and cannot be opened by another
        // application until it has been closed.  An IOException is thrown if the
        // directory specified doesn't exist.
        public static FileStream Create(string path, int bufferSize)
            => new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize);

        public static FileStream Create(string path, int bufferSize, FileOptions options)
            => new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize, options);

        // Deletes a file. The file specified by the designated path is deleted.
        // If the file does not exist, Delete succeeds without throwing
        // an exception.
        //
        // On Windows, Delete will fail for a file that is open for normal I/O
        // or a file that is memory mapped.
        public static void Delete(string path)
            => FileSystem.DeleteFile(Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path))));

        // Tests whether a file exists. The result is true if the file
        // given by the specified path exists; otherwise, the result is
        // false.  Note that if path describes a directory,
        // Exists will return true.
        public static bool Exists([NotNullWhen(true)] string? path)
        {
            try
            {
                if (path == null)
                    return false;
                if (path.Length == 0)
                    return false;

                path = Path.GetFullPath(path);

                // After normalizing, check whether path ends in directory separator.
                // Otherwise, FillAttributeInfo removes it and we may return a false positive.
                // GetFullPath should never return null
                Debug.Assert(path != null, "File.Exists: GetFullPath returned null");
                if (path.Length > 0 && PathInternal.IsDirectorySeparator(path[path.Length - 1]))
                {
                    return false;
                }

                return FileSystem.FileExists(path);
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return false;
        }

        public static FileStream Open(string path, FileMode mode)
            => Open(path, mode, (mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite), FileShare.None);

        public static FileStream Open(string path, FileMode mode, FileAccess access)
            => Open(path, mode, access, FileShare.None);

        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share)
            => new FileStream(path, mode, access, share);

        // File and Directory UTC APIs treat a DateTimeKind.Unspecified as UTC whereas
        // ToUniversalTime treats this as local.
        internal static DateTimeOffset GetUtcDateTimeOffset(DateTime dateTime)
            => dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime.ToUniversalTime();

        public static void SetCreationTime(string path, DateTime creationTime)
            => FileSystem.SetCreationTime(Path.GetFullPath(path), creationTime, asDirectory: false);

        public static void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
            => FileSystem.SetCreationTime(Path.GetFullPath(path), GetUtcDateTimeOffset(creationTimeUtc), asDirectory: false);

        public static DateTime GetCreationTime(string path)
            => FileSystem.GetCreationTime(Path.GetFullPath(path)).LocalDateTime;

        public static DateTime GetCreationTimeUtc(string path)
            => FileSystem.GetCreationTime(Path.GetFullPath(path)).UtcDateTime;

        public static void SetLastAccessTime(string path, DateTime lastAccessTime)
            => FileSystem.SetLastAccessTime(Path.GetFullPath(path), lastAccessTime, asDirectory: false);

        public static void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
            => FileSystem.SetLastAccessTime(Path.GetFullPath(path), GetUtcDateTimeOffset(lastAccessTimeUtc), asDirectory: false);

        public static DateTime GetLastAccessTime(string path)
            => FileSystem.GetLastAccessTime(Path.GetFullPath(path)).LocalDateTime;

        public static DateTime GetLastAccessTimeUtc(string path)
            => FileSystem.GetLastAccessTime(Path.GetFullPath(path)).UtcDateTime;

        public static void SetLastWriteTime(string path, DateTime lastWriteTime)
            => FileSystem.SetLastWriteTime(Path.GetFullPath(path), lastWriteTime, asDirectory: false);

        public static void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
            => FileSystem.SetLastWriteTime(Path.GetFullPath(path), GetUtcDateTimeOffset(lastWriteTimeUtc), asDirectory: false);

        public static DateTime GetLastWriteTime(string path)
            => FileSystem.GetLastWriteTime(Path.GetFullPath(path)).LocalDateTime;

        public static DateTime GetLastWriteTimeUtc(string path)
            => FileSystem.GetLastWriteTime(Path.GetFullPath(path)).UtcDateTime;

        public static FileAttributes GetAttributes(string path)
            => FileSystem.GetAttributes(Path.GetFullPath(path));

        public static void SetAttributes(string path, FileAttributes fileAttributes)
            => FileSystem.SetAttributes(Path.GetFullPath(path), fileAttributes);

        public static FileStream OpenRead(string path)
            => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        public static FileStream OpenWrite(string path)
            => new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

        public static string ReadAllText(string path)
            => ReadAllText(path, Encoding.UTF8);

        public static string ReadAllText(string path, Encoding encoding)
        {
            Validate(path, encoding);

            using StreamReader sr = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }

        public static void WriteAllText(string path, string? contents)
            => WriteAllText(path, contents, UTF8NoBOM);

        public static void WriteAllText(string path, string? contents, Encoding encoding)
        {
            Validate(path, encoding);

            WriteToFile(path, FileMode.Create, contents, encoding);
        }

        public static byte[] ReadAllBytes(string path)
        {
            // bufferSize == 1 used to avoid unnecessary buffer in FileStream
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan))
            {
                long fileLength = 0;
                if (fs.CanSeek && (fileLength = fs.Length) > int.MaxValue)
                {
                    throw new IOException(SR.IO_FileTooLong2GB);
                }
                if (fileLength == 0)
                {
                    // Some file systems (e.g. procfs on Linux) return 0 for length even when there's content; also there is non-seekable file stream.
                    // Thus we need to assume 0 doesn't mean empty.
                    return ReadAllBytesUnknownLength(fs);
                }

                int index = 0;
                int count = (int)fileLength;
                byte[] bytes = new byte[count];
                while (count > 0)
                {
                    int n = fs.Read(bytes, index, count);
                    if (n == 0)
                    {
                        ThrowHelper.ThrowEndOfFileException();
                    }

                    index += n;
                    count -= n;
                }
                return bytes;
            }
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path), SR.ArgumentNull_Path);
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            using SafeFileHandle sfh = OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            RandomAccess.WriteAtOffset(sfh, bytes, 0);
        }

        public static string[] ReadAllLines(string path)
            => ReadAllLines(path, Encoding.UTF8);

        public static string[] ReadAllLines(string path, Encoding encoding)
        {
            Validate(path, encoding);

            string? line;
            List<string> lines = new List<string>();

            using StreamReader sr = new StreamReader(path, encoding);
            while ((line = sr.ReadLine()) != null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        public static IEnumerable<string> ReadLines(string path)
            => ReadLines(path, Encoding.UTF8);

        public static IEnumerable<string> ReadLines(string path, Encoding encoding)
        {
            Validate(path, encoding);

            return ReadLinesIterator.CreateIterator(path, encoding);
        }

        public static void WriteAllLines(string path, string[] contents)
            => WriteAllLines(path, (IEnumerable<string>)contents);

        public static void WriteAllLines(string path, IEnumerable<string> contents)
            => WriteAllLines(path, contents, UTF8NoBOM);

        public static void WriteAllLines(string path, string[] contents, Encoding encoding)
            => WriteAllLines(path, (IEnumerable<string>)contents, encoding);

        public static void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            Validate(path, encoding);

            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            InternalWriteAllLines(new StreamWriter(path, false, encoding), contents);
        }

        private static void InternalWriteAllLines(TextWriter writer, IEnumerable<string> contents)
        {
            Debug.Assert(writer != null);
            Debug.Assert(contents != null);

            using (writer)
            {
                foreach (string line in contents)
                {
                    writer.WriteLine(line);
                }
            }
        }

        public static void AppendAllText(string path, string? contents)
            => AppendAllText(path, contents, UTF8NoBOM);

        public static void AppendAllText(string path, string? contents, Encoding encoding)
        {
            Validate(path, encoding);

            WriteToFile(path, FileMode.Append, contents, encoding);
        }

        public static void AppendAllLines(string path, IEnumerable<string> contents)
            => AppendAllLines(path, contents, UTF8NoBOM);

        public static void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            Validate(path, encoding);

            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            InternalWriteAllLines(new StreamWriter(path, true, encoding), contents);
        }

        public static void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
            => Replace(sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors: false);

        public static void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName, bool ignoreMetadataErrors)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName));
            if (destinationFileName == null)
                throw new ArgumentNullException(nameof(destinationFileName));

            FileSystem.ReplaceFile(
                Path.GetFullPath(sourceFileName),
                Path.GetFullPath(destinationFileName),
                destinationBackupFileName != null ? Path.GetFullPath(destinationBackupFileName) : null,
                ignoreMetadataErrors);
        }

        // Moves a specified file to a new location and potentially a new file name.
        // This method does work across volumes.
        //
        // The caller must have certain FileIOPermissions.  The caller must
        // have Read and Write permission to
        // sourceFileName and Write
        // permissions to destFileName.
        //
        public static void Move(string sourceFileName, string destFileName)
            => Move(sourceFileName, destFileName, false);

        public static void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (sourceFileName == null)
                throw new ArgumentNullException(nameof(sourceFileName), SR.ArgumentNull_FileName);
            if (destFileName == null)
                throw new ArgumentNullException(nameof(destFileName), SR.ArgumentNull_FileName);
            if (sourceFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(sourceFileName));
            if (destFileName.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyFileName, nameof(destFileName));

            string fullSourceFileName = Path.GetFullPath(sourceFileName);
            string fullDestFileName = Path.GetFullPath(destFileName);

            if (!FileSystem.FileExists(fullSourceFileName))
            {
                throw new FileNotFoundException(SR.Format(SR.IO_FileNotFound_FileName, fullSourceFileName), fullSourceFileName);
            }

            FileSystem.MoveFile(fullSourceFileName, fullDestFileName, overwrite);
        }

        [SupportedOSPlatform("windows")]
        public static void Encrypt(string path)
            => FileSystem.Encrypt(path ?? throw new ArgumentNullException(nameof(path)));

        [SupportedOSPlatform("windows")]
        public static void Decrypt(string path)
            => FileSystem.Decrypt(path ?? throw new ArgumentNullException(nameof(path)));

        // If we use the path-taking constructors we will not have FileOptions.Asynchronous set and
        // we will have asynchronous file access faked by the thread pool. We want the real thing.
        private static StreamReader AsyncStreamReader(string path, Encoding encoding)
            => new StreamReader(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan),
                encoding, detectEncodingFromByteOrderMarks: true);

        private static StreamWriter AsyncStreamWriter(string path, Encoding encoding, bool append)
            => new StreamWriter(
                new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, DefaultBufferSize, FileOptions.Asynchronous),
                encoding);

        public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
            => ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

        public static Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<string>(cancellationToken)
                : InternalReadAllTextAsync(path, encoding, cancellationToken);
        }

        private static async Task<string> InternalReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrEmpty(path));
            Debug.Assert(encoding != null);

            char[]? buffer = null;
            StreamReader sr = AsyncStreamReader(path, encoding);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer = ArrayPool<char>.Shared.Rent(sr.CurrentEncoding.GetMaxCharCount(DefaultBufferSize));
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    int read = await sr.ReadAsync(new Memory<char>(buffer), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return sb.ToString();
                    }

                    sb.Append(buffer, 0, read);
                }
            }
            finally
            {
                sr.Dispose();
                if (buffer != null)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }
            }
        }

        public static Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default(CancellationToken))
            => WriteAllTextAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task WriteAllTextAsync(string path, string? contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return WriteToFileAsync(path, FileMode.Create, contents, encoding, cancellationToken);
        }

        public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<byte[]>(cancellationToken);
            }

            var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, // bufferSize == 1 used to avoid unnecessary buffer in FileStream
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            bool returningInternalTask = false;
            try
            {
                long fileLength = 0L;
                if (fs.CanSeek && (fileLength = fs.Length) > int.MaxValue)
                {
                    var e = new IOException(SR.IO_FileTooLong2GB);
                    ExceptionDispatchInfo.SetCurrentStackTrace(e);
                    return Task.FromException<byte[]>(e);
                }

                returningInternalTask = true;
                return fileLength > 0 ?
                    InternalReadAllBytesAsync(fs, (int)fileLength, cancellationToken) :
                    InternalReadAllBytesUnknownLengthAsync(fs, cancellationToken);
            }
            finally
            {
                if (!returningInternalTask)
                {
                    fs.Dispose();
                }
            }
        }

        private static async Task<byte[]> InternalReadAllBytesAsync(FileStream fs, int count, CancellationToken cancellationToken)
        {
            using (fs)
            {
                int index = 0;
                byte[] bytes = new byte[count];
                do
                {
                    int n = await fs.ReadAsync(new Memory<byte>(bytes, index, count - index), cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                    {
                        ThrowHelper.ThrowEndOfFileException();
                    }

                    index += n;
                } while (index < count);

                return bytes;
            }
        }

        private static async Task<byte[]> InternalReadAllBytesUnknownLengthAsync(FileStream fs, CancellationToken cancellationToken)
        {
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(512);
            try
            {
                int bytesRead = 0;
                while (true)
                {
                    if (bytesRead == rentedArray.Length)
                    {
                        uint newLength = (uint)rentedArray.Length * 2;
                        if (newLength > MaxByteArrayLength)
                        {
                            newLength = (uint)Math.Max(MaxByteArrayLength, rentedArray.Length + 1);
                        }

                        byte[] tmp = ArrayPool<byte>.Shared.Rent((int)newLength);
                        Buffer.BlockCopy(rentedArray, 0, tmp, 0, bytesRead);

                        byte[] toReturn = rentedArray;
                        rentedArray = tmp;

                        ArrayPool<byte>.Shared.Return(toReturn);
                    }

                    Debug.Assert(bytesRead < rentedArray.Length);
                    int n = await fs.ReadAsync(rentedArray.AsMemory(bytesRead), cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                    {
                        return rentedArray.AsSpan(0, bytesRead).ToArray();
                    }
                    bytesRead += n;
                }
            }
            finally
            {
                fs.Dispose();
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }

        public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path), SR.ArgumentNull_Path);
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Core(path, bytes, cancellationToken);

            static async Task Core(string path, byte[] bytes, CancellationToken cancellationToken)
            {
                using SafeFileHandle sfh = OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.Read, FileOptions.Asynchronous);
                await RandomAccess.WriteAtOffsetAsync(sfh, bytes, 0, cancellationToken).ConfigureAwait(false);
            }
        }

        public static Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
            => ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);

        public static Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<string[]>(cancellationToken)
                : InternalReadAllLinesAsync(path, encoding, cancellationToken);
        }

        private static async Task<string[]> InternalReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrEmpty(path));
            Debug.Assert(encoding != null);

            using (StreamReader sr = AsyncStreamReader(path, encoding))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line;
                List<string> lines = new List<string>();
                while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lines.Add(line);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return lines.ToArray();
            }
        }

        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default(CancellationToken))
            => WriteAllLinesAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : InternalWriteAllLinesAsync(AsyncStreamWriter(path, encoding, append: false), contents, cancellationToken);
        }

        private static async Task InternalWriteAllLinesAsync(TextWriter writer, IEnumerable<string> contents, CancellationToken cancellationToken)
        {
            Debug.Assert(writer != null);
            Debug.Assert(contents != null);

            using (writer)
            {
                foreach (string line in contents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }

        public static Task AppendAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default(CancellationToken))
            => AppendAllTextAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task AppendAllTextAsync(string path, string? contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return WriteToFileAsync(path, FileMode.Append, contents, encoding, cancellationToken);
        }

        public static Task AppendAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default(CancellationToken))
            => AppendAllLinesAsync(path, contents, UTF8NoBOM, cancellationToken);

        public static Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(path, encoding);

            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : InternalWriteAllLinesAsync(AsyncStreamWriter(path, encoding, append: true), contents, cancellationToken);
        }

        /// <summary>
        /// Creates a file symbolic link identified by <paramref name="path"/> that points to <paramref name="pathToTarget"/>.
        /// </summary>
        /// <param name="path">The path where the symbolic link should be created.</param>
        /// <param name="pathToTarget">The path of the target to which the symbolic link points.</param>
        /// <returns>A <see cref="FileInfo"/> instance that wraps the newly created file symbolic link.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="pathToTarget"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="pathToTarget"/> is empty.
        /// -or-
        /// <paramref name="path"/> or <paramref name="pathToTarget"/> contains a null character.</exception>
        /// <exception cref="IOException">A file or directory already exists in the location of <paramref name="path"/>.
        /// -or-
        /// An I/O error occurred.</exception>
        public static FileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
        {
            string fullPath = Path.GetFullPath(path);
            FileSystem.VerifyValidPath(pathToTarget, nameof(pathToTarget));

            FileSystem.CreateSymbolicLink(path, pathToTarget, isDirectory: false);
            return new FileInfo(originalPath: path, fullPath: fullPath, isNormalized: true);
        }

        /// <summary>
        /// Gets the target of the specified file link.
        /// </summary>
        /// <param name="linkPath">The path of the file link.</param>
        /// <param name="returnFinalTarget"><see langword="true"/> to follow links to the final target; <see langword="false"/> to return the immediate next link.</param>
        /// <returns>A <see cref="FileInfo"/> instance if <paramref name="linkPath"/> exists, independently if the target exists or not. <see langword="null"/> if <paramref name="linkPath"/> is not a link.</returns>
        /// <exception cref="IOException">The file on <paramref name="linkPath"/> does not exist.
        /// -or-
        /// The link's file system entry type is inconsistent with that of its target.
        /// -or-
        /// Too many levels of symbolic links.</exception>
        /// <remarks>When <paramref name="returnFinalTarget"/> is <see langword="true"/>, the maximum number of symbolic links that are followed are 40 on Unix and 63 on Windows.</remarks>
        public static FileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
        {
            FileSystem.VerifyValidPath(linkPath, nameof(linkPath));
            return FileSystem.ResolveLinkTarget(linkPath, returnFinalTarget, isDirectory: false);
        }

        private static void Validate(string path, Encoding encoding)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException(SR.Argument_EmptyPath, nameof(path));
        }
    }
}
