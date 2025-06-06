-- Create USERS table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[USERS]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[USERS] (
        [USER_ID] INT IDENTITY(1,1) PRIMARY KEY,
        [USERNAME] NVARCHAR(100) NOT NULL UNIQUE,
        [EMAIL] NVARCHAR(255) NOT NULL UNIQUE,
        [PASSWORD_HASH] NVARCHAR(255) NOT NULL,
        [FULL_NAME] NVARCHAR(200) NOT NULL,
        [ROLE_ID] INT NOT NULL,
        [IS_ACTIVE] BIT NOT NULL DEFAULT(1),
        [CREATED_AT] DATETIME NOT NULL DEFAULT(GETDATE()),
        [LAST_LOGIN] DATETIME NULL
    );

    CREATE NONCLUSTERED INDEX [IX_USERS_USERNAME] ON [dbo].[USERS]([USERNAME]);
    CREATE NONCLUSTERED INDEX [IX_USERS_EMAIL] ON [dbo].[USERS]([EMAIL]);
    CREATE NONCLUSTERED INDEX [IX_USERS_ROLE_ID] ON [dbo].[USERS]([ROLE_ID]);

    PRINT 'USERS table created successfully.';
END
ELSE
BEGIN
    PRINT 'USERS table already exists.';
END 