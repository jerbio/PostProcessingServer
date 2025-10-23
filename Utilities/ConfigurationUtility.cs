using System;
using System.Configuration;

namespace PostProcessingServer.Utilities
{
    /// <summary>
    /// Centralized configuration utility for PostProcessingServer.
    /// Contains all configuration keys, delimiters, and helper methods for accessing configuration values.
    /// </summary>
    public static class ConfigurationUtility
    {
        #region Configuration Keys
        
        /// <summary>
        /// Configuration key for allowed API keys in format: ApiKey1_key_and_secret_Secret1;ApiKey2_key_and_secret_Secret2
        /// </summary>
        public const string AllowedApiKeysKey = "AllowedApiKeys";

        /// <summary>
        /// Configuration key for request timeout in seconds (default: 300)
        /// </summary>
        public const string RequestTimeoutSecondsKey = "RequestTimeoutSeconds";

        /// <summary>
        /// Configuration key to enable/disable the background job processor
        /// </summary>
        public const string EnableJobProcessorKey = "EnableJobProcessor";

        /// <summary>
        /// Configuration key for maximum number of concurrent job processors
        /// </summary>
        public const string MaxConcurrentJobsKey = "MaxConcurrentJobs";

        /// <summary>
        /// Configuration key for job processing poll interval in milliseconds
        /// </summary>
        public const string JobPollIntervalMsKey = "JobPollIntervalMs";

        /// <summary>
        /// Configuration key for maximum job retry attempts
        /// </summary>
        public const string MaxJobRetriesKey = "MaxJobRetries";

        /// <summary>
        /// Configuration key for job timeout in seconds
        /// </summary>
        public const string JobTimeoutSecondsKey = "JobTimeoutSeconds";

        /// <summary>
        /// Configuration key for maximum queue size before rejecting new jobs
        /// </summary>
        public const string MaxQueueSizeKey = "MaxQueueSize";

        /// <summary>
        /// Configuration key for old job cleanup age in hours
        /// </summary>
        public const string JobCleanupAgeHoursKey = "JobCleanupAgeHours";

        /// <summary>
        /// Configuration key for cleanup interval in hours
        /// </summary>
        public const string CleanupIntervalHoursKey = "CleanupIntervalHours";

        /// <summary>
        /// Configuration key to enable detailed logging
        /// </summary>
        public const string EnableDetailedLoggingKey = "EnableDetailedLogging";

        /// <summary>
        /// Configuration key for performance metrics collection
        /// </summary>
        public const string EnablePerformanceMetricsKey = "EnablePerformanceMetrics";

        #endregion

        #region Delimiters and Separators

        /// <summary>
        /// Delimiter used to separate API key from secret in AllowedApiKeys configuration.
        /// Format: ApiKey_key_and_secret_SecretKey
        /// </summary>
        public const string ApiKeySecretDelimiter = "_key_and_secret_";

        /// <summary>
        /// Separator used between multiple API key-secret pairs in AllowedApiKeys configuration.
        /// Format: Key1_key_and_secret_Secret1;Key2_key_and_secret_Secret2
        /// </summary>
        public const string ApiKeyPairSeparator = ";";

        #endregion

        #region HTTP Headers

        /// <summary>
        /// Header name for API key authentication
        /// </summary>
        public const string ApiKeyHeader = "X-Api-Key";

        /// <summary>
        /// Header name for HMAC signature
        /// </summary>
        public const string SignatureHeader = "X-Signature";

        /// <summary>
        /// Header name for request timestamp (Unix milliseconds)
        /// </summary>
        public const string TimestampHeader = "X-Timestamp";

        /// <summary>
        /// Header name for request nonce (replay attack prevention)
        /// </summary>
        public const string NonceHeader = "X-Nonce";

        #endregion

        #region Default Values

        /// <summary>
        /// Default request timeout in seconds
        /// </summary>
        public const int DefaultRequestTimeoutSeconds = 300;

        /// <summary>
        /// Default maximum concurrent jobs
        /// </summary>
        public const int DefaultMaxConcurrentJobs = 5;

        /// <summary>
        /// Default job poll interval in milliseconds
        /// </summary>
        public const int DefaultJobPollIntervalMs = 1000;

