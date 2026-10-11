-- Lets the web app's managed identity (and its staging slot's) use the database. Safe to run repeatedly.
-- Run as a member of the SQL server's Microsoft Entra admin group, after schema.sql, e.g.:
--   sqlcmd -S <server>.database.windows.net -d logviewer --authentication-method ActiveDirectoryDefault \
--          -v AppName="osse-logviewer-prod-app" SlotName="osse-logviewer-prod-app/slots/staging" -i grant-app-identity.sql
-- Leave SlotName empty when there is no staging slot.

DECLARE @identities TABLE (Name SYSNAME);
INSERT INTO @identities (Name) VALUES (N'$(AppName)');
IF N'$(SlotName)' <> N'' INSERT INTO @identities (Name) VALUES (N'$(SlotName)');

DECLARE @name SYSNAME, @sql NVARCHAR(MAX);
DECLARE identities CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @identities;
OPEN identities;
FETCH NEXT FROM identities INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@name) + N' FROM EXTERNAL PROVIDER;';
        EXEC sp_executesql @sql;
    END;
    SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@name) + N';' +
               N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@name) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM identities INTO @name;
END;
CLOSE identities;
DEALLOCATE identities;
GO
