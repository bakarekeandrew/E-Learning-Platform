-- Create SYSTEM_METRICS table for storing system performance data
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SYSTEM_METRICS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SYSTEM_METRICS] (
        [MetricID] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [Timestamp] DATETIME2(0) NOT NULL DEFAULT GETUTCDATE(),
        [CPUUsage] DECIMAL(5,2) NOT NULL,
        [MemoryUsage] DECIMAL(5,2) NOT NULL,
        [DatabaseConnections] INT NOT NULL,
        [ResponseTime] INT NOT NULL, -- in milliseconds
        [RequestsPerMinute] INT NOT NULL DEFAULT 0,
        [ActiveSessions] INT NOT NULL DEFAULT 0,
        [Notes] NVARCHAR(500) NULL
    );

    -- Create index for efficient time-based queries
    CREATE NONCLUSTERED INDEX [IX_SYSTEM_METRICS_Timestamp] 
    ON [dbo].[SYSTEM_METRICS] ([Timestamp] DESC);
END

-- Create ERROR_METRICS table for storing error rate statistics
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ERROR_METRICS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ERROR_METRICS] (
        [MetricID] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [Timestamp] DATETIME2(0) NOT NULL DEFAULT GETUTCDATE(),
        [ErrorRate] DECIMAL(5,4) NOT NULL,
        [TotalRequests] INT NOT NULL,
        [ErrorCount] INT NOT NULL,
        [TimeWindow] VARCHAR(20) NOT NULL DEFAULT '1h', -- e.g., '1h', '24h', '7d'
        [Notes] NVARCHAR(500) NULL
    );

    -- Create index for efficient time-based queries
    CREATE NONCLUSTERED INDEX [IX_ERROR_METRICS_Timestamp] 
    ON [dbo].[ERROR_METRICS] ([Timestamp] DESC);
END

-- Create ERROR_LOGS table for storing detailed error information
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ERROR_LOGS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ERROR_LOGS] (
        [LogID] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [Timestamp] DATETIME2(0) NOT NULL DEFAULT GETUTCDATE(),
        [ErrorType] VARCHAR(50) NOT NULL,
        [Severity] VARCHAR(20) NOT NULL DEFAULT 'Error', -- Info, Warning, Error, Critical
        [Path] NVARCHAR(500) NOT NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [StackTrace] NVARCHAR(MAX) NULL,
        [UserID] INT NULL,
        [RequestData] NVARCHAR(MAX) NULL,
        [Resolution] NVARCHAR(500) NULL,
        [IsResolved] BIT NOT NULL DEFAULT 0
    );

    -- Create indexes for efficient querying
    CREATE NONCLUSTERED INDEX [IX_ERROR_LOGS_Timestamp] 
    ON [dbo].[ERROR_LOGS] ([Timestamp] DESC);
    
    CREATE NONCLUSTERED INDEX [IX_ERROR_LOGS_ErrorType] 
    ON [dbo].[ERROR_LOGS] ([ErrorType]);
    
    CREATE NONCLUSTERED INDEX [IX_ERROR_LOGS_Severity] 
    ON [dbo].[ERROR_LOGS] ([Severity]);
END

-- Create stored procedure to clean up old monitoring data
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CleanupMonitoringData]') AND type in (N'P'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[CleanupMonitoringData]
        @DaysToKeep INT = 90
    AS
    BEGIN
        SET NOCOUNT ON;
        
        DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysToKeep, GETUTCDATE());
        
        -- Delete old system metrics
        DELETE FROM [dbo].[SYSTEM_METRICS]
        WHERE [Timestamp] < @CutoffDate;
        
        -- Delete old error metrics
        DELETE FROM [dbo].[ERROR_METRICS]
        WHERE [Timestamp] < @CutoffDate;
        
        -- Delete old error logs (keep critical errors longer)
        DELETE FROM [dbo].[ERROR_LOGS]
        WHERE [Timestamp] < @CutoffDate
        AND [Severity] != ''Critical'';
        
        -- Delete old critical errors
        DELETE FROM [dbo].[ERROR_LOGS]
        WHERE [Timestamp] < DATEADD(DAY, -(@DaysToKeep * 2), GETUTCDATE())
        AND [Severity] = ''Critical'';
    END
    ')
END

-- Create stored procedure to insert system metrics
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InsertSystemMetrics]') AND type in (N'P'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[InsertSystemMetrics]
        @CPUUsage DECIMAL(5,2),
        @MemoryUsage DECIMAL(5,2),
        @DatabaseConnections INT,
        @ResponseTime INT,
        @RequestsPerMinute INT,
        @ActiveSessions INT,
        @Notes NVARCHAR(500) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        
        INSERT INTO [dbo].[SYSTEM_METRICS]
            ([CPUUsage], [MemoryUsage], [DatabaseConnections], 
             [ResponseTime], [RequestsPerMinute], [ActiveSessions], [Notes])
        VALUES
            (@CPUUsage, @MemoryUsage, @DatabaseConnections, 
             @ResponseTime, @RequestsPerMinute, @ActiveSessions, @Notes);
    END
    ')
END

-- Create stored procedure to insert error metrics
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InsertErrorMetrics]') AND type in (N'P'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[InsertErrorMetrics]
        @ErrorRate DECIMAL(5,4),
        @TotalRequests INT,
        @ErrorCount INT,
        @TimeWindow VARCHAR(20) = ''1h'',
        @Notes NVARCHAR(500) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        
        INSERT INTO [dbo].[ERROR_METRICS]
            ([ErrorRate], [TotalRequests], [ErrorCount], [TimeWindow], [Notes])
        VALUES
            (@ErrorRate, @TotalRequests, @ErrorCount, @TimeWindow, @Notes);
    END
    ')
END

-- Create stored procedure to log errors
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LogError]') AND type in (N'P'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[LogError]
        @ErrorType VARCHAR(50),
        @Severity VARCHAR(20),
        @Path NVARCHAR(500),
        @Message NVARCHAR(MAX),
        @StackTrace NVARCHAR(MAX) = NULL,
        @UserID INT = NULL,
        @RequestData NVARCHAR(MAX) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        
        INSERT INTO [dbo].[ERROR_LOGS]
            ([ErrorType], [Severity], [Path], [Message], 
             [StackTrace], [UserID], [RequestData])
        VALUES
            (@ErrorType, @Severity, @Path, @Message, 
             @StackTrace, @UserID, @RequestData);
    END
    ')
END 