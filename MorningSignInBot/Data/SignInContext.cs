using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using System;
using System.IO;

namespace MorningSignInBot.Data
{
    public class SignInContext : DbContext
    {
        public DbSet<SignInEntry> SignIns { get; set; }

        private readonly string _dbPath;

        public SignInContext(IOptions<DatabaseSettings> dbSettings, ILogger<SignInContext> logger)
        {
            string relativePath = dbSettings.Value.Path;
            string basePath = AppContext.BaseDirectory;
            _dbPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

            string? directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    logger.LogInformation("Created database directory: {Directory}", directory);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create database directory: {Directory}", directory);
                    throw;
                }
            }
            logger.LogTrace("Database path configured to: {DbPath}", _dbPath);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={_dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SignInEntry>().HasIndex(e => e.Timestamp);
            modelBuilder.Entity<SignInEntry>().HasIndex(e => e.UserId);
        }
    }
}