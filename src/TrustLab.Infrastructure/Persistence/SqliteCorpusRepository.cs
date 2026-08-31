using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Infrastructure.Persistence;

public sealed class SqliteCorpusRepository : ICorpusRepository
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private bool _isInitialized;

    public SqliteCorpusRepository(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "data", "trustlab_corpus.db");
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var tableSql = @"
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                TotalPages INTEGER NOT NULL,
                TotalCharacters INTEGER NOT NULL,
                TotalChunks INTEGER NOT NULL,
                Content TEXT NOT NULL,
                UploadedAt TEXT NOT NULL,
                MetadataJson TEXT
            );

            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                DocumentId TEXT NOT NULL,
                ChunkIndex INTEGER NOT NULL,
                Content TEXT NOT NULL,
                StartOffset INTEGER NOT NULL,
                EndOffset INTEGER NOT NULL,
                MetadataJson TEXT,
                FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_docid ON Chunks(DocumentId);
        ";

        using var cmd = new SqliteCommand(tableSql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _isInitialized = true;
    }

    public async Task SaveDocumentWithChunksAsync(
        Document document,
        IReadOnlyList<Chunk> chunks,
        long fileSizeBytes,
        int totalPages,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        try
        {
            // 1. Delete existing if any (upsert semantics)
            using (var deleteCmd = new SqliteCommand("DELETE FROM Documents WHERE Id = @id", connection, tx))
            {
                deleteCmd.Parameters.AddWithValue("@id", document.Id);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2. Insert Document
            var metaJson = document.Metadata != null ? JsonSerializer.Serialize(document.Metadata) : null;
            var uploadedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            using (var insertDocCmd = new SqliteCommand(@"
                INSERT INTO Documents (Id, FileName, FileSizeBytes, TotalPages, TotalCharacters, TotalChunks, Content, UploadedAt, MetadataJson)
                VALUES (@id, @fileName, @fileSizeBytes, @totalPages, @totalCharacters, @totalChunks, @content, @uploadedAt, @metadataJson)",
                connection, tx))
            {
                insertDocCmd.Parameters.AddWithValue("@id", document.Id);
                insertDocCmd.Parameters.AddWithValue("@fileName", document.Id);
                insertDocCmd.Parameters.AddWithValue("@fileSizeBytes", fileSizeBytes);
                insertDocCmd.Parameters.AddWithValue("@totalPages", totalPages);
                insertDocCmd.Parameters.AddWithValue("@totalCharacters", document.Content.Length);
                insertDocCmd.Parameters.AddWithValue("@totalChunks", chunks.Count);
                insertDocCmd.Parameters.AddWithValue("@content", document.Content);
                insertDocCmd.Parameters.AddWithValue("@uploadedAt", uploadedAt);
                insertDocCmd.Parameters.AddWithValue("@metadataJson", (object?)metaJson ?? DBNull.Value);

                await insertDocCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 3. Batch Insert Chunks
            foreach (var chunk in chunks)
            {
                var chunkMetaJson = chunk.Metadata != null ? JsonSerializer.Serialize(chunk.Metadata) : null;
                using var insertChunkCmd = new SqliteCommand(@"
                    INSERT INTO Chunks (Id, DocumentId, ChunkIndex, Content, StartOffset, EndOffset, MetadataJson)
                    VALUES (@id, @docId, @chunkIndex, @content, @startOffset, @endOffset, @metadataJson)",
                    connection, tx);
                {
                    insertChunkCmd.Parameters.AddWithValue("@id", chunk.Id);
                    insertChunkCmd.Parameters.AddWithValue("@docId", document.Id);
                    insertChunkCmd.Parameters.AddWithValue("@chunkIndex", chunk.ChunkIndex);
                    insertChunkCmd.Parameters.AddWithValue("@content", chunk.Content);
                    insertChunkCmd.Parameters.AddWithValue("@startOffset", chunk.StartOffset);
                    insertChunkCmd.Parameters.AddWithValue("@endOffset", chunk.EndOffset);
                    insertChunkCmd.Parameters.AddWithValue("@metadataJson", (object?)chunkMetaJson ?? DBNull.Value);

                    await insertChunkCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SavedDocumentInfo>> GetAllDocumentSummariesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var list = new List<SavedDocumentInfo>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand(@"
            SELECT Id, FileName, FileSizeBytes, TotalPages, TotalCharacters, TotalChunks, UploadedAt, MetadataJson
            FROM Documents
            ORDER BY UploadedAt DESC", connection);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var fileName = reader.GetString(1);
            var size = reader.GetInt64(2);
            var pages = reader.GetInt32(3);
            var chars = reader.GetInt32(4);
            var chunksCount = reader.GetInt32(5);
            var uploadedAt = reader.GetString(6);
            var metaStr = reader.IsDBNull(7) ? null : reader.GetString(7);
            var metadata = metaStr != null ? JsonSerializer.Deserialize<Dictionary<string, string>>(metaStr) : null;

            list.Add(new SavedDocumentInfo(id, fileName, size, pages, chars, chunksCount, uploadedAt, metadata));
        }

        return list;
    }

    public async Task<IReadOnlyList<Document>> GetAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var list = new List<Document>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand("SELECT Id, Content, MetadataJson FROM Documents ORDER BY UploadedAt ASC", connection);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var content = reader.GetString(1);
            var metaStr = reader.IsDBNull(2) ? null : reader.GetString(2);
            var metadata = metaStr != null ? JsonSerializer.Deserialize<Dictionary<string, string>>(metaStr) : null;

            list.Add(Document.Create(id, content, metadata));
        }

        return list;
    }

    public async Task<IReadOnlyList<Chunk>> GetAllChunksAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var list = new List<Chunk>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand(@"
            SELECT Id, DocumentId, ChunkIndex, Content, StartOffset, EndOffset, MetadataJson
            FROM Chunks
            ORDER BY DocumentId, ChunkIndex ASC", connection);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var docId = reader.GetString(1);
            var chunkIdx = reader.GetInt32(2);
            var content = reader.GetString(3);
            var startOffset = reader.GetInt32(4);
            var endOffset = reader.GetInt32(5);
            var metaStr = reader.IsDBNull(6) ? null : reader.GetString(6);
            var metadata = metaStr != null ? JsonSerializer.Deserialize<Dictionary<string, string>>(metaStr) : null;

            list.Add(Chunk.Create(id, docId, content, chunkIdx, startOffset, endOffset, metadata));
        }

        return list;
    }

    public async Task<Document?> GetDocumentByIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand("SELECT Id, Content, MetadataJson FROM Documents WHERE Id = @id", connection);
        cmd.Parameters.AddWithValue("@id", documentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var content = reader.GetString(1);
            var metaStr = reader.IsDBNull(2) ? null : reader.GetString(2);
            var metadata = metaStr != null ? JsonSerializer.Deserialize<Dictionary<string, string>>(metaStr) : null;

            return Document.Create(id, content, metadata);
        }

        return null;
    }

    public async Task<IReadOnlyList<Chunk>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var list = new List<Chunk>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand(@"
            SELECT Id, DocumentId, ChunkIndex, Content, StartOffset, EndOffset, MetadataJson
            FROM Chunks
            WHERE DocumentId = @id
            ORDER BY ChunkIndex ASC", connection);
        cmd.Parameters.AddWithValue("@id", documentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var docId = reader.GetString(1);
            var chunkIdx = reader.GetInt32(2);
            var content = reader.GetString(3);
            var startOffset = reader.GetInt32(4);
            var endOffset = reader.GetInt32(5);
            var metaStr = reader.IsDBNull(6) ? null : reader.GetString(6);
            var metadata = metaStr != null ? JsonSerializer.Deserialize<Dictionary<string, string>>(metaStr) : null;

            list.Add(Chunk.Create(id, docId, content, chunkIdx, startOffset, endOffset, metadata));
        }

        return list;
    }

    public async Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand("DELETE FROM Documents WHERE Id = @id", connection);
        cmd.Parameters.AddWithValue("@id", documentId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqliteCommand("DELETE FROM Documents;", connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
