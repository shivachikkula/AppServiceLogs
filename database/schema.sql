-- OSSE Application Log Viewer: which user may view which application.
-- AppKey is the name of the Key Vault secret that holds the application's Application Insights connection string.

IF OBJECT_ID(N'dbo.UserApplications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserApplications
    (
        Id              INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_UserApplications PRIMARY KEY,
        Email           NVARCHAR(256)      NOT NULL,  -- store in lower case; matched against the B2C email claim
        ApplicationName NVARCHAR(200)      NOT NULL,  -- shown in the application dropdown
        AppKey          NVARCHAR(127)      NOT NULL,  -- Key Vault secret name: letters, digits and dashes only
        CreatedAt       DATETIME2          NOT NULL CONSTRAINT DF_UserApplications_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_UserApplications_Email_AppKey UNIQUE (Email, AppKey),
        CONSTRAINT CK_UserApplications_AppKey CHECK (AppKey NOT LIKE '%[^0-9a-zA-Z-]%')
    );
END;
GO

-- Example rows (replace with your users and applications):
-- INSERT INTO dbo.UserApplications (Email, ApplicationName, AppKey) VALUES
--     (N'jane.doe@contoso.com', N'Orders API',   N'orders-api-appinsights'),
--     (N'jane.doe@contoso.com', N'Payments API', N'payments-api-appinsights');

-- Let the API's managed identity read the table (run in the application database, as an Entra admin):
-- CREATE USER [<app-service-name>] FROM EXTERNAL PROVIDER;
-- ALTER ROLE db_datareader ADD MEMBER [<app-service-name>];
