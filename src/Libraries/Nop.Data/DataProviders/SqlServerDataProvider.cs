﻿using System;
using System.Data;
using System.Linq;
using System.Threading;
using LinqToDB.Data;
using Nop.Core;
using Nop.Core.Data;

namespace Nop.Data
{
    /// <summary>
    /// Represents SQL Server data provider
    /// </summary>
    public partial class SqlServerDataProvider : IDataProvider
    {
        public SqlServerDataProvider()
        {

        }

        #region Utilities

        /// <summary>
        /// Checks if the specified database exists, returns true if database exists
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        /// <returns>Returns true if the database exists.</returns>
        protected virtual bool SqlServerDatabaseExists()
        {
            try
            {
                //just try to connect
                using (var db = new DbNopCommerce())
                {
                    db.Connection.Open();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Methods

        public virtual void CreateDatabase(string collation, int triesToConnect = 10)
        {
            using (var db = new DbNopCommerce())
            {
                //parse database name
                var databaseName = db.Connection.Database;
                //now create connection string to 'master' dabatase. It always exists.
                db.Connection.ChangeDatabase("master");

                var query = $"CREATE DATABASE [{databaseName}]";
                if (!string.IsNullOrWhiteSpace(collation))
                    query = $"{query} COLLATE {collation}";

                db.Execute(query);

            }

            using (var db = new DbNopCommerce())
            {
                //try connect
                if (triesToConnect <= 0)
                    return;

                //sometimes on slow servers (hosting) there could be situations when database requires some time to be created.
                //but we have already started creation of tables and sample data.
                //as a result there is an exception thrown and the installation process cannot continue.
                //that's why we are in a cycle of "triesToConnect" times trying to connect to a database with a delay of one second.
                for (var i = 0; i <= triesToConnect; i++)
                {
                    if (i == triesToConnect)
                        throw new Exception("Unable to connect to the new database. Please try one more time");

                    if (!SqlServerDatabaseExists())
                        Thread.Sleep(1000);
                    else
                        break;
                }
            }
        }

        /// <summary>
        /// Initialize database
        /// </summary>
        public virtual void InitializeDatabase()
        {


            //var context = EngineContext.Current.Resolve<IDbContext>();

            ////check some of table names to ensure that we have nopCommerce 2.00+ installed
            //var tableNamesToValidate = new List<string> { "Customer", "Discount", "Order", "Product", "ShoppingCartItem" };
            //var existingTableNames = context
            //    .QueryFromSql<StringQueryType>("SELECT table_name AS Value FROM INFORMATION_SCHEMA.TABLES WHERE table_type = 'BASE TABLE'")
            //    .Select(stringValue => stringValue.Value).ToList();
            //var createTables = !existingTableNames.Intersect(tableNamesToValidate, StringComparer.InvariantCultureIgnoreCase).Any();
            //if (!createTables)
            //    return;

            //var fileProvider = EngineContext.Current.Resolve<INopFileProvider>();

            ////create tables
            ////EngineContext.Current.Resolve<IRelationalDatabaseCreator>().CreateTables();
            ////(context as DbContext).Database.EnsureCreated();
            //context.ExecuteSqlScript(context.GenerateCreateScript());

            ////create indexes
            //context.ExecuteSqlScriptFromFile(fileProvider.MapPath(NopDataDefaults.SqlServerIndexesFilePath));

            ////create stored procedures 
            //context.ExecuteSqlScriptFromFile(fileProvider.MapPath(NopDataDefaults.SqlServerStoredProceduresFilePath));
        }

        /// <summary>
        /// Creates a backup of the database
        /// </summary>
        public virtual void BackupDatabase(string fileName)
        {
            if (!BackupSupported)
                throw new DataException("This database does not support backup");

            using (var db = new DbNopCommerce())
            {
                db.Execute($"BACKUP DATABASE [{db.Connection.Database}] TO DISK = '{fileName}' WITH FORMAT");
            }
        }

        /// <summary>
        /// Gets command for restores the database from a backup
        /// </summary>
        /// <param name="backupFileName"></param>
        /// <returns></returns>
        public virtual void RestoreDatabase(string backupFileName)
        {
            if (!BackupSupported)
                throw new DataException("This database does not support backup");

            using (var db = new DbNopCommerce())
            {
                db.Execute($@"
                        DECLARE @ErrorMessage NVARCHAR(4000)
                        ALTER DATABASE [{db.Connection.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE
                        BEGIN TRY
                        RESTORE DATABASE [{db.Connection.Database}] FROM DISK = '{backupFileName}' WITH REPLACE
                        END TRY
                        BEGIN CATCH
                        SET @ErrorMessage = ERROR_MESSAGE()
                        END CATCH
                        ALTER DATABASE [{db.Connection.Database}] SET MULTI_USER WITH ROLLBACK IMMEDIATE
                        IF (@ErrorMessage is not NULL)
                        BEGIN
                        RAISERROR (@ErrorMessage, 16, 1)
                        END");
            }
        }

        /// <summary>
        /// Re-index database tables
        /// </summary>
        public virtual void ReIndexTables()
        {
            using (var db = new DbNopCommerce())
            {
                db.Execute($@"
                        DECLARE @TableName sysname 
                        DECLARE cur_reindex CURSOR FOR
                        SELECT table_name
                        FROM [{db.Connection.Database}].information_schema.tables
                        WHERE table_type = 'base table'
                        OPEN cur_reindex
                        FETCH NEXT FROM cur_reindex INTO @TableName
                        WHILE @@FETCH_STATUS = 0
                            BEGIN
                          exec('ALTER INDEX ALL ON [' + @TableName + '] REBUILD')
                                FETCH NEXT FROM cur_reindex INTO @TableName
                            END
                        CLOSE cur_reindex
                        DEALLOCATE cur_reindex");
            }
        }

        /// <summary>
        /// Get the current identity value
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <returns>Integer identity; null if cannot get the result</returns>
        public virtual int? GetTableIdent<T>() where T : BaseEntity
        {
            using (var db = new DbNopCommerce())
            {
                var tableName = db.GetTable<T>().TableName;

                var result = db.Query<decimal?>($"SELECT IDENT_CURRENT('[{tableName}]') as Value").FirstOrDefault();

                return result.HasValue ? Convert.ToInt32(result) : 1;
            }
        }

        /// <summary>
        /// Set table identity (is supported)
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <param name="ident">Identity value</param>
        public virtual void SetTableIdent<T>(int ident) where T : BaseEntity
        {
            var currentIdent = GetTableIdent<T>();
            if (!currentIdent.HasValue || ident <= currentIdent.Value)
                return;

            using (var db = new DbNopCommerce())
            {
                var tableName = db.GetTable<T>().TableName;
                db.Execute($"DBCC CHECKIDENT([{tableName}], RESEED, {ident})");
            }
        }

        /// <summary>
        /// Get a support database parameter object (used by stored procedures)
        /// </summary>
        /// <returns>Parameter</returns>
        public virtual DataParameter GetParameter()
        {
            return new DataParameter();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether this data provider supports backup
        /// </summary>
        public virtual bool BackupSupported => true;

        /// <summary>
        /// Gets a maximum length of the data for HASHBYTES functions, returns 0 if HASHBYTES function is not supported
        /// </summary>
        public virtual int SupportedLengthOfBinaryHash => 8000; //for SQL Server 2008 and above HASHBYTES function has a limit of 8000 characters.

        #endregion
    }
}