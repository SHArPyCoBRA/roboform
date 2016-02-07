// Copyright (C) 2016 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Dashlane
{
    public static class Import
    {
        // TODO: Not sure how to test this!
        public static string ImportUki(string username, string password)
        {
            return ImportUkiFromSettingsFile(FindSettingsFile(username), password);
        }

        public static string ImportUkiFromSettingsFile(string filename, string password)
        {
            return ImportUkiFromSettings(LoadSettingsFile(filename, password));
        }

        public static string ImportUkiFromSettings(string settingsXml)
        {
            try
            {
                return ImportUkiFromSettings(XDocument.Parse(settingsXml));
            }
            catch (XmlException e)
            {
                // TODO: Use custom exception!
                throw new InvalidOperationException("Invalid XML in the settings file", e);
            }
        }

        public static string ImportUkiFromSettings(XDocument settings)
        {
            var uki = settings.XPathSelectElement("/root/KWLocalSettingsManager/KWDataItem[@key='uki']");
            if (uki == null)
                // TODO: Use custom exception!
                throw new InvalidOperationException("The settings file doesn't contain an UKI");

            return uki.Value;
        }

        public static string LoadSettingsFile(string filename, string password)
        {
            var blob = File.ReadAllBytes(filename);
            return Parse.DecryptBlob(blob, password).ToUtf8();
        }

        // TODO: Not sure how to test this!
        public static string FindSettingsFile(string username)
        {
            // TODO: Are there other platforms besides Windows desktop we need to check on?

            var filename = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dashlane",
                "profiles",
                username,
                "Settings",
                "localSettings.aes");

            if (!File.Exists(filename))
                // TODO: Use custom exception!
                throw new InvalidOperationException(
                    string.Format("Profile '{0}' doesn't exist", username));

            return filename;
        }
    }
}