        /// <summary>
        /// Default maximum job retries
        /// </summary>
        public const int DefaultMaxJobRetries = 3;

        /// <summary>
        /// Default job timeout in seconds
        /// </summary>
        public const int DefaultJobTimeoutSeconds = 300;

        /// <summary>
        /// Default maximum queue size
        /// </summary>
        public const int DefaultMaxQueueSize = 1000;

        /// <summary>
        /// Default job cleanup age in hours
        /// </summary>
        public const int DefaultJobCleanupAgeHours = 24;

        /// <summary>
        /// Default cleanup interval in hours
        /// </summary>
        public const int DefaultCleanupIntervalHours = 1;

        #endregion

        #region Configuration Helper Methods

        /// <summary>
        /// Gets a string configuration value with optional default.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key is not found or empty</param>
        /// <returns>Configuration value or default</returns>
        public static string GetString(string key, string defaultValue = null)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
            }
            catch (Exception ex)
            {
                LogError($"Error reading configuration key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Gets a required string configuration value. Throws if not found.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <returns>Configuration value</returns>
        /// <exception cref="ConfigurationErrorsException">Thrown when key is missing or empty</exception>
        public static string GetRequiredString(string key)
        {
            string value = GetString(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationErrorsException(
                    $"Required configuration key '{key}' is missing or empty in Web.config");
            }
            return value;
        }

        /// <summary>
        /// Gets a boolean configuration value.
        /// Supports: true/false, 1/0, yes/no, on/off (case-insensitive)
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key is not found or invalid</param>
        /// <returns>Boolean configuration value or default</returns>
        public static bool GetBool(string key, bool defaultValue = false)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                value = value.Trim().ToLowerInvariant();
                if (value == "true" || value == "1" || value == "yes" || value == "on")
                {
                    return true;
                }
                if (value == "false" || value == "0" || value == "no" || value == "off")
                {
                    return false;
                }

                LogWarning($"Invalid boolean value '{value}' for key '{key}', using default: {defaultValue}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                LogError($"Error reading boolean configuration key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Gets an integer configuration value with validation.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key is not found or invalid</param>
        /// <returns>Integer configuration value or default</returns>
        public static int GetInt(string key, int defaultValue = 0)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                if (int.TryParse(value, out int result))
                {
                    return result;
                }

                LogWarning($"Invalid integer value '{value}' for key '{key}', using default: {defaultValue}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                LogError($"Error reading integer configuration key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Gets a TimeSpan configuration value.
        /// Accepts format: hh:mm:ss or total seconds as number.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key is not found or invalid</param>
        /// <returns>TimeSpan configuration value or default</returns>
        public static TimeSpan GetTimeSpan(string key, TimeSpan defaultValue)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                // Try parsing as TimeSpan (hh:mm:ss)
                if (TimeSpan.TryParse(value, out TimeSpan result))
                {
                    return result;
                }

                // Try parsing as seconds
                if (double.TryParse(value, out double seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                LogWarning($"Invalid TimeSpan value '{value}' for key '{key}', using default: {defaultValue}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                LogError($"Error reading TimeSpan configuration key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a configuration key exists and has a non-empty value.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <returns>True if key exists with a non-empty value</returns>
        public static bool HasValue(string key)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a connection string by name.
        /// </summary>
        /// <param name="name">Connection string name</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>Connection string or default</returns>
        public static string GetConnectionString(string name, string defaultValue = null)
        {
            try
            {
                var connectionString = ConfigurationManager.ConnectionStrings[name];
                return connectionString?.ConnectionString ?? defaultValue;
            }
            catch (Exception ex)
            {
                LogError($"Error reading connection string '{name}': {ex.Message}");
                return defaultValue;
            }
        }

        #endregion

        #region Logging

        private static void LogError(string message)
        {
            System.Diagnostics.Trace.TraceError($"[PostProcessingServer.ConfigurationUtility] {message}");
        }

        private static void LogWarning(string message)
        {
            System.Diagnostics.Trace.TraceWarning($"[PostProcessingServer.ConfigurationUtility] {message}");
        }

        #endregion
    }
}
