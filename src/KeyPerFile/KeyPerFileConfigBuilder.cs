﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;

namespace Microsoft.Configuration.ConfigurationBuilders
{
    /// <summary>
    /// A ConfigurationProvider that uses a directory's files as a source of values. A file's name is the key, and the contents are the value.
    /// </summary>
    public class KeyPerFileConfigBuilder : KeyValueConfigBuilder
    {
        #pragma warning disable CS1591 // No xml comments for tag literals.
        public const string directoryPathTag = "directoryPath";
        public const string keyDelimiterTag = "keyDelimiter";
        public const string ignorePrefixTag = "ignorePrefix";
        public const string optionalTag = "optional";
        #pragma warning restore CS1591 // No xml comments for tag literals.

        /// <summary>
        /// Gets or sets a path to the source directory to look in for values.
        /// </summary>
        public string DirectoryPath { get; protected set; }
        /// <summary>
        /// If specified, the config builder will traverse multiple levels of the directory, building key names with this delimeter.
        /// If null, the config builder only looks at the top-level of the directory. This is the default.
        /// </summary>
        public string KeyDelimiter { get; protected set; }
        /// <summary>
        /// Gets or sets a prefix string. Files that start with this prefix will be excluded.
        /// Defaults to "ignore.".
        /// </summary>
        public string IgnorePrefix { get; protected set; }
        /// <summary>
        /// Specifies whether the config builder should cause errors if the source directory doesn't exist.
        /// Defaults to false.
        /// </summary>
        public bool Optional { get; protected set; }

        private ConcurrentDictionary<string, string> _allValues = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes the configuration builder.
        /// </summary>
        /// <param name="name">The friendly name of the provider.</param>
        /// <param name="config">A collection of the name/value pairs representing builder-specific attributes specified in the configuration for this provider.</param>
        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            bool optional;
            Optional = (Boolean.TryParse(config?[optionalTag], out optional)) ? optional : false;

            string directoryPath = config?[directoryPathTag];
            DirectoryPath = Utils.MapPath(directoryPath);
            if (!Optional && (String.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath)))
            {
                throw new ArgumentException($"'directoryPath' does not exist.");
            }

            IgnorePrefix = config?[ignorePrefixTag] ?? "ignore.";

            // The Core KeyPerFile config provider does not do multi-level.
            // If KeyDelimiter is null, do single-level. Otherwise, multi-level.
            KeyDelimiter = config?[keyDelimiterTag];
        }

        /// <summary>
        /// Retrieves all known key/value pairs from the secrets file where the key begins with with <paramref name="prefix"/>.
        /// </summary>
        /// <param name="prefix">A prefix string to filter the list of potential keys retrieved from the source.</param>
        /// <returns>A collection of key/value pairs.</returns>
        public override ICollection<KeyValuePair<string, string>> GetAllValues(string prefix)
        {
            ReadAllValues(DirectoryPath, "", _allValues);
            return _allValues?.Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Looks up a single 'value' for the given 'key.'
        /// </summary>
        /// <param name="key">The 'key' to look up in the secrets file. (Prefix handling is not needed here.)</param>
        /// <returns>The value corresponding to the given 'key' or null if no value is found.</returns>
        public override string GetValue(string key)
        {
            if (_allValues != null && _allValues.TryGetValue(key, out string val))
                return val;

            return null;
        }

        private IDictionary<string, string> ReadAllValues(string root, string prefix, IDictionary<string, string> values)
        {
            if (values == null)
                values = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            DirectoryInfo di = new DirectoryInfo(root);

            // Only do multi-level if KeyDelimiter is non-null.
            // When doing multi-level, go depth-first, to give priority to the root level in event of a collision.
            if (KeyDelimiter != null)
            {
                foreach (var sub in di.EnumerateDirectories())
                {
                    if (!String.IsNullOrWhiteSpace(IgnorePrefix) && sub.Name.StartsWith(IgnorePrefix))
                        continue;

                    ReadAllValues(sub.FullName, sub.Name + KeyDelimiter, values);
                }
            }

            foreach (var file in di.EnumerateFiles())
            {
                if (!String.IsNullOrWhiteSpace(IgnorePrefix) && file.Name.StartsWith(IgnorePrefix))
                    continue;

                string key = prefix + file.Name;
                string val = ReadValueFromFile(file.FullName);
                values.Add(key, val);
            }

            return values;
        }

        private string ReadValueFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            string val = File.ReadAllText(filePath);
            while (val.EndsWith(Environment.NewLine))
                val = val.Substring(0, val.Length - Environment.NewLine.Length);

            return val;
        }
    }
}