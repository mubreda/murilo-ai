using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class SqliteImageProcessingJobStore : IImageProcessingJobStore, IDisposable
{
    private readonly string _connectionString;

    public SqliteImageProcessingJobStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetValue<string>("ImageJobs:SqliteConnectionString") ?? "Data Source=jobs.db";
    }

    public async Task<ImageProcessingJob> CreateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ImageProcessingJobs (
                Id, Status, CreatedAt, StartedAt, CompletedAt,
                Engine, OriginalFileName, ContentType, OptionsJson, CorrelationId,
                InputPath, WorkingDirectory, OutputPath, ErrorMessage, Progress
            ) VALUES (
                $id, $status, $createdAt, $startedAt, $completedAt,
                $engine, $originalFileName, $contentType, $optionsJson, $correlationId,
                $inputPath, $workingDirectory, $outputPath, $errorMessage, $progress
            );";

        AddParameter(command, "$id", job.Id);
        AddParameter(command, "$status", job.Status.ToString());
        AddParameter(command, "$createdAt", job.CreatedAt.UtcDateTime);
        AddParameter(command, "$startedAt", job.StartedAt?.UtcDateTime);
        AddParameter(command, "$completedAt", job.CompletedAt?.UtcDateTime);
        AddParameter(command, "$engine", job.Engine);
        AddParameter(command, "$originalFileName", job.OriginalFileName);
        AddParameter(command, "$contentType", job.ContentType);
        AddParameter(command, "$optionsJson", job.OptionsJson);
        AddParameter(command, "$correlationId", job.CorrelationId);
        AddParameter(command, "$inputPath", job.InputPath);
        AddParameter(command, "$workingDirectory", job.WorkingDirectory);
        AddParameter(command, "$outputPath", job.OutputPath);
        AddParameter(command, "$errorMessage", job.ErrorMessage);
        AddParameter(command, "$progress", job.Progress);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return job;
    }

    public async Task<ImageProcessingJob?> GetByIdAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Status, CreatedAt, StartedAt, CompletedAt,
                   Engine, OriginalFileName, ContentType, OptionsJson, CorrelationId,
                   InputPath, WorkingDirectory, OutputPath, ErrorMessage, Progress
            FROM ImageProcessingJobs
            WHERE Id = $id;";
        AddParameter(command, "$id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpdateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ImageProcessingJobs
            SET Status = $status,
                StartedAt = $startedAt,
                CompletedAt = $completedAt,
                Engine = $engine,
                OriginalFileName = $originalFileName,
                ContentType = $contentType,
                OptionsJson = $optionsJson,
                CorrelationId = $correlationId,
                InputPath = $inputPath,
                WorkingDirectory = $workingDirectory,
                OutputPath = $outputPath,
                ErrorMessage = $errorMessage,
                Progress = $progress
            WHERE Id = $id;";

        AddParameter(command, "$id", job.Id);
        AddParameter(command, "$status", job.Status.ToString());
        AddParameter(command, "$startedAt", job.StartedAt?.UtcDateTime);
        AddParameter(command, "$completedAt", job.CompletedAt?.UtcDateTime);
        AddParameter(command, "$engine", job.Engine);
        AddParameter(command, "$originalFileName", job.OriginalFileName);
        AddParameter(command, "$contentType", job.ContentType);
        AddParameter(command, "$optionsJson", job.OptionsJson);
        AddParameter(command, "$correlationId", job.CorrelationId);
        AddParameter(command, "$inputPath", job.InputPath);
        AddParameter(command, "$workingDirectory", job.WorkingDirectory);
        AddParameter(command, "$outputPath", job.OutputPath);
        AddParameter(command, "$errorMessage", job.ErrorMessage);
        AddParameter(command, "$progress", job.Progress);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ImageProcessingJob>> ListNonFinalAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Status, CreatedAt, StartedAt, CompletedAt,
                   Engine, OriginalFileName, ContentType, OptionsJson, CorrelationId,
                   InputPath, WorkingDirectory, OutputPath, ErrorMessage, Progress
            FROM ImageProcessingJobs
            WHERE Status IN ('Queued', 'Processing')
            ORDER BY CreatedAt ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var jobs = new List<ImageProcessingJob>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(Map(reader));
        }

        return jobs;
    }

    public void Dispose()
    {
    }

    private async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString)
        {
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS ImageProcessingJobs (
                Id TEXT PRIMARY KEY,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                CompletedAt TEXT NULL,
                Engine TEXT NULL,
                OriginalFileName TEXT NULL,
                ContentType TEXT NULL,
                OptionsJson TEXT NULL,
                CorrelationId TEXT NULL,
                InputPath TEXT NULL,
                WorkingDirectory TEXT NULL,
                OutputPath TEXT NULL,
                ErrorMessage TEXT NULL,
                Progress INTEGER NOT NULL
            );";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static ImageProcessingJob Map(DbDataReader reader)
    {
        return new ImageProcessingJob
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Status = Enum.Parse<ImageProcessingJobStatus>(reader.GetString(reader.GetOrdinal("Status"))),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            StartedAt = reader.IsDBNull(reader.GetOrdinal("StartedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("StartedAt"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CompletedAt"))),
            Engine = reader.IsDBNull(reader.GetOrdinal("Engine")) ? null : reader.GetString(reader.GetOrdinal("Engine")),
            OriginalFileName = reader.IsDBNull(reader.GetOrdinal("OriginalFileName")) ? null : reader.GetString(reader.GetOrdinal("OriginalFileName")),
            ContentType = reader.IsDBNull(reader.GetOrdinal("ContentType")) ? null : reader.GetString(reader.GetOrdinal("ContentType")),
            OptionsJson = reader.IsDBNull(reader.GetOrdinal("OptionsJson")) ? null : reader.GetString(reader.GetOrdinal("OptionsJson")),
            CorrelationId = reader.IsDBNull(reader.GetOrdinal("CorrelationId")) ? null : reader.GetString(reader.GetOrdinal("CorrelationId")),
            InputPath = reader.IsDBNull(reader.GetOrdinal("InputPath")) ? null : reader.GetString(reader.GetOrdinal("InputPath")),
            WorkingDirectory = reader.IsDBNull(reader.GetOrdinal("WorkingDirectory")) ? null : reader.GetString(reader.GetOrdinal("WorkingDirectory")),
            OutputPath = reader.IsDBNull(reader.GetOrdinal("OutputPath")) ? null : reader.GetString(reader.GetOrdinal("OutputPath")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            Progress = reader.GetInt32(reader.GetOrdinal("Progress"))
        };
    }
}
